using System.IO;
using WireMarkerInspection.Application;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using Xunit;

namespace WireMarkerInspection.Tests;

public sealed class GrabReferenceTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-grab-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public void GrabIsOfferedOnlyWithAModelDraftAndAFreshLiveFrame()=>DispatcherTestHost.Sta(()=>
    {
        var camera=new LiveCamera();
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,TimeSpan.FromSeconds(1));
        try
        {
            Assert.False(vm.CanGrabReference);
            Assert.False(vm.GrabReferenceCommand.CanExecute("1"));

            Connect(vm);
            vm.NewModelCommand.Execute(new ModelIdentity("GRAB-1","Grab availability"));
            Assert.True(vm.CanConfigureModel);
            Assert.False(vm.CanGrabReference);

            Start(vm);
            Assert.True(vm.CanGrabReference);
            Assert.True(vm.GrabReferenceCommand.CanExecute("1"));

            DispatcherTestHost.Wait(vm.StopAcquisitionAsync());
            Assert.False(vm.CanGrabReference);
            Assert.False(vm.GrabReferenceCommand.CanExecute("2"));

            // A blocked grab must explain itself instead of silently doing nothing.
            vm.GrabReferenceCommand.Execute("2");
            Assert.Null(vm.End2.Frame);
            Assert.Contains("Start Acquisition",vm.Message);
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    [Fact]
    public void GrabStoresAnIndependentCopyOfTheLiveFramePerEnd()=>DispatcherTestHost.Sta(()=>
    {
        var camera=new LiveCamera();
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,TimeSpan.FromSeconds(1));
        try
        {
            Connect(vm);
            vm.NewModelCommand.Execute(new ModelIdentity("GRAB-2","Grab copy"));
            Start(vm);

            vm.End1.Roi=new(RoiShape.Rectangle,[new(1,1),new(20,10)]);
            vm.Dirty=false;
            vm.GrabReferenceCommand.Execute("1");
            var first=vm.End1.Frame;
            Assert.NotNull(first);
            Assert.Equal(LiveCamera.FrameWidth,first!.Width);
            Assert.Equal(LiveCamera.FrameHeight,first.Height);
            Assert.NotNull(vm.End1.Image);
            Assert.Null(vm.End1.Roi);          // a new reference image clears the previous ROI
            Assert.False(vm.End1.Applied);
            Assert.True(vm.Dirty);

            var grabs=camera.Grabs;
            DispatcherTestHost.Pump(()=>camera.Grabs>grabs,TimeSpan.FromSeconds(10),"The camera produced no further live frame.");
            vm.GrabReferenceCommand.Execute("2");
            var second=vm.End2.Frame;
            Assert.NotNull(second);

            Assert.NotSame(first,second);
            Assert.NotSame(first.Bgr,second!.Bgr);
            Assert.NotEqual(first.Id,second.Id);
            Assert.DoesNotContain(camera.Buffers,buffer=>ReferenceEquals(buffer,first.Bgr)||ReferenceEquals(buffer,second.Bgr));

            DispatcherTestHost.Wait(vm.StopAcquisitionAsync());
            Assert.NotNull(vm.End1.Frame);     // stopping acquisition never drops captured references
            Assert.NotNull(vm.End2.Frame);
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    private static void Connect(MainViewModel vm)
    {
        DispatcherTestHost.Wait(vm.InitializeCameraAsync());
        DispatcherTestHost.Wait(vm.ConnectCommand.ExecuteAsync(null));
        Assert.True(vm.CameraConnected);
    }

    private static void Start(MainViewModel vm)
    {
        DispatcherTestHost.Wait(vm.AcquisitionCommand.ExecuteAsync(null));
        Assert.True(vm.Acquiring);
        DispatcherTestHost.Pump(()=>vm.CanGrabReference,TimeSpan.FromSeconds(10),"The live frame never reached the view model.");
    }

    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    private sealed class LiveCamera:ICamera
    {
        public const int FrameWidth=40;
        public const int FrameHeight=20;
        private readonly object gate=new();
        private readonly List<byte[]> buffers=[];
        private int grabs;
        private bool grabbing;
        public int Grabs{get{lock(gate)return grabs;}}
        public byte[][] Buffers{get{lock(gate)return [..buffers];}}
        public IReadOnlyList<CameraDevice> Enumerate()=>[new("live-fake","Fake live camera","test",false)];
        public void Open(CameraDevice device){}
        public CameraSettings? Applied{get;private set;}
        public CameraInfo ReadInfo()=>new("FAKE-MODEL","SN-FAKE","Mono8",FrameWidth,FrameHeight,30,35);
        public IReadOnlyList<CameraParameterInfo> DescribeParameters()=>
        [
            new("ExposureTime","us",10,1000000,0,10000,true),
            new("Gain","dB",0,20,0,0,true),
            new("Width","px",8,FrameWidth,8,FrameWidth,true),
            new("Height","px",8,FrameHeight,8,FrameHeight,true)
        ];
        public CameraSettings ReadSettings()=>Applied??new(10000,0);
        public void ApplySettings(CameraSettings settings)=>Applied=settings;
        public void Start(){lock(gate)grabbing=true;}
        public ImageFrame Grab(int timeoutMs)
        {
            lock(gate)
            {
                if(!grabbing)throw new InvalidOperationException("Acquisition has not started.");
                var stride=FrameWidth*3;
                var pixels=new byte[stride*FrameHeight];
                Array.Fill(pixels,(byte)(grabs%251));
                buffers.Add(pixels);
                grabs++;
                return new(FrameWidth,FrameHeight,stride,pixels,Guid.NewGuid(),DateTimeOffset.UtcNow,"FAKE LIVE CAMERA");
            }
        }
        public void Stop(){lock(gate)grabbing=false;}
        public void Close(){}
        public void Dispose(){}
    }
}
