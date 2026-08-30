using System.IO;
using WireMarkerInspection.Application;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using Xunit;

namespace WireMarkerInspection.Tests;

[Collection(DispatcherTestHost.Collection)]
public sealed class TimingAndRecoveryTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-timing-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public void DurationsUseAMonotonicClockAndCannotGoBackwards()
    {
        var start=MonotonicClock.Now;
        Assert.True(MonotonicClock.MillisecondsSince(start)>=0);
        // A wall-clock correction moving time backwards would produce a negative interval with UtcNow.
        Assert.Equal(0,MonotonicClock.Milliseconds(start,start-1_000_000));
        Assert.True(MonotonicClock.Milliseconds(start,start+System.Diagnostics.Stopwatch.Frequency)>=999);
    }

    [Fact]
    public async Task EachEndAndCycleReportsItsOwnMeasuredStages()
    {
        var sink=new ResultSink();
        var session=new InspectionSession(new SlowOcr(TimeSpan.FromMilliseconds(30)),sink);
        session.Begin(Recipe());

        var first=await session.AcceptAsync(Frame(),frameAgeMs:12.5);
        var second=await session.AcceptAsync(Frame());

        Assert.NotNull(first);Assert.NotNull(second);
        Assert.Equal(12.5,first!.MillisecondsOf("frame-age"));
        Assert.True(first.MillisecondsOf("ocr")>=25,$"OCR was measured as {first.MillisecondsOf("ocr")} ms.");
        Assert.True(first.MillisecondsOf("end")>=first.MillisecondsOf("ocr"));
        Assert.All(first.Timings!,stage=>Assert.True(stage.Milliseconds>=0));

        var product=Assert.Single(sink.Products);
        var cycle=Assert.Single(product.Timings!,t=>t.Stage=="cycle");
        // The written result must carry the cycle time; it is measured before the file is saved.
        Assert.True(cycle.Milliseconds>=first.MillisecondsOf("end")+second!.MillisecondsOf("end")-1);
        Assert.True(session.LastPersistMilliseconds>=0);
    }

    [Fact]
    public async Task LosingTheCameraMidCycleDiscardsTheProductInsteadOfContinuingIt()
    {
        var sink=new ResultSink();
        var session=new InspectionSession(new SlowOcr(TimeSpan.Zero),sink);
        session.Begin(Recipe());
        await session.AcceptAsync(Frame());
        Assert.Equal(InspectionState.WaitingEnd2,session.State);

        Assert.True(session.Fault("Mất kết nối camera giữa chu kỳ."));

        Assert.Equal(InspectionState.Faulted,session.State);
        Assert.Empty(session.EndResults);
        // The next frame must not be filed as end 2 of the interrupted product.
        await Assert.ThrowsAsync<InvalidOperationException>(()=>session.AcceptAsync(Frame()));
        Assert.Empty(sink.Products);
        Assert.False(session.Fault("again"));   // an already faulted cycle is not faulted twice

        session.Begin(Recipe());
        Assert.Equal(InspectionState.WaitingEnd1,session.State);
        await session.AcceptAsync(Frame());
        await session.AcceptAsync(Frame());
        Assert.Single(sink.Products);
    }

    [Fact]
    public void RollingCycleStatisticsReportSpreadNotJustAnAverage()
    {
        var stats=new CycleStatistics(4);
        Assert.Equal((0,0,0,0),stats.Summary());
        foreach(var value in new double[]{100,200,300,400})stats.Add(value);

        var (count,average,p95,max)=stats.Summary();
        Assert.Equal(4,count);
        Assert.Equal(250,average);
        Assert.Equal(400,p95);
        Assert.Equal(400,max);
        Assert.Equal(400,stats.Last);

        stats.Add(50);   // the window is bounded, so the oldest value drops out
        Assert.Equal(4,stats.Summary().Count);
        Assert.Equal(50,stats.Last);
        Assert.Equal(237.5,stats.Summary().Average);

        Assert.Throws<ArgumentOutOfRangeException>(()=>stats.Add(-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new CycleStatistics(0));
    }

    [Fact]
    public void AcquisitionRecoversFromALostCameraAndCountsTheReconnect()=>DispatcherTestHost.Sta(()=>
    {
        var camera=new FlakyCamera(failAfter:3);
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,TimeSpan.FromSeconds(1));
        try
        {
            DispatcherTestHost.Wait(vm.InitializeCameraAsync());
            DispatcherTestHost.Wait(vm.ConnectCommand.ExecuteAsync(null));
            DispatcherTestHost.Wait(vm.AcquisitionCommand.ExecuteAsync(null));

            DispatcherTestHost.Pump(()=>vm.CameraState==CameraUiState.Reconnecting,TimeSpan.FromSeconds(20),
                "Acquisition never reported the lost camera.");
            DispatcherTestHost.Pump(()=>vm.CameraState==CameraUiState.Acquiring&&vm.Diagnostics.Snapshot().Reconnects>0,
                TimeSpan.FromSeconds(20),"Acquisition never recovered.");

            var snapshot=vm.Diagnostics.Snapshot();
            Assert.True(snapshot.Reconnects>=1);
            Assert.True(snapshot.Frames>3);
            Assert.True(camera.Reopens>=1);
            Assert.True(vm.Acquiring);            // recovery keeps acquisition running
            Assert.Contains("reconnect",vm.AcquisitionSummary,StringComparison.OrdinalIgnoreCase);
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    private static ImageFrame Frame()=>new(100,100,300,new byte[30000],Guid.NewGuid(),DateTimeOffset.UtcNow,"TEST");
    private static EndRecipe End()=>new("",100,100,SearchRoi.FullImage(100,100),["A"],TextOrientation.Auto);
    private static Recipe Recipe()=>new(Guid.NewGuid(),"M-TIME","Timing",1,[End(),End()],DateTimeOffset.UtcNow);

    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    private sealed class SlowOcr(TimeSpan delay):IOcrEngine
    {
        public async Task<OcrReading> ReadAsync(ImageFrame frame,EndRecipe recipe,CancellationToken token)
        {
            if(delay>TimeSpan.Zero)await Task.Delay(delay,token);
            return new([new("A",0.99,[],[])],0);
        }
    }

    private sealed class ResultSink:IResultStore
    {
        public List<ProductResult> Products{get;}=[];
        public Task SaveAsync(ProductResult result,ImageFrame[] frames,CancellationToken token)
        {
            Products.Add(result);return Task.CompletedTask;
        }
    }

    /// <summary>Delivers frames, drops the connection once, then works again after being reopened.</summary>
    private sealed class FlakyCamera(int failAfter):ICamera
    {
        private readonly object gate=new();
        private int grabs;
        private bool failed;
        private bool tripped;
        private bool grabbing;
        public int Reopens{get;private set;}
        public IReadOnlyList<CameraDevice> Enumerate()=>[new("flaky","Flaky camera","test",false)];
        public void Open(CameraDevice device){lock(gate){if(failed){failed=false;Reopens++;}}}
        public CameraInfo ReadInfo()=>new("FLAKY","SN-FLAKY","Mono8",40,20,30,null);
        public IReadOnlyList<CameraParameterInfo> DescribeParameters()=>
            [new("ExposureTime","us",10,100000,0,10000,true),new("Gain","dB",0,20,0,0,true)];
        public CameraSettings ReadSettings()=>new(10000,0);
        public void ApplySettings(CameraSettings settings){}
        public void Start(){lock(gate)grabbing=true;}
        public ImageFrame Grab(int timeoutMs)
        {
            lock(gate)
            {
                if(!grabbing)throw new InvalidOperationException("Acquisition has not started.");
                // The link drops exactly once, so recovery has to be what makes frames flow again.
                if(!tripped&&grabs>=failAfter){tripped=true;failed=true;throw new IOException("Camera link lost.");}
                grabs++;
                var stride=40*3;
                return new(40,20,stride,new byte[stride*20],Guid.NewGuid(),DateTimeOffset.UtcNow,"FLAKY");
            }
        }
        public void Stop(){lock(gate)grabbing=false;}
        public void Close(){}
        public void Dispose(){}
    }
}
