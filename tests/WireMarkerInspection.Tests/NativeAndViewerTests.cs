using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WireMarkerInspection.Controls;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Vision;
using Xunit;
namespace WireMarkerInspection.Tests;
public class NativeAndViewerTests
{
    private const string Dll="WireMarkerVision";
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]private static extern int wmi_abi_version();
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr wmi_crop(byte[] bgr,int w,int h,int stride,int shape,double[] xy,int points);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]private static extern void wmi_free(IntPtr p);
    [Fact]public void NativeCircleAndPolygonUseActualMask()=>Sta(()=>
    {
        Assert.Equal(1,wmi_abi_version());
        var pixels=new byte[100*100*3];Array.Fill(pixels,(byte)20);
        foreach(var item in new[]{(1,new double[]{50,50,70,50}),(2,new double[]{10,10,90,10,50,90})})
        {
            var p=wmi_crop(pixels,100,100,300,item.Item1,item.Item2,item.Item2.Length/2);
            string json;try{json=Marshal.PtrToStringUTF8(p)!;}finally{wmi_free(p);}
            using var doc=JsonDocument.Parse(json);Assert.False(doc.RootElement.TryGetProperty("error",out _));
            var bytes=doc.RootElement.GetProperty("cropPng").GetBytesFromBase64();
            using var stream=new MemoryStream(bytes);var bmp=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
            var bgr=new FormatConvertedBitmap(bmp,PixelFormats.Bgr24,null,0);var output=new byte[bgr.PixelWidth*bgr.PixelHeight*3];
            bgr.CopyPixels(output,bgr.PixelWidth*3,0);
            Assert.Equal(20,output[((bgr.PixelHeight/2)*bgr.PixelWidth+bgr.PixelWidth/2)*3]);
            Assert.Equal(255,output[((bgr.PixelHeight-1)*bgr.PixelWidth)*3]);
        }
    });
    [Fact]public void ViewerTransformsAndEditorUndoRemainIndependent()=>Sta(()=>
    {
        var bitmap=BitmapSource.Create(100,100,96,96,PixelFormats.Bgr24,null,new byte[30000],300);bitmap.Freeze();
        var editor=new ImageEditor{Source=bitmap};editor.Measure(new(500,400));editor.Arrange(new(0,0,500,400));editor.Fit();
        var viewer=new ImageViewer{Source=bitmap,Roi=SearchRoi.FullImage(100,100)};
        viewer.Measure(new(300,300));viewer.Arrange(new(0,0,300,300));viewer.Fit();
        var before=viewer.Roi.Copy();editor.FullImage();editor.DeleteRoi();editor.Undo();Assert.NotNull(editor.Roi);
        editor.Redo();Assert.Null(editor.Roi);Assert.Equal(before.Points,viewer.Roi.Points);
        for(var i=0;i<5;i++)
        {
            editor.ZoomBy(1.25);var point=new PixelPoint(33.3,71.2);
            var converted=editor.ViewToImage(editor.ImageToView(point));
            Assert.Equal(point.X,converted.X,8);Assert.Equal(point.Y,converted.Y,8);
        }
    });
    [Fact]public async Task MissingModelsNeverProduceSimulatedOcr()
    {
        using var engine=new NativeOcrEngine(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")));
        Assert.Contains("Missing OCR asset",engine.AvailabilityError);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>engine.ReadAsync(InspectionTests.Frame(),InspectionTests.End("A"),CancellationToken.None));
    }
    [Fact]public void HudSynchronizesToolsAndHistoryWithoutChangingRecipe()=>Sta(()=>
    {
        var bitmap=BitmapSource.Create(100,100,96,96,PixelFormats.Bgr24,null,new byte[30000],300);
        var editor=new ImageEditor{Source=bitmap};
        var hud=new ImageHud{Viewer=editor};
        T Part<T>(string name)=>(T)hud.FindName(name);
        var circle=Part<ToggleButton>("CircleButton");
        circle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(EditorTool.Circle,editor.Tool);Assert.True(circle.IsChecked);
        editor.FullImage();
        var roi=editor.Roi!.Copy();
        var undo=Part<Button>("UndoButton");Assert.True(undo.IsEnabled);
        undo.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Assert.Null(editor.Roi);
        Part<Button>("RedoButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(roi.Points,editor.Roi!.Points);
        editor.Tool=EditorTool.Select;
        Assert.Equal(roi.Points,editor.Roi.Points);
        Assert.True(Part<ToggleButton>("SelectButton").IsChecked);Assert.False(circle.IsChecked);
        Assert.Equal(Visibility.Collapsed,Part<Border>("PolygonStrip").Visibility);
    });
    [Fact]public void HudReadOnlyViewerAndSourceReplacementKeepCorrectState()=>Sta(()=>
    {
        var viewer=new ImageViewer();var hud=new ImageHud{Viewer=viewer};
        Assert.Equal(Visibility.Collapsed,((Border)hud.FindName("DrawingRail")).Visibility);
        var navigation=(StackPanel)hud.FindName("NavigationButtons");Assert.False(navigation.IsEnabled);
        var bitmap=BitmapSource.Create(100,100,96,96,PixelFormats.Bgr24,null,new byte[30000],300);
        viewer.Source=bitmap;viewer.Roi=SearchRoi.FullImage(100,100);
        Assert.True(navigation.IsEnabled);
        Assert.NotNull(viewer.Roi);
        viewer.Source=null;Assert.False(navigation.IsEnabled);
        var replacement=new ImageEditor{Source=bitmap};hud.Viewer=replacement;
        Assert.Equal(Visibility.Visible,((Border)hud.FindName("DrawingRail")).Visibility);
        viewer.Source=bitmap; // Detached viewer must not drive the new HUD.
        replacement.Tool=EditorTool.Polygon;
        Assert.True(((ToggleButton)hud.FindName("PolygonButton")).IsChecked);
    });
    [Fact]public void ChevronButtonTailModeIsExplicitAndDefaultsToNotched()=>Sta(()=>
    {
        var button=new ChevronButton();
        Assert.Equal(ChevronTailMode.Notched,button.TailMode);
        button.TailMode=ChevronTailMode.Straight;
        Assert.Equal(ChevronTailMode.Straight,button.TailMode);
    });
    private static void Sta(Action action)
    {
        Exception? failure=null;
        var thread=new Thread(()=>{try{action();}catch(Exception ex){failure=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
