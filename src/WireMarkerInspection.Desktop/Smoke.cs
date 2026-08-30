using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WireMarkerInspection.Controls;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Desktop.Services;
using WireMarkerInspection.Desktop.ViewModels;

namespace WireMarkerInspection.Desktop;
internal static class Smoke
{
    public static async void Run(string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        MainWindow? window=null;
        try
        {
            // Each run owns fresh data; re-running release smoke must not collide with its prior recipe.
            var vm=new MainViewModel(Path.Combine(output,"isolated-data",Guid.NewGuid().ToString("N")));
            window=new MainWindow(vm){Width=1920,Height=1080,Title="OFFLINE SMOKE · SYNTHETIC UI FIXTURES"};
            vm.SourceStatus="SMOKE FIXTURE · NOT LIVE";
            window.Show();
            if(vm.CanConfigureModel||vm.CanManageSelectedModel)throw new Exception("Model setup must be locked before selection or Add Model.");
            VerifyModelControls(window,false,false,false,false);
            var image=Fixture();
            vm.NewModelCommand.Execute(new ModelIdentity("UI-FIXTURE","Synthetic layout fixture"));
            if(!vm.CanConfigureModel||vm.CanManageSelectedModel)throw new Exception("A new model draft must unlock setup without enabling selected-model actions.");
            VerifyModelControls(window,true,true,false,true);
            vm.End1.SetFrame(ImageFiles.Frame(image,"SYNTHETIC UI FIXTURE"));
            vm.End2.SetFrame(ImageFiles.Frame(image,"SYNTHETIC UI FIXTURE"));
            vm.End1.Roi=new(RoiShape.Rectangle,[new(120,110),new(1000,440)]);
            vm.End2.Roi=new(RoiShape.Polygon,[new(120,110),new(1000,130),new(970,450),new(110,425)]);
            vm.End1.ExpectedText="QK1.11\nFT3.F";vm.End2.ExpectedText="FT3.F\nQK1.11";
            vm.LiveImage=image;vm.End1.Apply();vm.End2.Apply();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            VerifyModelControls(window,true,true,false,true);
            Capture(window,Path.Combine(output,"setting-dirty.png"));
            vm.SaveRecipeCommand.Execute(null);
            if(vm.Dirty || vm.SelectedModel?.Code!="UI-FIXTURE" || vm.SelectedModel.Revision!="v1" || vm.Models.Count!=1)
                throw new Exception($"Smoke recipe save/reload failed: {vm.Message}");
            if(!vm.CanConfigureModel||!vm.CanManageSelectedModel)throw new Exception("A saved selected model must keep setup and model actions enabled.");
            VerifyModelControls(window,true,false,true,false);
            vm.EditModelCommand.Execute(new ModelIdentity("UI-FIXTURE","Synthetic layout fixture v2"));
            VerifyModelControls(window,true,true,true,true);
            vm.SaveRecipeCommand.Execute(null);
            if(vm.Dirty || vm.SelectedModel?.Name!="Synthetic layout fixture v2" || vm.SelectedModel.Revision!="v2")
                throw new Exception($"Smoke recipe edit/revision failed: {vm.Message}");
            VerifyModelControls(window,true,false,true,false);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            VerifyHudLayout(window);
            Capture(window,Path.Combine(output,"setting.png"));
            var modelDialog=new ModelDetailsWindow("MODEL DIALOG · UI SMOKE",vm.ModelCode,vm.ModelName,_=>null){Owner=window};
            modelDialog.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Capture(modelDialog,Path.Combine(output,"model-dialog.png"));modelDialog.Close();
            var viewer=new ImageViewer{Source=image,Width=600,Height=300};
            viewer.Measure(new(600,300));viewer.Arrange(new(0,0,600,300));viewer.Fit();
            var p=new PixelPoint(312.5,208.25);
            viewer.ZoomBy(2);
            var roundtrip=viewer.ViewToImage(viewer.ImageToView(p));
            if(Math.Abs(roundtrip.X-p.X)>1e-8||Math.Abs(roundtrip.Y-p.Y)>1e-8)throw new Exception("Image transform roundtrip failed.");
            var editor=new ImageEditor{Source=image,Width=600,Height=300};
            editor.Measure(new(600,300));editor.Arrange(new(0,0,600,300));editor.FullImage();editor.DeleteRoi();editor.Undo();
            if(editor.Roi==null)throw new Exception("Editor undo failed.");
            editor.Redo();if(editor.Roi!=null)throw new Exception("Editor redo failed.");
            var crop=ImageFiles.Png(image);
            OcrReading Reading(int rotation,params string[] lines)=>new(
                lines.Select(text=>new OcrRegion(text,0.99,[],crop)).ToArray(),rotation);
            var frame1=ImageFiles.Frame(image,"SYNTHETIC UI FIXTURE");
            var frame2=ImageFiles.Frame(image,"SYNTHETIC UI FIXTURE");
            var spec1=vm.End1.Spec();var spec2=vm.End2.Spec();
            vm.Result1.Reset(spec1);vm.Result2.Reset(spec2);
            vm.Result1.Image=image;vm.Result2.Image=image;
            vm.RunPage=true;vm.Running=true;vm.RunStatus="CHỜ ĐẦU 1";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            VerifyWaitingStatusVisuals(window);
            Capture(window,Path.Combine(output,"run-waiting.png"));
            vm.Result1.Show(frame1,ExactTextComparer.Compare(frame1,spec1,Reading(0,"QK1.11","FT3.F")));
            vm.Result2.Show(frame2,ExactTextComparer.Compare(frame2,spec2,Reading(180,"FT3.F","QK1.11")));
            vm.RunStatus="NG";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            VerifyRunStatusVisuals(window);
            VerifyHudLayout(window);
            Capture(window,Path.Combine(output,"run.png"));
            vm.Running=false;window.Width=1366;window.Height=900;vm.RunPage=false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Capture(window,Path.Combine(output,"setting-1366.png"));
            VerifyHudLayout(window);
            var hudEditor=new ImageEditor{Source=image,Roi=vm.End1.Roi?.Copy(),Tool=EditorTool.Rectangle};
            var hud=new ImageHud{Viewer=hudEditor,ShowCaption=false};
            var hudWindow=new Window{Title="HUD CONTROL SMOKE",Width=1100,Height=760,Content=hud,Owner=window};
            hudWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var defaultZoom=hudEditor.Zoom;hudEditor.ZoomBy(2);
            ((System.Windows.Controls.Button)hud.FindName("ResetViewButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if(Math.Abs(hudEditor.Zoom-defaultZoom)>1e-8)throw new Exception("HUD reset did not restore the initial Fit view.");
            VerifyHudLayout(hudWindow);
            Capture(hud,Path.Combine(output,"hud-editor.png"));
            hudWindow.Close();
            vm.Dirty=false;await vm.ShutdownAsync();
            File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: WPF views rendered; dirty/save notification states; prominent RUN waiting and red Stop; 40-DIP total/per-end verdicts; green/red actual text and detail; HUD reset restores initial Fit; HUD bounds/non-overlap at 1920 and 1366; expanded HUD render; transform roundtrip; editor undo/redo; model Add/Save v1/Edit/Save v2/reload path. Synthetic UI fixtures only. No OCR or hardware acceptance.");
            window.Close();
            System.Windows.Application.Current.Shutdown(0);
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(output,"result.txt"),ex.ToString());
            System.Windows.Application.Current.Shutdown(1);
        }
    }
    private static void Capture(FrameworkElement element,string path)
    {
        element.UpdateLayout();
        var bmp=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);
        bmp.Render(element);File.WriteAllBytes(path,ImageFiles.Png(bmp));
    }
    private static void VerifyHudLayout(DependencyObject root)
    {
        if(root is ImageHud hud && hud.IsVisible)
        {
            hud.UpdateLayout();
            Rect Bounds(string name)
            {
                var part=(FrameworkElement)hud.FindName(name);
                return part.TransformToAncestor(hud).TransformBounds(new Rect(part.RenderSize));
            }
            var navigation=Bounds("NavigationHud");
            if(!new Rect(hud.RenderSize).Contains(navigation))throw new Exception("Navigation HUD is clipped.");
            if(hud.Viewer is ImageEditor)
            {
                var rail=Bounds("DrawingRail");
                if(!new Rect(hud.RenderSize).Contains(rail) || rail.IntersectsWith(navigation) || rail.IntersectsWith(Bounds("CaptionPanel")))
                    throw new Exception("Editor HUD controls overlap or are clipped.");
            }
        }
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)VerifyHudLayout(VisualTreeHelper.GetChild(root,i));
    }
    private static void VerifyModelControls(MainWindow window,bool setupEnabled,bool saveEnabled,bool selectedActionsEnabled,bool notifyVisible)
    {
        window.UpdateLayout();
        if(window.ModelSetupEditors.IsEnabled!=setupEnabled||window.SaveRecipeButton.IsEnabled!=saveEnabled)
            throw new Exception($"Model setup/save enabled state is invalid. Expected {setupEnabled}/{saveEnabled}.");
        if(window.EditModelButton.IsEnabled!=selectedActionsEnabled||window.DeleteModelButton.IsEnabled!=selectedActionsEnabled)
            throw new Exception($"Selected-model action state is invalid. Expected {selectedActionsEnabled}.");
        if((window.UnsavedNotification.Visibility==Visibility.Visible)!=notifyVisible)
            throw new Exception($"Unsaved notification state is invalid. Expected {notifyVisible}.");
        if(saveEnabled&&ColorOf(window.SaveRecipeButton.Foreground)!=ColorOf((Brush)window.FindResource("Brush.Brand.Secondary")))
            throw new Exception("Pending Save button is not highlighted.");
    }
    private static void VerifyRunStatusVisuals(MainWindow window)
    {
        window.UpdateLayout();
        var success=ColorOf((Brush)window.FindResource("Brush.Status.Success"));
        var error=ColorOf((Brush)window.FindResource("Brush.Status.Error"));
        if(window.RunStatusText.FontSize!=40||ColorOf(window.RunStatusText.Foreground)!=error)
            throw new Exception("Total NG verdict is not rendered at the required size/color.");
        if(ColorOf(window.StopRuntimeButton.BorderBrush)!=error||window.StopRuntimeButton.BorderThickness.Left<1)
            throw new Exception("Stop button does not have a red border.");
        if(window.FirstEndResult.StatusText.FontSize!=40||ColorOf(window.FirstEndResult.StatusText.Foreground)!=success||
           ColorOf(window.FirstEndResult.ActualText.Foreground)!=success)
            throw new Exception("Per-end OK verdict/read text is not rendered large and green.");
        if(window.SecondEndResult.StatusText.FontSize!=40||ColorOf(window.SecondEndResult.StatusText.Foreground)!=error||
           ColorOf(window.SecondEndResult.ActualText.Foreground)!=error||ColorOf(window.SecondEndResult.DetailText.Foreground)!=error)
            throw new Exception("Per-end NG verdict/read text/detail is not rendered large and red.");
    }
    private static void VerifyWaitingStatusVisuals(MainWindow window)
    {
        window.UpdateLayout();
        var warning=ColorOf((Brush)window.FindResource("Brush.Status.Warning"));
        var error=ColorOf((Brush)window.FindResource("Brush.Status.Error"));
        if(window.RunStatusText.FontSize!=28||ColorOf(window.RunStatusText.Foreground)!=warning)
            throw new Exception("Waiting state is not rendered as a prominent warning.");
        if(ColorOf(window.StopRuntimeButton.BorderBrush)!=error||window.StopRuntimeButton.BorderThickness.Left<1)
            throw new Exception("Stop button does not have a red border while RUN is active.");
    }
    private static Color ColorOf(Brush brush)=>brush is SolidColorBrush solid?solid.Color:throw new Exception("Expected a solid status brush.");
    private static BitmapSource Fixture()
    {
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(32,39,47)),null,new Rect(0,0,1200,600));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(220,222,206)),null,new Rect(130,120,850,310),30,30);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(60,93,150)),null,new Rect(0,230,130,85));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(60,93,150)),null,new Rect(980,230,220,85));
            var text=new FormattedText("QK1.11\nFT3.F",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Consolas"),88,Brushes.Black,1);
            dc.DrawText(text,new Point(285,155));
            var label=new FormattedText("SYNTHETIC UI FIXTURE — NOT OCR VALIDATION",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),24,Brushes.White,1);
            dc.DrawText(label,new Point(200,530));
        }
        var bitmap=new RenderTargetBitmap(1200,600,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }
}
