using System.IO;
using WireMarkerInspection.Application;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Infrastructure;
using Xunit;
namespace WireMarkerInspection.Tests;
public class InspectionTests
{
    internal static ImageFrame Frame()=>new(100,100,300,new byte[30000],Guid.NewGuid(),DateTimeOffset.UtcNow,"TEST");
    internal static EndRecipe End(params string[] expected)=>new("reference.png",100,100,SearchRoi.FullImage(100,100),expected);
    internal static Recipe Recipe()=>new(Guid.NewGuid(),"M001","Model 1",1,[End("QK1.11","FT3.F"),End("FT3.F","QK1.11")],DateTimeOffset.UtcNow);
    internal static OcrReading Reading(params string[] lines)=>new(lines.Select(t=>new OcrRegion(t,0.99,[],[])).ToArray(),0);
    [Theory]
    [InlineData("QK1.11","QK1.11",true)]
    [InlineData("QK1.11","QK1.1",false)]
    [InlineData("QK1.11","QK111",false)]
    [InlineData("A-B","AB",false)]
    [InlineData("A B","AB",false)]
    [InlineData("ABC","abc",false)]
    [InlineData("O1","01",false)]
    [InlineData("ABC","ABC ",false)]
    public void ComparisonPreservesEveryCharacter(string expected,string actual,bool ok)
    {
        Assert.Equal(ok?Verdict.Ok:Verdict.Ng,ExactTextComparer.Compare(Frame(),End(expected),Reading(actual)).Verdict);
    }
    [Fact]public void RegionOrderAndCountAreStrict()
    {
        Assert.Equal(Verdict.Ng,ExactTextComparer.Compare(Frame(),End("A","B"),Reading("B","A")).Verdict);
        Assert.Equal(Verdict.Ng,ExactTextComparer.Compare(Frame(),End("A","B"),Reading("A")).Verdict);
        Assert.Equal(Verdict.Ng,ExactTextComparer.Compare(Frame(),End("A"),Reading("A","")).Verdict);
        Assert.Equal(Verdict.Ng,ExactTextComparer.Compare(Frame(),End("A"),Reading()).Verdict);
    }
    [Fact]public async Task TwoEndsBelongToOneImmutableRecipe()
    {
        var engine=new QueueOcr(Reading("QK1.11","FT3.F"),Reading("FT3.F","QK1.11"));
        var store=new ResultSink();var session=new InspectionSession(engine,store);var recipe=Recipe();
        session.Begin(recipe);recipe.Ends[0].ExpectedLines[0]="CHANGED";
        await session.AcceptAsync(Frame());
        Assert.Equal(InspectionState.WaitingEnd2,session.State);Assert.Null(session.Result);Assert.Empty(store.Products);
        await session.AcceptAsync(Frame());
        Assert.Equal(InspectionState.Completed,session.State);Assert.Equal(Verdict.Ok,session.Result!.Verdict);
        Assert.Equal("QK1.11",store.Products.Single().Recipe.Ends[0].ExpectedLines[0]);
        Assert.All(session.Result.Captures!, capture=>Assert.Equal("TEST",capture.Source));
        session.Begin(Recipe());Assert.Empty(session.EndResults);Assert.Null(session.Result);
    }
    [Fact]public async Task OneNgEndRejectsTheProductWithoutRepair()
    {
        var session=new InspectionSession(new QueueOcr(Reading("QK111","FT3.F"),Reading("FT3.F","QK1.11")),new ResultSink());
        session.Begin(Recipe());await session.AcceptAsync(Frame());await session.AcceptAsync(Frame());
        Assert.Equal(Verdict.Ng,session.Result!.Verdict);
        Assert.Equal("QK111",session.Result.Ends[0].Reading.Regions[0].Text);
    }
    [Fact]public async Task RejectsDuplicateFramesAndActiveRecipeChange()
    {
        var s=new InspectionSession(new QueueOcr(Reading("QK1.11","FT3.F")),new ResultSink());s.Begin(Recipe());
        Assert.Throws<InvalidOperationException>(()=>s.Begin(Recipe()));
        var frame=Frame();await s.AcceptAsync(frame);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.AcceptAsync(frame));
        Assert.Equal(InspectionState.WaitingEnd2,s.State);
    }
    [Fact]public async Task CancellationDiscardsLateNativeResult()
    {
        var signal=new TaskCompletionSource<OcrReading>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store=new ResultSink();var session=new InspectionSession(new DeferredOcr(signal.Task),store);session.Begin(Recipe());
        var pending=session.AcceptAsync(Frame());
        session.Stop();session.Begin(Recipe());signal.SetResult(Reading("QK1.11","FT3.F"));
        Assert.Null(await pending);Assert.Equal(InspectionState.WaitingEnd1,session.State);Assert.Empty(session.EndResults);Assert.Empty(store.Products);
    }
    [Fact]public async Task MismatchedImageSizeIsErrorNotNgOrOk()
    {
        var s=new InspectionSession(new QueueOcr(),new ResultSink());s.Begin(Recipe());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.AcceptAsync(new(10,10,30,new byte[300],Guid.NewGuid(),DateTimeOffset.UtcNow,"TEST")));
        Assert.Equal(InspectionState.Faulted,s.State);Assert.Null(s.Result);
    }
    [Fact]public async Task ResultPersistenceFailureCannotPublishOk()
    {
        var s=new InspectionSession(new QueueOcr(Reading("QK1.11","FT3.F"),Reading("FT3.F","QK1.11")),new FailingSink());s.Begin(Recipe());
        await s.AcceptAsync(Frame());await Assert.ThrowsAsync<IOException>(()=>s.AcceptAsync(Frame()));
        Assert.Equal(InspectionState.Faulted,s.State);Assert.Null(s.Result);
    }
    private sealed class QueueOcr(params OcrReading[] readings):IOcrEngine
    {
        private int index;
        public Task<OcrReading> ReadAsync(ImageFrame f,EndRecipe r,CancellationToken c)=>Task.FromResult(readings[index++]);
    }
    private sealed class DeferredOcr(Task<OcrReading> pending):IOcrEngine
    {public Task<OcrReading> ReadAsync(ImageFrame f,EndRecipe r,CancellationToken c)=>pending;}
    private sealed class ResultSink:IResultStore
    {
        public List<ProductResult> Products{get;}=[];
        public Task SaveAsync(ProductResult r,ImageFrame[] f,CancellationToken c){Products.Add(r);return Task.CompletedTask;}
    }
    private sealed class FailingSink:IResultStore
    {public Task SaveAsync(ProductResult r,ImageFrame[] f,CancellationToken c)=>throw new IOException("Disk full");}
}
