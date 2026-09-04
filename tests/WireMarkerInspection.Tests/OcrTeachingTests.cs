using WireMarkerInspection.Desktop.Services;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using Xunit;

namespace WireMarkerInspection.Tests;

[Collection(DispatcherTestHost.Collection)]
public sealed class OcrTeachingTests
{
    [Fact]
    public void ReadingFillsOrderedExactTextAndRequiresApplyWithoutChangingDirection()=>DispatcherTestHost.Sta(()=>
    {
        var editor=Editor();var changes=0;editor.Changed+=(_,_)=>changes++;
        var reading=Reading(editor,"FT3.f/QK1.11","BUS BAR.N/QE06.3");
        editor.ShowReading(reading);
        Assert.Equal("FT3.f/QK1.11\nBUS BAR.N/QE06.3",editor.ExpectedText);
        Assert.Equal(TextOrientation.Degrees180,editor.Orientation);
        Assert.False(editor.Applied);Assert.Equal(1,changes);
        Assert.Same(reading.Regions,editor.Regions);Assert.Equal(2,editor.Previews.Count);
        editor.Apply();Assert.True(editor.Applied);
        Assert.Equal(new[]{"FT3.f/QK1.11","BUS BAR.N/QE06.3"},editor.Spec().ExpectedLines);
    });

    [Fact]
    public void EmptyOrIncompleteReadingDoesNotEraseExistingSample()=>DispatcherTestHost.Sta(()=>
    {
        var editor=Editor();var changes=0;editor.Changed+=(_,_)=>changes++;
        editor.ShowReading(new([],0));
        Assert.Equal("OLD",editor.ExpectedText);Assert.True(editor.Applied);
        editor.ShowReading(Reading(editor,"READ"," "));
        Assert.Equal("OLD",editor.ExpectedText);Assert.True(editor.Applied);Assert.Equal(0,changes);
    });

    [Fact]
    public void IdenticalReadingKeepsAppliedStateAndMalformedPreviewCannotPartiallyOverwrite()=>DispatcherTestHost.Sta(()=>
    {
        var editor=Editor();editor.ShowReading(Reading(editor,"OLD"));
        Assert.True(editor.Applied);Assert.Single(editor.Previews);
        Assert.ThrowsAny<Exception>(()=>editor.ShowReading(new([new("NEW",1,[],[])],0)));
        Assert.Equal("OLD",editor.ExpectedText);Assert.True(editor.Applied);Assert.Single(editor.Previews);
    });

    private static EndEditorViewModel Editor()
    {
        var editor=new EndEditorViewModel(1);
        editor.SetFrame(new(20,20,60,new byte[1200],Guid.NewGuid(),DateTimeOffset.UtcNow,"TEST"));
        editor.Roi=new(RoiShape.Rectangle,[new(0,0),new(20,20)]);
        editor.ExpectedText="OLD";editor.Orientation=TextOrientation.Degrees180;editor.Apply();return editor;
    }
    private static OcrReading Reading(EndEditorViewModel editor,params string[] lines)=>
        new(lines.Select(text=>new OcrRegion(text,0.99,[],ImageFiles.Png(editor.Image!))).ToArray(),0);
}
