using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WireMarkerInspection.Controls;
using WireMarkerInspection.Application;
using WireMarkerInspection.Desktop.Views;
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
            var camera=new SmokeCamera();
            var vm=new MainViewModel(Path.Combine(output,"isolated-data",Guid.NewGuid().ToString("N")),camera,autoDiscoverCameraOnLoad:false);
            window=new MainWindow(vm){Width=1920,Height=1080,Title="OFFLINE SMOKE · SYNTHETIC UI FIXTURES"};
            vm.SourceStatus="SMOKE FIXTURE · NOT LIVE";
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            VerifyPlcConnectionUi(window,vm);
            window.SettingsScroll.ScrollToEnd();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Capture(window,Path.Combine(output,"plc-connection.png"));
            window.SettingsScroll.ScrollToHome();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            VerifyCameraControls(window,search:true,connect:false,disconnect:false,acquisition:false,parameters:false);
            if(!window.LiveCameraHud.CanExpand)throw new Exception("Live Camera HUD must expose Expand.");
            vm.FindingCamera=true;vm.CameraState=CameraUiState.Finding;vm.CameraStatus="ĐANG TÌM CAMERA HIKROBOT...";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            VerifyCameraControls(window,search:false,connect:false,disconnect:false,acquisition:false,parameters:false);
            if(window.FindingIndicator.Visibility!=Visibility.Visible)throw new Exception("Camera finding indicator is not visible.");
            Capture(window,Path.Combine(output,"camera-finding.png"));
            vm.FindingCamera=false;vm.CameraState=CameraUiState.NotFound;vm.CameraStatus="KHÔNG TÌM THẤY CAMERA";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            VerifyCameraControls(window,search:true,connect:false,disconnect:false,acquisition:false,parameters:false);
            var smokeCamera=new CameraDevice("smoke-camera","Hikrobot smoke device","smoke",false);
            vm.Cameras.Add(smokeCamera);vm.SelectedCamera=smokeCamera;vm.CameraState=CameraUiState.Found;vm.CameraStatus="ĐÃ TÌM THẤY 1 CAMERA · SẴN SÀNG KẾT NỐI";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            VerifyCameraControls(window,search:true,connect:true,disconnect:false,acquisition:false,parameters:false);
            vm.CameraConnected=true;vm.CameraState=CameraUiState.Connected;vm.CameraStatus="ĐÃ KẾT NỐI · Hikrobot smoke device";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            VerifyCameraControls(window,search:false,connect:false,disconnect:true,acquisition:true,parameters:true);
            vm.Acquiring=true;vm.CameraState=CameraUiState.Acquiring;vm.CameraStatus="ĐANG ACQUISITION · 4024 × 3036";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            VerifyCameraControls(window,search:false,connect:false,disconnect:false,acquisition:true,parameters:false);
            var cameraError=ColorOf((Brush)window.FindResource("Brush.Status.Error"));
            if(window.AcquisitionButton.Width!=32||ColorOf(window.AcquisitionButton.BorderBrush)!=cameraError||window.AcquisitionButton.BorderThickness.Left<1)
                throw new Exception("Stop Acquisition state must be a rounded square with a red border.");
            Capture(window,Path.Combine(output,"camera-acquiring.png"));
            vm.Acquiring=false;vm.CameraConnected=false;vm.Cameras.Clear();vm.SelectedCamera=null;vm.CameraState=CameraUiState.NotFound;vm.CameraStatus="KHÔNG TÌM THẤY CAMERA";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            if(vm.CanConfigureModel||vm.CanManageSelectedModel)throw new Exception("Model setup must be locked before selection or Add Model.");
            VerifyModelControls(window,false,false,false,false);
            var image=Fixture();
            vm.NewModelCommand.Execute(new ModelIdentity("UI-FIXTURE","Synthetic layout fixture"));
            if(!vm.CanConfigureModel||vm.CanManageSelectedModel)throw new Exception("A new model draft must unlock setup without enabling selected-model actions.");
            VerifyModelControls(window,true,true,false,true);
            await VerifyLiveGrab(window,vm,camera);
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
            vm.RecordCycleTime(742);vm.RecordCycleTime(910);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            VerifyRunStatusVisuals(window);
            VerifyTimingReadout(window,vm);
            VerifyHudLayout(window);
            Capture(window,Path.Combine(output,"run.png"));
            vm.Running=false;window.Width=1366;window.Height=900;vm.RunPage=false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Capture(window,Path.Combine(output,"setting-1366.png"));
            VerifyHudLayout(window);
            await VerifyDeclinedModelSwitch(window,vm);
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
            File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: WPF views rendered; strict camera Finding/NotFound/Found/Connected/Acquiring enable states; visible finding indicator; red rounded-square Stop Acquisition; Live Camera Expand enabled; dirty/save notification states; prominent RUN waiting and red Stop; 40-DIP total/per-end verdicts; green/red actual text and detail; HUD reset restores initial Fit; per-model camera parameters showing real device limits with optional groups disabled; Grab Image disabled without a live frame, enabled while acquiring and storing the grabbed frame; HUD bounds/non-overlap at 1920 and 1366; expanded HUD render; transform roundtrip; editor undo/redo; model Add/Save v1/Edit/Save v2/reload path; measured cycle time with average/p95/max and acquisition diagnostics on screen; hardware trigger arming that keeps the camera silent until a pulse and restores free-run, with per-end mapping on one camera line refused; PLC connection defaults to COM11 / Modbus ASCII / 9600 / 7E1 with Ethernet IP as a separate physical option, visible Connect/Disconnect states, and trigger fields shown with write-back off until configured; declined draft discard keeps the draft and restores the library selection outside the selection change. Synthetic UI fixtures only. No OCR or hardware acceptance.");
            window.Close();
            System.Windows.Application.Current.Shutdown(0);
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(output,"result.txt"),ex.ToString());
            System.Windows.Application.Current.Shutdown(1);
        }
    }
    private static void VerifyPlcConnectionUi(MainWindow window,MainViewModel vm)
    {
        window.UpdateLayout();
        if(window.PlcComPanel.Visibility!=Visibility.Visible||window.PlcEthernetPanel.Visibility==Visibility.Visible)
            throw new Exception("PLC must default to the COM physical connection, separate from Ethernet IP.");
        if(vm.PlcSerialPort!="COM11"||vm.PlcBaudRate!="9600"||vm.PlcSerialProtocol!=PlcSerialProtocol.ModbusAscii||
           vm.PlcDataBits!="7"||vm.PlcParity!=PlcSerialParity.Even||vm.PlcStopBits!=PlcSerialStopBits.One)
            throw new Exception("PLC COM defaults must preserve the proven COM11 / 9600 / Modbus ASCII / 7E1 setup.");
        if(!Equals(window.PlcSerialPortSelector.SelectedItem,"COM11"))
            throw new Exception("The configured COM port is not visible in the PLC connection selector.");
        if(!window.ConnectPlcButton.IsEnabled||window.DisconnectPlcButton.IsEnabled)
            throw new Exception("PLC must offer Connect only before a physical link is open.");
        var ethernet=vm.PlcTransports.Single(option=>option.Value==PlcTransport.EthernetIp);
        var com=vm.PlcTransports.Single(option=>option.Value==PlcTransport.Com);
        if(ethernet.Label!="Ethernet IP"||com.Label!="COM")throw new Exception("PLC transport labels are not operator-facing.");
    }
    /// <summary>
    /// Drives the real acquisition loop with a synthetic camera so Grab Image is verified end to end:
    /// disabled without a live frame, enabled while acquiring, and it stores the frame in the end editor.
    /// </summary>
    /// <summary>
    /// Arming a hardware trigger is an acquisition lifecycle change. This drives it against a camera that
    /// really stays silent until pulsed, and checks the form only offers valid combinations.
    /// </summary>
    private static async Task VerifyTriggerArming(MainWindow window,MainViewModel vm,SmokeCamera camera)
    {
        window.UpdateLayout();
        if(!window.TriggerKindSelector.IsEnabled||!window.TriggerMappingSelector.IsEnabled)
            throw new Exception("Trigger settings must be editable while RUN is stopped.");
        if(window.CameraTriggerPanel.Visibility==Visibility.Visible)
            throw new Exception("Camera trigger fields must be hidden for a manual trigger.");

        vm.TriggerKind=TriggerKind.CameraLine;
        vm.TriggerLine="2";
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        window.UpdateLayout();
        if(window.CameraTriggerPanel.Visibility!=Visibility.Visible)
            throw new Exception("Camera trigger fields must appear for a hardware trigger.");

        // One camera has a single trigger source, so this combination must be refused, not silently accepted.
        vm.TriggerMapping=TriggerMapping.PerEnd;
        try
        {
            vm.BuildTriggerSettings();
            throw new Exception("A per-end mapping on one camera line must be rejected.");
        }
        catch(InvalidOperationException){}
        vm.TriggerMapping=TriggerMapping.Shared;

        // A PLC trigger shows its own fields and reports whether writing back is configured.
        vm.TriggerKind=TriggerKind.Plc;
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        window.UpdateLayout();
        if(window.PlcTriggerPanel.Visibility!=Visibility.Visible||window.CameraTriggerPanel.Visibility==Visibility.Visible)
            throw new Exception("Selecting a PLC trigger must show the PLC fields and hide the camera line fields.");
        if(!window.PlcOutputsSummaryText.Text.Contains("tắt",StringComparison.Ordinal))
            throw new Exception($"Writing back must be off until it is configured: {window.PlcOutputsSummaryText.Text}");
        vm.TriggerKind=TriggerKind.CameraLine;
        await Dispatcher.Yield(DispatcherPriority.DataBind);

        await vm.ArmTriggerAsync(vm.BuildTriggerSettings());
        if(!vm.TriggerStatus.Contains("Line 2",StringComparison.Ordinal))
            throw new Exception($"Trigger status does not describe the armed line: {vm.TriggerStatus}");
        var before=camera.Frames;
        var quiet=DateTime.UtcNow.AddMilliseconds(400);
        while(DateTime.UtcNow<quiet)await Dispatcher.Yield(DispatcherPriority.Background);
        if(camera.Frames!=before)throw new Exception("A triggered camera must stay silent without a pulse.");

        camera.Pulse();
        var deadline=DateTime.UtcNow.AddSeconds(5);
        while(camera.Frames==before)
        {
            if(DateTime.UtcNow>deadline)throw new Exception("The trigger pulse produced no frame.");
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        if(window.RunTriggerText.Text.Length==0)throw new Exception("RUN must report the last trigger.");

        await vm.DisarmTriggerAsync();
        vm.TriggerKind=TriggerKind.Manual;
        var resumed=camera.Frames;
        deadline=DateTime.UtcNow.AddSeconds(5);
        while(camera.Frames==resumed)
        {
            if(DateTime.UtcNow>deadline)throw new Exception("Free-run did not resume after disarming.");
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
    }

    private static async Task VerifyLiveGrab(MainWindow window,MainViewModel vm,SmokeCamera smokeCamera)
    {
        window.UpdateLayout();
        if(window.FirstEndEditor.GrabButton.IsEnabled||window.SecondEndEditor.GrabButton.IsEnabled)
            throw new Exception("Grab Image must be disabled without a live camera frame.");
        await vm.InitializeCameraAsync();
        await vm.ConnectCommand.ExecuteAsync(null);
        VerifyCameraParameterForm(window,vm);
        await vm.AcquisitionCommand.ExecuteAsync(null);
        var deadline=DateTime.UtcNow.AddSeconds(10);
        while(!vm.CanGrabReference)
        {
            if(DateTime.UtcNow>deadline)throw new Exception($"No live frame reached the view model: {vm.Message}");
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        window.UpdateLayout();
        if(!window.FirstEndEditor.GrabButton.IsEnabled||!window.SecondEndEditor.GrabButton.IsEnabled)
            throw new Exception("Grab Image must be enabled while a fresh live frame is available.");
        await VerifyTriggerArming(window,vm,smokeCamera);
        vm.GrabReferenceCommand.Execute("1");
        if(vm.End1.Frame==null||vm.End1.Image==null||vm.End1.Frame.Width!=SmokeCamera.FrameWidth)
            throw new Exception($"Grab Image did not store the live frame: {vm.Message}");
        await vm.StopAcquisitionAsync();
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        window.UpdateLayout();
        if(window.FirstEndEditor.GrabButton.IsEnabled||window.SecondEndEditor.GrabButton.IsEnabled)
            throw new Exception("Grab Image must be disabled again after acquisition stops.");
        await vm.DisconnectCommand.ExecuteAsync(null);
        vm.Cameras.Clear();vm.SelectedCamera=null;vm.CameraState=CameraUiState.NotFound;vm.CameraStatus="KHÔNG TÌM THẤY CAMERA";
        vm.End1.Clear();
    }
    /// <summary>
    /// Declining the unsaved-draft question used to write the previous selection back while the
    /// DataGrid was still inside its own selection change, which threw on a null item automation peer.
    /// This drives the real library grid and checks the draft survives and both selections agree.
    /// </summary>
    private static async Task VerifyDeclinedModelSwitch(MainWindow window,MainViewModel vm)
    {
        var saved=vm.SelectedModel??throw new Exception("A saved model must be selected before the discard check.");
        var confirm=vm.Confirm;
        try
        {
            vm.Confirm=_=>false;
            vm.NewModelCommand.Execute(new ModelIdentity("UI-DRAFT","Unsaved draft"));
            if(!vm.Dirty||vm.SelectedModel!=null)throw new Exception("A new draft must be dirty and clear the selection.");
            window.ModelLibraryGrid.SelectedItem=saved;   // the operator clicks another library row
            if(!ReferenceEquals(vm.SelectedModel,saved))
                throw new Exception("A declined selection must not be reverted inside the selection change.");
            var deadline=DateTime.UtcNow.AddSeconds(5);
            while(vm.SelectedModel!=null)
            {
                if(DateTime.UtcNow>deadline)throw new Exception("The declined selection was never restored.");
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            if(window.ModelLibraryGrid.SelectedItem!=null)throw new Exception("The library grid still shows the declined selection.");
            if(!vm.Dirty||vm.ModelCode!="UI-DRAFT"||!vm.CanConfigureModel)
                throw new Exception("Declining the discard must keep the unsaved draft.");
            vm.Confirm=_=>true;
            window.ModelLibraryGrid.SelectedItem=saved;   // accepting must load the saved model again
            if(!ReferenceEquals(vm.SelectedModel,saved)||vm.Dirty)
                throw new Exception("Accepting the discard must load the selected model.");
        }
        finally{vm.Confirm=confirm;}
    }
    /// <summary>
    /// Camera parameters are taught per model, so the form must show the device limits and keep every
    /// optional group disabled until the operator turns it on.
    /// </summary>
    private static void VerifyCameraParameterForm(MainWindow window,MainViewModel vm)
    {
        window.UpdateLayout();
        if(!window.ReadCameraSettingsButton.IsEnabled||!window.ExposureTextBox.IsEnabled||!window.GainTextBox.IsEnabled)
            throw new Exception("Camera parameters must be editable once connected.");
        if(!vm.CameraInfo.Contains("FAKE-MODEL",StringComparison.Ordinal))
            throw new Exception($"Camera identity was not read after connecting: {vm.CameraInfo}");
        if(!vm.ExposureRange.Contains("1000000",StringComparison.Ordinal)||vm.GainRange.Length==0)
            throw new Exception($"Camera limits were not read from the device: {vm.ExposureRange} / {vm.GainRange}");
        if(window.GammaTextBox.IsEnabled||window.BlackLevelTextBox.IsEnabled)
            throw new Exception("Optional camera values must stay disabled until they are enabled.");
        // The device reports Gamma but no BlackLevel node, so the form must offer only what exists.
        if(!window.GammaCheckBox.IsEnabled||window.BlackLevelCheckBox.IsEnabled)
            throw new Exception("Camera parameter availability must follow the device parameter list.");
        vm.GammaEnabled=true;
        window.UpdateLayout();
        if(!window.GammaTextBox.IsEnabled)throw new Exception("Enabling Gamma must enable its value.");
        vm.GammaEnabled=false;
        if(window.AdvancedCameraPanel.Visibility==Visibility.Visible)
            throw new Exception("Advanced camera parameters must start collapsed.");
        vm.ShowAdvancedCamera=true;
        window.UpdateLayout();
        if(window.AdvancedCameraPanel.Visibility!=Visibility.Visible||window.SensorWidthTextBox.IsEnabled)
            throw new Exception("Advanced group must expand with the sensor ROI still disabled.");
        vm.ShowAdvancedCamera=false;
    }
    /// <summary>RUN must show the measured cycle time next to the verdict, with its spread, not just an average.</summary>
    private static void VerifyTimingReadout(MainWindow window,MainViewModel vm)
    {
        window.UpdateLayout();
        foreach(var fragment in new[]{"910","TB","p95","max","n=2"})
            if(!window.CycleTimingText.Text.Contains(fragment,StringComparison.Ordinal))
                throw new Exception($"Cycle timing read-out is missing '{fragment}': {window.CycleTimingText.Text}");
        if(window.RunAcquisitionSummaryText.Text.Length==0||window.AcquisitionSummaryText.Text.Length==0)
            throw new Exception("Acquisition diagnostics must be visible in SETTING and RUN.");
        _=vm;
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
    private static void VerifyCameraControls(MainWindow window,bool search,bool connect,bool disconnect,bool acquisition,bool parameters)
    {
        window.UpdateLayout();
        if(window.ScanCameraButton.IsEnabled!=search||window.ConnectCameraButton.IsEnabled!=connect||
           window.DisconnectCameraButton.IsEnabled!=disconnect||window.AcquisitionButton.IsEnabled!=acquisition)
            throw new Exception($"Camera action state is invalid. Expected search/connect/disconnect/acquisition {search}/{connect}/{disconnect}/{acquisition}.");
        if(window.CameraSelector.IsEnabled!=connect||window.ExposureTextBox.IsEnabled!=parameters||
           window.GainTextBox.IsEnabled!=parameters||window.ApplyCameraParametersButton.IsEnabled!=parameters)
            throw new Exception($"Camera selector/parameter state is invalid. Expected {connect}/{parameters}.");
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
    /// <summary>Synthetic frame source for the offline smoke. It never touches acquisition hardware.</summary>
    private sealed class SmokeCamera:ICamera
    {
        public const int FrameWidth=640;
        public const int FrameHeight=360;
        private readonly object gate=new();
        private bool grabbing;
        private int grabs;
        public IReadOnlyList<CameraDevice> Enumerate()=>[new("smoke-live","Synthetic smoke camera","smoke",false)];
        public void Open(CameraDevice device){}
        public CameraSettings? Applied{get;private set;}
        public CameraInfo ReadInfo()=>new("FAKE-MODEL","SN-FAKE","Mono8",FrameWidth,FrameHeight,30,35);
        public IReadOnlyList<CameraParameterInfo> DescribeParameters()=>
        [
            new("ExposureTime","us",10,1000000,0,10000,true),
            new("Gain","dB",0,20,0,0,true),
            new("Gamma",string.Empty,0,4,0,0.7,true),
            new("Width","px",8,FrameWidth,8,FrameWidth,true),
            new("Height","px",8,FrameHeight,8,FrameHeight,true)
        ];
        public CameraSettings ReadSettings()=>Applied??new(10000,0);
        public void ApplySettings(CameraSettings settings)=>Applied=settings;
        private CameraTrigger trigger=CameraTrigger.FreeRun;
        private int pulses;
        /// <summary>Simulates one pulse on the trigger line.</summary>
        public void Pulse(){lock(gate)pulses++;}
        public long Frames{get{lock(gate)return frames;}}
        private long frames;
        public void ConfigureTrigger(CameraTrigger value)
        {
            lock(gate)
            {
                if(grabbing)throw new InvalidOperationException("Stop acquisition before changing the trigger.");
                trigger=value;pulses=0;
            }
        }
        public void ExecuteSoftwareTrigger()
        {
            lock(gate)
            {
                if(trigger.Source!=CameraTriggerSource.Software)throw new InvalidOperationException("Not in software trigger mode.");
                pulses++;
            }
        }
        public void Start(){lock(gate)grabbing=true;}
        public ImageFrame Grab(int timeoutMs)
        {
            lock(gate)
            {
                if(!grabbing)throw new InvalidOperationException("Acquisition has not started.");
                // A triggered camera stays silent until it is pulsed.
                if(trigger.IsTriggered)
                {
                    if(pulses==0)throw new TimeoutException("No trigger pulse.");
                    pulses--;
                }
                var stride=FrameWidth*3;
                var pixels=new byte[stride*FrameHeight];
                for(var y=0;y<FrameHeight;y++)
                    for(var x=0;x<FrameWidth;x++)
                    {
                        var i=y*stride+x*3;
                        pixels[i]=(byte)(x%256);pixels[i+1]=(byte)(y%256);pixels[i+2]=(byte)((grabs+x+y)%256);
                    }
                grabs++;frames++;
                return new(FrameWidth,FrameHeight,stride,pixels,Guid.NewGuid(),DateTimeOffset.UtcNow,"SYNTHETIC SMOKE CAMERA · NOT LIVE");
            }
        }
        public void Stop(){lock(gate)grabbing=false;}
        public void Close(){}
        public void Dispose(){}
    }
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
