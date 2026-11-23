using WireMarkerInspection.Application;
using WireMarkerInspection.Domain;
using Xunit;

namespace WireMarkerInspection.Tests;

public sealed class TriggerTests
{
    private static TriggerEvent Signal(int? end=null,long? at=null)=>new(end,at??MonotonicClock.Now,"test");

    [Fact]
    public void OneSharedSignalFollowsTheSessionFromEndOneToEndTwo()
    {
        var router=new TriggerRouter(new TriggerSettings(TriggerKind.CameraLine,TriggerMapping.Shared,
            new CameraTrigger(CameraTriggerSource.Line),RepeatBlockMs:0));

        var first=router.Route(Signal(),InspectionState.WaitingEnd1,0);
        Assert.True(first.Accepted);
        Assert.Equal(0,first.End);

        var second=router.Route(Signal(),InspectionState.WaitingEnd2,1);
        Assert.True(second.Accepted);
        Assert.Equal(1,second.End);
    }

    [Fact]
    public void ASignalArrivingWhileTheImageIsBeingProcessedIsRefusedWithAReason()
    {
        var router=new TriggerRouter(new TriggerSettings(RepeatBlockMs:0));

        var processing=router.Route(Signal(),InspectionState.ProcessingEnd1,0);
        Assert.False(processing.Accepted);
        Assert.Contains("Đang xử lý",processing.Reason);

        var idle=router.Route(Signal(),InspectionState.Idle,0);
        Assert.False(idle.Accepted);
        Assert.Contains("không ở trạng thái chờ",idle.Reason);

        var completed=router.Route(Signal(),InspectionState.Completed,2);
        Assert.False(completed.Accepted);
    }

    [Fact]
    public void ABouncingContactCannotCaptureTheSameEndTwice()
    {
        var router=new TriggerRouter(new TriggerSettings(RepeatBlockMs:200));
        var at=MonotonicClock.Now;
        var ticksPerMs=System.Diagnostics.Stopwatch.Frequency/1000;

        Assert.True(router.Route(Signal(at:at),InspectionState.WaitingEnd1,0).Accepted);

        var bounce=router.Route(Signal(at:at+50*ticksPerMs),InspectionState.WaitingEnd1,0);
        Assert.False(bounce.Accepted);
        Assert.Contains("lặp quá nhanh",bounce.Reason);

        Assert.True(router.Route(Signal(at:at+250*ticksPerMs),InspectionState.WaitingEnd1,0).Accepted);
    }

    [Fact]
    public void PerEndSignalsRefuseToFileAnImageAgainstTheWrongEnd()
    {
        var router=new TriggerRouter(new TriggerSettings(TriggerKind.Plc,TriggerMapping.PerEnd,RepeatBlockMs:0));

        // The end-2 button pressed while end 1 is still expected must not be filed as end 1.
        var wrong=router.Route(Signal(end:1),InspectionState.WaitingEnd1,0);
        Assert.False(wrong.Accepted);
        Assert.Contains("đang chờ đầu 1",wrong.Reason);

        Assert.True(router.Route(Signal(end:0),InspectionState.WaitingEnd1,0).Accepted);
        Assert.True(router.Route(Signal(end:1),InspectionState.WaitingEnd2,1).Accepted);

        var unnamed=router.Route(Signal(),InspectionState.WaitingEnd2,1);
        Assert.False(unnamed.Accepted);
        Assert.Contains("không cho biết đầu nào",unnamed.Reason);
    }

    [Fact]
    public void ImpossibleTriggerCombinationsAreRejectedBeforeRunStarts()
    {
        // One camera exposes a single TriggerSource node, so two lines cannot drive the two ends.
        var perEndOnOneCamera=new TriggerSettings(TriggerKind.CameraLine,TriggerMapping.PerEnd,
            new CameraTrigger(CameraTriggerSource.Line));
        Assert.Contains("một nguồn trigger",perEndOnOneCamera.Validate()!);
        Assert.Throws<ArgumentException>(()=>new TriggerRouter(perEndOnOneCamera));

        Assert.NotNull(new TriggerSettings(TriggerKind.CameraLine,TriggerMapping.Shared,CameraTrigger.FreeRun).Validate());
        Assert.NotNull(new TriggerSettings(RepeatBlockMs:-1).Validate());
        Assert.NotNull(new CameraTrigger(CameraTriggerSource.Line,Line:-1).Validate());
        Assert.Null(new TriggerSettings(TriggerKind.CameraLine,TriggerMapping.Shared,
            new CameraTrigger(CameraTriggerSource.Line,2,false,100,1000)).Validate());
    }

    [Fact]
    public async Task RetakingTheFirstEndDropsItWithoutEndingTheRun()
    {
        var sink=new Sink();
        var session=new InspectionSession(new Ocr(),sink);
        session.Begin(Recipe());

        Assert.False(session.RetakeLastEnd());          // nothing captured yet
        await session.AcceptAsync(Frame());
        Assert.Equal(InspectionState.WaitingEnd2,session.State);

        Assert.True(session.RetakeLastEnd());

        Assert.Equal(InspectionState.WaitingEnd1,session.State);
        Assert.Empty(session.EndResults);
        Assert.False(session.RetakeLastEnd());          // and again there is nothing to retake

        await session.AcceptAsync(Frame());
        await session.AcceptAsync(Frame());
        var product=Assert.Single(sink.Products);
        Assert.Equal(2,product.Ends.Length);            // the retaken cycle still publishes exactly two ends
    }

    [Fact]
    public async Task ACameraLineSourceDescribesItsWiringAndPublishesPulses()
    {
        // The device configuration itself belongs to the caller, which owns the stopped acquisition
        // window; TriggerAcquisitionTests covers that. Here the source only has to describe and publish.
        var trigger=new CameraTrigger(CameraTriggerSource.Line,2,RisingEdge:false,DelayUs:50,DebouncerUs:1000);
        var source=new CameraLineTriggerSource(trigger);
        TriggerEvent? seen=null;
        source.Fired+=(_,e)=>seen=e;

        await source.StartAsync(CancellationToken.None);
        Assert.Contains("Line 2",source.Status);
        Assert.Contains("sườn xuống",source.Status);

        source.Fire(null,"camera-line");
        Assert.NotNull(seen);
        Assert.Equal("camera-line",seen!.Source);
        Assert.Null(seen.End);

        var invalid=new CameraLineTriggerSource(new CameraTrigger(CameraTriggerSource.Line,Line:-1));
        Assert.Throws<InvalidOperationException>(()=>invalid.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
    }

    private static ImageFrame Frame()=>new(100,100,300,new byte[30000],Guid.NewGuid(),DateTimeOffset.UtcNow,"TEST");
    private static EndRecipe End()=>new("",100,100,SearchRoi.FullImage(100,100),["A"],TextOrientation.Auto);
    private static Recipe Recipe()=>new(Guid.NewGuid(),"M-TRIG","Trigger",1,[End(),End()],DateTimeOffset.UtcNow);

    private sealed class Ocr:IOcrEngine
    {
        public Task<OcrReading> ReadAsync(ImageFrame f,EndRecipe r,CancellationToken c)=>
            Task.FromResult(new OcrReading([new("A",0.99,[],[])],0));
    }
    private sealed class Sink:IResultStore
    {
        public List<ProductResult> Products{get;}=[];
        public Task SaveAsync(ProductResult r,ImageFrame[] f,CancellationToken c){Products.Add(r);return Task.CompletedTask;}
    }
}
