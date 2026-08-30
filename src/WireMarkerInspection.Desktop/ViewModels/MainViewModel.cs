using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WireMarkerInspection.Application;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Infrastructure;
using WireMarkerInspection.Vision;
using WireMarkerInspection.Desktop.Services;

namespace WireMarkerInspection.Desktop.ViewModels;
public sealed class RecipeRow(Recipe recipe,FileRecipeStore store)
{
    public Recipe Recipe{get;}=recipe;
    public string Code=>Recipe.ModelCode;
    public string Name=>Recipe.Name;
    public string Revision=>$"v{Recipe.Revision}";
    public string FirstExpected=>string.Join("\n",Recipe.Ends[0].ExpectedLines);
    public string SecondExpected=>string.Join("\n",Recipe.Ends[1].ExpectedLines);
    private BitmapSource? first,second;
    public BitmapSource First=>first??=ImageFiles.Decode(store.LoadReference(Recipe,0),112);
    public BitmapSource Second=>second??=ImageFiles.Decode(store.LoadReference(Recipe,1),112);
}
public sealed record ModelIdentity(string Code,string Name);
public enum CameraUiState { Idle, Finding, NotFound, Found, Connected, Acquiring, Reconnecting, Error }
public partial class MainViewModel : ObservableObject
{
    public FileRecipeStore Store{get;}
    public NativeOcrEngine Ocr{get;}
    public ICamera Camera{get;}
    public bool AutoDiscoverCameraOnLoad{get;}
    public InspectionSession Session{get;}
    public AcquisitionDiagnostics Diagnostics{get;}=new();
    public CycleStatistics CycleTimes{get;}=new();
    public IDiagnosticsLog Log{get;}
    /// <summary>Consecutive reconnect attempts before acquisition gives up. Zero disables reconnecting.</summary>
    public int ReconnectAttempts{get;init;}=4;
    public EndEditorViewModel End1{get;}=new(1);
    public EndEditorViewModel End2{get;}=new(2);
    public EndResultViewModel Result1{get;}=new(1);
    public EndResultViewModel Result2{get;}=new(2);
    public ObservableCollection<RecipeRow> Models{get;}=[];
    public ICollectionView ModelsView{get;}
    public ObservableCollection<CameraDevice> Cameras{get;}=[];
    private Recipe? saved;
    private Guid draftId=Guid.NewGuid();
    private bool loading;
    private bool modelSetupActive;
    private ImageFrame? latest;
    private IReadOnlyList<CameraParameterInfo> cameraParameters=[];
    private bool loadingCamera;
    private CameraDevice? connectedDevice;
    private CameraTrigger cameraTrigger=CameraTrigger.FreeRun;
    private TriggerRouter router=new(new TriggerSettings());
    private ITriggerSource? activeTrigger;
    private readonly ManualTriggerSource manualTrigger=new();
    private CameraSettings? appliedSettings;
    private long latestTimestamp;
    private static readonly TimeSpan[] ReconnectDelays=
        [TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(10)];
    private bool liveFrameReady;
    private readonly System.Windows.Threading.Dispatcher dispatcher;
    private static readonly TimeSpan LiveFrameMaxAge=TimeSpan.FromSeconds(2);
    private CancellationTokenSource? acquisition;
    private Task? acquisitionTask;
    private DateTimeOffset waitingSince;
    private Recipe? runtimeRecipe;
    private readonly TimeSpan cameraSearchTimeout;
    private Task<IReadOnlyList<CameraDevice>>? cameraDiscoveryTask;
    [ObservableProperty]private RecipeRow? selectedModel;
    [ObservableProperty]private CameraDevice? selectedCamera;
    [ObservableProperty]private string modelCode="";
    [ObservableProperty]private string modelName="";
    [ObservableProperty]private string search="";
    [ObservableProperty]private bool dirty;
    [ObservableProperty]private bool busy;
    [ObservableProperty]private bool running;
    [ObservableProperty]private bool runPage;
    [ObservableProperty]private bool cameraConnected;
    [ObservableProperty]private bool acquiring;
    [ObservableProperty]private bool findingCamera;
    [ObservableProperty]private CameraUiState cameraState=CameraUiState.Idle;
    [ObservableProperty]private BitmapSource? liveImage;
    [ObservableProperty]private string sourceStatus="OFFLINE";
    [ObservableProperty]private string cameraStatus="Chưa kết nối camera";
    [ObservableProperty]private string exposure="10000";
    [ObservableProperty]private string gain="0";
    [ObservableProperty]private bool gammaEnabled;
    [ObservableProperty]private string gamma="1";
    [ObservableProperty]private bool blackLevelEnabled;
    [ObservableProperty]private string blackLevel="0";
    [ObservableProperty]private bool sensorRoiEnabled;
    [ObservableProperty]private string sensorOffsetX="0";
    [ObservableProperty]private string sensorOffsetY="0";
    [ObservableProperty]private string sensorWidth="0";
    [ObservableProperty]private string sensorHeight="0";
    [ObservableProperty]private bool strobeEnabled;
    [ObservableProperty]private string strobeLine="0";
    [ObservableProperty]private string strobeDuration="0";
    [ObservableProperty]private string strobeDelay="0";
    [ObservableProperty]private string cameraInfo="Chưa kết nối camera.";
    [ObservableProperty]private bool showAdvancedCamera;
    [ObservableProperty]private TriggerKind triggerKind=TriggerKind.Manual;
    [ObservableProperty]private TriggerMapping triggerMapping=TriggerMapping.Shared;
    [ObservableProperty]private string triggerLine="0";
    [ObservableProperty]private bool triggerRisingEdge=true;
    [ObservableProperty]private string triggerDelay="0";
    [ObservableProperty]private string triggerDebouncer="1000";
    [ObservableProperty]private string triggerRepeatBlock="250";
    [ObservableProperty]private string triggerStatus="Trigger thủ công";
    [ObservableProperty]private string lastTrigger="Chưa có trigger.";
    [ObservableProperty]private string message="Chọn một model hoặc Add Model để bắt đầu setup.";
    [ObservableProperty]private string runStatus="CHƯA CHẠY";
    [ObservableProperty]private string ocrStatus="";
    public bool CanEdit=>!Running&&!Busy;
    public bool CanConfigureModel=>CanEdit&&modelSetupActive;
    public bool CanSaveRecipe=>CanConfigureModel&&Dirty;
    public bool CanManageSelectedModel=>CanEdit&&SelectedModel!=null;
    public bool CanSearchCamera=>CanEdit&&!FindingCamera&&!CameraConnected&&!Acquiring;
    public bool CanSelectCamera=>CanEdit&&!FindingCamera&&!CameraConnected&&Cameras.Count>0;
    public bool CanConnectCamera=>CanSelectCamera&&SelectedCamera!=null;
    public bool CanDisconnectCamera=>CanEdit&&CameraConnected&&!Acquiring;
    public bool CanToggleAcquisition=>CanEdit&&CameraConnected;
    public bool CanEditCameraParameters=>CanEdit&&CameraConnected&&!Acquiring;
    public bool HasLiveFrame=>Acquiring&&latest!=null&&DateTimeOffset.UtcNow-latest.CapturedAt<=LiveFrameMaxAge;
    public string ExposureRange=>Range("ExposureTime");
    public string GainRange=>Range("Gain");
    public string GammaRange=>Range("Gamma");
    public string BlackLevelRange=>Range("BlackLevel");
    public string SensorRange=>cameraParameters.Count==0?"":$"Width {Range("Width")} · Height {Range("Height")}";
    private string Range(string parameter)=>
        cameraParameters.FirstOrDefault(p=>p.Name==parameter) is{}info
            ?$"{info.Minimum:0.###} – {info.Maximum:0.###} {info.Unit}".TrimEnd()
            :cameraParameters.Count==0?"":"camera không hỗ trợ";
    private bool Supports(string parameter)=>cameraParameters.Any(p=>p.Name==parameter);
    public bool CanEditGamma=>CanEditCameraParameters&&Supports("Gamma");
    public bool CanEditBlackLevel=>CanEditCameraParameters&&Supports("BlackLevel");
    public bool CanEditSensorRoi=>CanEditCameraParameters&&Supports("Width")&&Supports("Height");
    // Strobe nodes only become readable after a line is selected, so availability cannot be probed up front.
    public bool CanEditStrobe=>CanEditCameraParameters;
    public bool CanGrabReference=>CanConfigureModel&&HasLiveFrame;
    public string AcquisitionActionLabel=>Acquiring?"Stop Acquisition":"Start Acquisition";
    public bool CanCapture=>Running&&!Busy&&Session.State is InspectionState.WaitingEnd1 or InspectionState.WaitingEnd2;
    public bool CanNext=>Running&&!Busy&&Session.State==InspectionState.Completed;
    public string CaptureLabel=>$"Nhận ảnh đầu {Session.NextEnd+1}";
    public string ModelCount=>$"{Models.Count} MODELS";
    public TriggerKind[] TriggerKinds{get;}=[TriggerKind.Manual,TriggerKind.CameraLine];
    public TriggerMapping[] TriggerMappings{get;}=[TriggerMapping.Shared,TriggerMapping.PerEnd];
    public bool CanEditTrigger=>CanEdit&&!Running;
    public bool CanRetakeEnd=>Running&&!Busy&&Session.State==InspectionState.WaitingEnd2;
    public string CycleTimingText
    {
        get
        {
            var (count,average,p95,max)=CycleTimes.Summary();
            return count==0?"Chưa có số liệu thời gian xử lý."
                :$"Chu kỳ {CycleTimes.Last:0} ms · TB {average:0} · p95 {p95:0} · max {max:0} ms (n={count})";
        }
    }
    public string AcquisitionSummary
    {
        get
        {
            var snapshot=Diagnostics.Snapshot();
            if(snapshot.Frames==0&&snapshot.Uptime==TimeSpan.Zero)return "Chưa chạy acquisition.";
            var text=$"Frame {snapshot.Frames} · timeout {snapshot.Timeouts} · reconnect {snapshot.Reconnects}";
            if(snapshot.FramesPerSecond is{}fps)text+=$" · {fps:0.0} fps";
            if(snapshot.ReconnectFailures>0)text+=$" · lỗi nối lại {snapshot.ReconnectFailures}";
            return snapshot.LastError is{}error?$"{text} · lỗi cuối: {error}":text;
        }
    }
    public string CycleLabel=>Session.CycleId==Guid.Empty?"—":Session.CycleId.ToString("N")[..12].ToUpperInvariant();
    public string ActiveModel=>runtimeRecipe is null?"CHƯA CHỌN":$"{runtimeRecipe.ModelCode} / v{runtimeRecipe.Revision}";
    public Func<string,bool>? Confirm{get;set;}
    public MainViewModel(string dataRoot,ICamera? camera=null,bool autoDiscoverCameraOnLoad=true,TimeSpan? cameraSearchTimeout=null)
    {
        Camera=camera??new HikrobotMvsCamera();
        // The view model is created on the UI thread; the acquisition loop marshals frames back through this dispatcher.
        dispatcher=System.Windows.Application.Current?.Dispatcher??System.Windows.Threading.Dispatcher.CurrentDispatcher;
        AutoDiscoverCameraOnLoad=autoDiscoverCameraOnLoad;
        this.cameraSearchTimeout=cameraSearchTimeout??TimeSpan.FromSeconds(5);
        if(this.cameraSearchTimeout<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(cameraSearchTimeout));
        Store=new(dataRoot);Ocr=new(Path.Combine(AppContext.BaseDirectory,"assets","ocr"));
        Log=new FileDiagnosticsLog(dataRoot);
        Session=new(Ocr,new FileResultStore(dataRoot));
        ModelsView=CollectionViewSource.GetDefaultView(Models);
        ModelsView.Filter=o=>o is RecipeRow r&&(r.Code.Contains(Search,StringComparison.OrdinalIgnoreCase)||r.Name.Contains(Search,StringComparison.OrdinalIgnoreCase));
        End1.Changed+=(_,_)=>{if(!loading)Dirty=true;};End2.Changed+=(_,_)=>{if(!loading)Dirty=true;};
        Reload();RefreshOcr();
    }
    partial void OnSearchChanged(string value)=>ModelsView.Refresh();
    partial void OnModelCodeChanged(string value){if(!loading)Dirty=true;}
    partial void OnModelNameChanged(string value){if(!loading)Dirty=true;}
    partial void OnDirtyChanged(bool value)=>OnPropertyChanged(nameof(CanSaveRecipe));
    partial void OnBusyChanged(bool value)=>RefreshState();
    partial void OnRunningChanged(bool value)=>RefreshState();
    partial void OnSelectedCameraChanged(CameraDevice? value)=>RefreshCameraState();
    partial void OnCameraConnectedChanged(bool value)=>RefreshCameraState();
    partial void OnAcquiringChanged(bool value)=>RefreshCameraState();
    partial void OnFindingCameraChanged(bool value)=>RefreshCameraState();
    private static readonly HashSet<string> CameraDraftProperties=
    [
        nameof(Exposure),nameof(Gain),nameof(GammaEnabled),nameof(Gamma),nameof(BlackLevelEnabled),nameof(BlackLevel),
        nameof(SensorRoiEnabled),nameof(SensorOffsetX),nameof(SensorOffsetY),nameof(SensorWidth),nameof(SensorHeight),
        nameof(StrobeEnabled),nameof(StrobeLine),nameof(StrobeDuration),nameof(StrobeDelay)
    ];
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Camera settings are stored with the recipe, so changing them is an unsaved recipe change.
        if(!loading&&!loadingCamera&&modelSetupActive&&e.PropertyName is{}name&&CameraDraftProperties.Contains(name))
            Dirty=true;
    }
    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanEdit));OnPropertyChanged(nameof(CanConfigureModel));OnPropertyChanged(nameof(CanSaveRecipe));OnPropertyChanged(nameof(CanManageSelectedModel));
        OnPropertyChanged(nameof(CanCapture));OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanEditTrigger));OnPropertyChanged(nameof(CanRetakeEnd));
        RetakeEndCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CaptureLabel));OnPropertyChanged(nameof(CycleLabel));OnPropertyChanged(nameof(ActiveModel));
        RefreshCameraState();
    }
    private void RefreshCameraState()
    {
        OnPropertyChanged(nameof(CanSearchCamera));OnPropertyChanged(nameof(CanSelectCamera));OnPropertyChanged(nameof(CanConnectCamera));
        OnPropertyChanged(nameof(CanDisconnectCamera));OnPropertyChanged(nameof(CanToggleAcquisition));OnPropertyChanged(nameof(CanEditCameraParameters));
        OnPropertyChanged(nameof(AcquisitionActionLabel));
        RefreshCameraCapabilities();
        RefreshGrabState();
    }
    private void RefreshGrabState()
    {
        var ready=CanGrabReference;
        if(ready==liveFrameReady)return;
        liveFrameReady=ready;
        OnPropertyChanged(nameof(HasLiveFrame));OnPropertyChanged(nameof(CanGrabReference));
        GrabReferenceCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedModelChanged(RecipeRow? oldValue,RecipeRow? newValue)
    {
        OnPropertyChanged(nameof(CanManageSelectedModel));
        if(loading)return;
        if(Dirty&&Confirm?.Invoke(newValue==null?"Bỏ thay đổi chưa lưu và bỏ chọn model?":"Bỏ thay đổi chưa lưu và mở model đã chọn?")==false)
        {
            RestoreSelection(oldValue,newValue);
            Message="Giữ lại thay đổi chưa lưu. Save Recipe hoặc chọn lại model khác.";
            return;
        }
        if(newValue==null)
        {
            ClearModelSetup();
            Message="Chọn một model hoặc Add Model để bắt đầu setup.";
            return;
        }
        try{Load(newValue.Recipe);SetModelSetupActive(true);}
        catch(Exception ex)
        {
            RestoreSelection(oldValue,newValue);Message=ex.Message;
        }
    }
    /// <summary>
    /// Puts the selection back after the operator declined the change or the recipe failed to load.
    /// The originating DataGrid/ComboBox is still inside its own selection change at this point, so
    /// writing SelectedItem back synchronously re-enters DataGrid selection, which builds a
    /// DataGridItemAutomationPeer for a null item and crashes the application. Restore afterwards.
    /// </summary>
    private void RestoreSelection(RecipeRow? previous,RecipeRow? rejected)
    {
        if(dispatcher.HasShutdownStarted||dispatcher.HasShutdownFinished)return;
        dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>
        {
            // A later selection may already have superseded the rejected one.
            if(!ReferenceEquals(SelectedModel,rejected))return;
            loading=true;try{SelectedModel=previous;}finally{loading=false;}
            OnPropertyChanged(nameof(CanManageSelectedModel));
        }));
    }
    private void SetModelSetupActive(bool value)
    {
        if(modelSetupActive==value)return;
        modelSetupActive=value;OnPropertyChanged(nameof(CanConfigureModel));OnPropertyChanged(nameof(CanSaveRecipe));RefreshGrabState();
    }
    private void ClearModelSetup()
    {
        loading=true;
        try{saved=null;draftId=Guid.NewGuid();ModelCode="";ModelName="";End1.Clear();End2.Clear();Dirty=false;SetModelSetupActive(false);}
        finally{loading=false;}
    }
    private void Load(Recipe recipe)
    {
        // Decode before replacing current editor state to avoid a partially loaded recipe.
        var p1=Store.LoadReference(recipe,0);var p2=Store.LoadReference(recipe,1);
        var image1=ImageFiles.Decode(p1);var image2=ImageFiles.Decode(p2);
        if(image1.PixelWidth!=recipe.Ends[0].Width || image1.PixelHeight!=recipe.Ends[0].Height ||
           image2.PixelWidth!=recipe.Ends[1].Width || image2.PixelHeight!=recipe.Ends[1].Height)
            throw new InvalidDataException("Reference dimensions do not match recipe. Repair the recipe before loading.");
        loading=true;
        try {saved=recipe;draftId=recipe.Id;ModelCode=recipe.ModelCode;ModelName=recipe.Name;End1.Load(recipe.Ends[0],p1);End2.Load(recipe.Ends[1],p2);Dirty=false;}
        finally {loading=false;}
        Message=$"Đã nạp {recipe.ModelCode} · v{recipe.Revision}.";
        if(recipe.Camera is not{}settings)return;
        ShowCameraSettings(settings);
        if(!CameraConnected)return;
        if(Acquiring){Message+=" Dừng acquisition rồi Apply để áp thông số camera của model.";return;}
        try{Camera.ApplySettings(settings);appliedSettings=settings;RefreshCameraParameters();Message+=" Đã áp thông số camera của model.";}
        catch(Exception ex){Message+=$" Không áp được thông số camera: {ex.Message}";}
    }
    private void Reload()
    {
        Models.Clear();foreach(var recipe in Store.LoadAll())Models.Add(new(recipe,Store));
        OnPropertyChanged(nameof(ModelCount));
        if(Store.LoadErrors.Count>0)Message="Không nạp được một số recipe: "+string.Join(" | ",Store.LoadErrors);
    }
    [RelayCommand]private void RefreshOcr()=>OcrStatus=Ocr.AvailabilityError??"OCR assets sẵn sàng · chưa xác nhận độ chính xác.";
    public string? ValidateModelIdentity(ModelIdentity identity,Guid? existingId=null)
    {
        var code=identity.Code.Trim();var name=identity.Name.Trim();
        if(code.Length==0)return "Nhập mã model.";
        if(name.Length==0)return "Nhập tên model.";
        if(code.Length>64)return "Mã model tối đa 64 ký tự.";
        if(name.Length>128)return "Tên model tối đa 128 ký tự.";
        if(Models.Any(row=>row.Recipe.Id!=existingId&&string.Equals(row.Code,code,StringComparison.OrdinalIgnoreCase)))
            return $"Mã model {code} đã tồn tại.";
        return null;
    }
    [RelayCommand]private void NewModel(ModelIdentity identity)=>Guard(()=>
    {
        if(!CanEdit || (Dirty&&Confirm?.Invoke("Bỏ thay đổi chưa lưu và tạo model mới?")==false))return;
        if(ValidateModelIdentity(identity) is { } error)throw new InvalidOperationException(error);
        loading=true;
        try
        {
            SelectedModel=null;saved=null;draftId=Guid.NewGuid();
            ModelCode=identity.Code.Trim();ModelName=identity.Name.Trim();
            End1.Clear();End2.Clear();Dirty=true;SetModelSetupActive(true);
        }
        finally{loading=false;}
        Message=$"Model mới {ModelCode} · cần hai ảnh, ROI, text mẫu, Apply và Save Recipe.";
    });
    [RelayCommand]private void EditModel(ModelIdentity identity)=>Guard(()=>
    {
        if(!CanEdit||SelectedModel==null)return;
        var target=SelectedModel.Recipe;
        if(Dirty && Confirm?.Invoke("Bỏ thay đổi chưa lưu và mở model đã chọn?")==false)return;
        if(ValidateModelIdentity(identity,target.Id) is { } error)throw new InvalidOperationException(error);
        Load(target);
        loading=true;
        try
        {
            ModelCode=identity.Code.Trim();ModelName=identity.Name.Trim();
            Dirty=ModelCode!=target.ModelCode||ModelName!=target.Name;
        }
        finally{loading=false;}
        Message=Dirty?$"Đang sửa {target.ModelCode} · Apply nếu đổi Recipe, sau đó Save Recipe.":$"Đã nạp {target.ModelCode} · v{target.Revision}.";
    });
    [RelayCommand]private void SaveRecipe()=>Guard(()=>
    {
        if(!CanSaveRecipe)return;
        if(!End1.Applied||!End2.Applied)throw new InvalidOperationException("Apply cả hai đầu trước khi Save Recipe.");
        var draft=new Recipe(draftId,ModelCode.Trim(),ModelName.Trim(),saved?.Revision??0,[End1.Spec(),End2.Spec()],
            DateTimeOffset.UtcNow,1,BuildCameraSettings());
        var updated=Store.Save(draft,[ImageFiles.Png(End1.Image!),ImageFiles.Png(End2.Image!)]);
        loading=true;try{Reload();SelectedModel=Models.First(r=>r.Recipe.Id==updated.Id);}finally{loading=false;}
        Load(updated);Message=$"Đã lưu {updated.ModelCode} · v{updated.Revision}.";
    });
    [RelayCommand]private void DeleteModel()=>Guard(()=>
    {
        if(!CanEdit||SelectedModel==null)return;
        var target=SelectedModel.Recipe;
        if(Confirm?.Invoke($"Xóa model {target.ModelCode}? Ảnh mẫu vẫn được giữ để phục hồi.")!=true)return;
        Store.Delete(target.Id);
        if(saved?.Id==target.Id)ClearModelSetup();
        Reload();SelectedModel=saved==null?null:Models.FirstOrDefault(r=>r.Recipe.Id==saved.Id);
    });
    [RelayCommand]private void LoadReference(string end)=>Guard(()=>
    {
        if(!CanConfigureModel)return;
        var file=ChooseImage();if(file==null)return;
        Editor(end).SetFrame(ImageFiles.Load(file));
    });
    [RelayCommand(CanExecute=nameof(CanGrabReference))]private void GrabReference(string end)=>Guard(()=>
    {
        if(!CanConfigureModel)return;
        var live=latest;
        if(!HasLiveFrame||live==null)
            throw new InvalidOperationException(Acquiring
                ?"Frame live đã quá cũ. Chờ frame mới rồi Grab Image, hoặc dùng Load Image."
                :"Chưa có ảnh live. Kết nối camera và Start Acquisition trước khi Grab Image.");
        // Each end owns its own copy; ends must never share a captured buffer.
        var editor=Editor(end);
        editor.SetFrame(live with {Bgr=[..live.Bgr],Id=Guid.NewGuid()});
        Message=$"Đầu {editor.Number} · đã lấy ảnh live {live.Width} × {live.Height} từ {live.Source}.";
    });
    [RelayCommand]private void ApplyEnd(string end)=>Guard(()=>{if(CanConfigureModel)Editor(end).Apply();});
    [RelayCommand]private async Task TestOcrAsync(string end)
    {
        if(!CanConfigureModel)return;
        await GuardAsync(async()=>
        {
            var editor=Editor(end);var spec=editor.Spec();
            if(spec.Roi.Validate(spec.Width,spec.Height)is{}error)throw new InvalidOperationException(error);
            editor.ShowReading(await Ocr.ReadAsync(editor.Frame!,spec,CancellationToken.None));
        });
    }
    private EndEditorViewModel Editor(string end)=>end=="1"?End1:End2;
    [RelayCommand]private void Setting()
    {
        if(Running){Message="Stop RUN trước khi quay lại SETTING.";return;}RunPage=false;
    }
    [RelayCommand]private async Task StartRunAsync()
    {
        if(Busy||Running)return;
        RunPage=true;RefreshOcr();
        var started=false;
        await GuardAsync(async()=>
        {
            if(saved==null||Dirty)throw new InvalidOperationException("Cần lưu recipe trước khi RUN.");
            if(Ocr.AvailabilityError is{}error)throw new InvalidOperationException(error);
            var settings=BuildTriggerSettings();
            await ArmTriggerAsync(settings);
            runtimeRecipe=saved.Copy();Session.Begin(runtimeRecipe);Running=true;started=true;
            Result1.Reset(runtimeRecipe.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);waitingSince=DateTimeOffset.UtcNow;
            RunStatus="CHỜ ĐẦU 1";
        });
        Message=started
            ?$"RUN đã bắt đầu · {TriggerStatus}."
            :$"RUN không bắt đầu được · {Message}";
        RefreshState();
    }
    [RelayCommand]private async Task StopRunAsync()
    {
        Session.Stop();Running=false;RunStatus="ĐÃ DỪNG";
        await GuardAsync(DisarmTriggerAsync);
        RefreshState();
    }
    public void StopRun(){Session.Stop();Running=false;RunStatus="ĐÃ DỪNG";RefreshState();}

    /// <summary>
    /// Switching between free-run and a triggered source is an acquisition lifecycle change: MVS will not
    /// accept a trigger-source change while grabbing, so acquisition is stopped and restarted around it.
    /// </summary>
    public async Task ArmTriggerAsync(TriggerSettings settings)
    {
        await DisarmTriggerAsync();
        router=new(settings);
        ITriggerSource source=settings.Kind==TriggerKind.CameraLine
            ?new CameraLineTriggerSource(Camera,settings.CameraTrigger)
            :manualTrigger;
        if(settings.CameraTrigger.IsTriggered)
        {
            if(!CameraConnected)throw new InvalidOperationException("Trigger phần cứng cần camera đang kết nối.");
            await SwitchAcquisitionAsync(settings.CameraTrigger,()=>source.StartAsync(CancellationToken.None));
        }
        else await source.StartAsync(CancellationToken.None);
        source.Fired+=OnTriggerFired;
        activeTrigger=source;TriggerStatus=source.Status;
        Log.Write("trigger","armed",new Dictionary<string,object?>
        {
            ["kind"]=settings.Kind.ToString(),["mapping"]=settings.Mapping.ToString(),
            ["line"]=settings.CameraTrigger.Line,["repeatBlockMs"]=settings.RepeatBlockMs
        });
    }

    public async Task DisarmTriggerAsync()
    {
        if(activeTrigger is not{}source)return;
        source.Fired-=OnTriggerFired;
        activeTrigger=null;
        // The source owns the device configuration, so restoring free-run must happen exactly once,
        // inside the stopped window. Configuring it again afterwards would hit a grabbing camera.
        if(cameraTrigger.IsTriggered&&CameraConnected)await SwitchAcquisitionAsync(CameraTrigger.FreeRun,source.StopAsync);
        else await source.StopAsync();
        if(!ReferenceEquals(source,manualTrigger))await source.DisposeAsync();
        TriggerStatus=manualTrigger.Status;
        Log.Write("trigger","disarmed",null);
    }

    private async Task SwitchAcquisitionAsync(CameraTrigger trigger,Func<Task> configure)
    {
        var resume=Acquiring;
        if(resume)await StopAcquisitionAsync();
        await configure();
        cameraTrigger=trigger;
        if(resume)await StartAcquisitionAsync();
    }

    private void OnTriggerFired(object? sender,TriggerEvent signal)
    {
        if(!dispatcher.CheckAccess()){dispatcher.InvokeAsync(()=>OnTriggerFired(sender,signal));return;}
        var decision=router.Route(signal,Session.State,Session.NextEnd);
        LastTrigger=$"{DateTimeOffset.Now:HH:mm:ss} · {decision.Reason}";
        if(!decision.Accepted)
        {
            // An ignored trigger is never silent: a mis-wired line shows up here instead of as a lost product.
            Log.Write("trigger","ignored",new Dictionary<string,object?>{["reason"]=decision.Reason,["source"]=signal.Source});
            return;
        }
        Log.Write("trigger","accepted",new Dictionary<string,object?>{["end"]=decision.End+1,["source"]=signal.Source});
        _=CaptureRuntimeCommand.ExecuteAsync(null);
    }

    /// <summary>Drops a bad first image so the operator can shoot it again without losing the product.</summary>
    [RelayCommand(CanExecute=nameof(CanRetakeEnd))]private void RetakeEnd()=>Guard(()=>
    {
        if(!CanRetakeEnd||runtimeRecipe==null)return;
        if(!Session.RetakeLastEnd())return;
        Result1.Reset(runtimeRecipe.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);
        router.Reset();waitingSince=DateTimeOffset.UtcNow;RunStatus="CHỜ ĐẦU 1";
        Message="RUN · Đã bỏ ảnh đầu 1. Chụp lại đầu 1.";RefreshState();
    });
    [RelayCommand]private void NextProduct()=>Guard(()=>
    {
        if(!CanNext)return;
        Session.Begin(runtimeRecipe!);Result1.Reset(runtimeRecipe!.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);
        router.Reset();waitingSince=DateTimeOffset.UtcNow;RunStatus="CHỜ ĐẦU 1";RefreshState();
    });
    [RelayCommand]private async Task LoadRuntimeAsync()
    {
        if(!CanCapture)return;
        var file=ChooseImage();if(file==null)return;
        await GuardAsync(()=>InspectAsync(ImageFiles.Load(file)));
    }
    /// <summary>Manual capture is the manual trigger source, so it follows the same routing rules.</summary>
    [RelayCommand]private void ManualTrigger()
    {
        if(!Running){Message="RUN chưa bắt đầu.";return;}
        manualTrigger.Fire(TriggerMapping==TriggerMapping.PerEnd?Session.NextEnd:null,"thao tác tay");
    }
    [RelayCommand]private async Task CaptureRuntimeAsync()
    {
        if(!CanCapture)return;
        await GuardAsync(async()=>
        {
            if(!Acquiring||latest==null||latest.CapturedAt<waitingSince||DateTimeOffset.UtcNow-latest.CapturedAt>TimeSpan.FromSeconds(2))
                throw new InvalidOperationException("Chưa có camera frame mới cho đầu này.");
            if(latest.Source=="SIMULATION")throw new InvalidOperationException("Không dùng ảnh camera giả lập cho RUN sản xuất.");
            await InspectAsync(latest,MonotonicClock.MillisecondsSince(latestTimestamp));
        });
    }
    private async Task InspectAsync(ImageFrame frame,double frameAgeMs=0)
    {
        var end=Session.NextEnd;var target=end==0?Result1:Result2;target.Show(frame,null);
        RunStatus=$"ĐANG OCR ĐẦU {end+1}";
        try
        {
            var result=await Session.AcceptAsync(frame,frameAgeMs);
            if(result==null)return;
            target.Show(frame,result);waitingSince=DateTimeOffset.UtcNow;
            RecordTimings(result);
            RunStatus=Session.Result?.Verdict.ToString().ToUpperInvariant()??"CHỜ ĐẦU 2";
            Message=Session.Result!=null?"Đã lưu kết quả và ảnh nguồn. Sản phẩm tiếp theo hoặc Stop.":"Đã kiểm tra đầu 1. Chờ đầu 2 cùng sản phẩm.";
        }
        catch {target.Status="ERROR";RunStatus="ERROR";throw;}
        finally{RefreshState();}
    }
    private void RecordTimings(EndResult result)
    {
        var product=Session.Result;
        Log.Write("end",result.Verdict.ToString(),new Dictionary<string,object?>
        {
            ["cycleId"]=Session.CycleId,["ocrMs"]=result.MillisecondsOf("ocr"),
            ["compareMs"]=result.MillisecondsOf("compare"),["frameAgeMs"]=result.MillisecondsOf("frame-age"),
            ["endMs"]=result.MillisecondsOf("end")
        });
        if(product==null)return;
        var cycleMs=product.Timings?.FirstOrDefault(t=>t.Stage=="cycle")?.Milliseconds??0;
        RecordCycleTime(cycleMs);
        Log.Write("cycle",product.Verdict.ToString(),new Dictionary<string,object?>
        {
            ["cycleId"]=product.CycleId,["model"]=product.Recipe.ModelCode,
            ["cycleMs"]=cycleMs,["persistMs"]=Session.LastPersistMilliseconds
        });
    }
    /// <summary>Adds a measured cycle to the rolling window and refreshes the read-out.</summary>
    public void RecordCycleTime(double milliseconds)
    {
        CycleTimes.Add(milliseconds);
        OnPropertyChanged(nameof(CycleTimingText));
    }
    public Task InitializeCameraAsync()=>ScanCamerasAsync();
    [RelayCommand]private async Task ScanCamerasAsync()
    {
        if(!CanSearchCamera)return;
        FindingCamera=true;CameraState=CameraUiState.Finding;
        CameraStatus="ĐANG TÌM CAMERA HIKROBOT...";
        Message="ACQUISITION · Đang tìm camera qua MVS.";
        Cameras.Clear();SelectedCamera=null;RefreshCameraState();
        try
        {
            cameraDiscoveryTask??=Task.Run(Camera.Enumerate);
            _=cameraDiscoveryTask.ContinueWith(task=>{_ = task.Exception;},CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            var devices=await cameraDiscoveryTask.WaitAsync(cameraSearchTimeout);
            cameraDiscoveryTask=null;
            foreach(var device in devices)Cameras.Add(device);
            SelectedCamera=Cameras.FirstOrDefault();
            if(Cameras.Count==0)
            {
                CameraState=CameraUiState.NotFound;
                CameraStatus="KHÔNG TÌM THẤY CAMERA";
                Message="ACQUISITION · Không tìm thấy camera Hikrobot. Kiểm tra nguồn/cáp mạng rồi bấm Tìm camera.";
            }
            else
            {
                CameraState=CameraUiState.Found;
                CameraStatus=$"ĐÃ TÌM THẤY {Cameras.Count} CAMERA · SẴN SÀNG KẾT NỐI";
                Message="ACQUISITION · Đã tìm thấy camera. Chọn thiết bị và bấm Kết nối.";
            }
        }
        catch(TimeoutException)
        {
            CameraState=CameraUiState.NotFound;
            CameraStatus=$"HẾT THỜI GIAN TÌM CAMERA ({cameraSearchTimeout.TotalSeconds:0.#}s)";
            Message="ACQUISITION · Tìm camera quá thời gian. Chỉ nút Tìm camera được mở để thử lại.";
        }
        catch(Exception ex)
        {
            cameraDiscoveryTask=null;CameraState=CameraUiState.Error;
            CameraStatus="LỖI TÌM CAMERA";
            Message=$"ACQUISITION · {ex.Message}";
        }
        finally{FindingCamera=false;RefreshCameraState();}
    }
    [RelayCommand]private async Task ConnectAsync()
    {
        if(!CanConnectCamera||SelectedCamera==null)return;
        var target=SelectedCamera;
        await GuardAsync(async()=>
        {
            await Task.Run(()=>Camera.Open(target));
            connectedDevice=target;
            CameraConnected=true;CameraState=CameraUiState.Connected;
            SourceStatus=target.IsSimulation?"SIMULATION":"CAMERA CONNECTED";
            CameraStatus=$"ĐÃ KẾT NỐI · {target.Name}";
            RefreshCameraParameters();
            Message="ACQUISITION · Kết nối camera thành công. Có thể chỉnh thông số hoặc Start Acquisition.";
        });
        if(!CameraConnected)
        {
            CameraState=CameraUiState.Error;CameraStatus="KẾT NỐI CAMERA THẤT BẠI";
        }
    }
    [RelayCommand]private async Task CameraParametersAsync()
    {
        if(!CanEditCameraParameters)return;
        var applied=false;
        await GuardAsync(()=>Task.Run(()=>
        {
            var settings=BuildCameraSettings();
            ValidateAgainstCamera(settings);
            Camera.ApplySettings(settings);
            appliedSettings=settings;applied=true;
        }));
        if(applied)
        {
            RefreshCameraParameters();
            Message="ACQUISITION · Đã áp dụng thông số camera vào thiết bị.";
        }
    }

    /// <summary>Reads the live device values back into the form so a taught setup starts from reality.</summary>
    [RelayCommand]private async Task ReadCameraSettingsAsync()
    {
        if(!CanEditCameraParameters)return;
        CameraSettings? settings=null;
        await GuardAsync(()=>Task.Run(()=>settings=Camera.ReadSettings()));
        if(settings==null)return;
        ShowCameraSettings(settings);
        RefreshCameraParameters();
        Message="ACQUISITION · Đã đọc thông số hiện tại từ camera.";
    }

    /// <summary>Builds the acquisition setup stored with the recipe from the operator's form values.</summary>
    public CameraSettings BuildCameraSettings()
    {
        var settings=new CameraSettings(
            Number(Exposure,"Exposure"),Number(Gain,"Gain"),
            GammaEnabled?Number(Gamma,"Gamma"):null,
            BlackLevelEnabled?Number(BlackLevel,"Black level"):null,
            SensorRoiEnabled?new SensorRoi(Whole(SensorOffsetX,"Offset X"),Whole(SensorOffsetY,"Offset Y"),
                Whole(SensorWidth,"Width"),Whole(SensorHeight,"Height")):null,
            StrobeEnabled?new StrobeSettings(true,Whole(StrobeLine,"Strobe line"),
                Number(StrobeDuration,"Strobe duration"),Number(StrobeDelay,"Strobe delay")):null);
        if(settings.Validate() is{}error)throw new InvalidOperationException(error);
        return settings;
    }

    private void ShowCameraSettings(CameraSettings settings)
    {
        loadingCamera=true;
        try
        {
            var culture=System.Globalization.CultureInfo.InvariantCulture;
            Exposure=settings.ExposureTimeUs.ToString("0.###",culture);
            Gain=settings.Gain.ToString("0.###",culture);
            GammaEnabled=settings.Gamma!=null;
            if(settings.Gamma is{}gamma)Gamma=gamma.ToString("0.###",culture);
            BlackLevelEnabled=settings.BlackLevel!=null;
            if(settings.BlackLevel is{}black)BlackLevel=black.ToString("0.###",culture);
            SensorRoiEnabled=settings.Roi!=null;
            if(settings.Roi is{}roi)
            {
                SensorOffsetX=roi.OffsetX.ToString(culture);SensorOffsetY=roi.OffsetY.ToString(culture);
                SensorWidth=roi.Width.ToString(culture);SensorHeight=roi.Height.ToString(culture);
            }
            StrobeEnabled=settings.Strobe?.Enabled==true;
            if(settings.Strobe is{}strobe)
            {
                StrobeLine=strobe.Line.ToString(culture);
                StrobeDuration=strobe.DurationUs.ToString("0.###",culture);
                StrobeDelay=strobe.DelayUs.ToString("0.###",culture);
            }
        }
        finally{loadingCamera=false;}
    }

    private void ValidateAgainstCamera(CameraSettings settings)
    {
        void Check(string parameter,double value)
        {
            if(cameraParameters.FirstOrDefault(p=>p.Name==parameter) is{}info&&info.Validate(value) is{}error)
                throw new InvalidOperationException(error);
        }
        Check("ExposureTime",settings.ExposureTimeUs);Check("Gain",settings.Gain);
        if(settings.Gamma is{}gamma)Check("Gamma",gamma);
        if(settings.BlackLevel is{}black)Check("BlackLevel",black);
        if(settings.Roi is{}roi)
        {
            Check("Width",roi.Width);Check("Height",roi.Height);
            Check("OffsetX",roi.OffsetX);Check("OffsetY",roi.OffsetY);
        }
    }

    private void RefreshCameraParameters()
    {
        try{cameraParameters=Camera.DescribeParameters();}
        catch(Exception){cameraParameters=[];}
        try
        {
            var info=Camera.ReadInfo();
            CameraInfo=$"{info.Model} · {info.Serial} · {info.PixelFormat} · {info.SensorWidth}×{info.SensorHeight}"+
                (info.FrameRate is{}fps?$" · {fps:0.#} fps":"")+
                (info.TemperatureCelsius is{}temp?$" · {temp:0.#} °C":"");
        }
        catch(Exception){CameraInfo="Không đọc được thông tin camera.";}
        RefreshCameraCapabilities();
    }
    private void RefreshCameraCapabilities()
    {
        OnPropertyChanged(nameof(ExposureRange));OnPropertyChanged(nameof(GainRange));
        OnPropertyChanged(nameof(GammaRange));OnPropertyChanged(nameof(BlackLevelRange));
        OnPropertyChanged(nameof(SensorRange));
        OnPropertyChanged(nameof(CanEditGamma));OnPropertyChanged(nameof(CanEditBlackLevel));
        OnPropertyChanged(nameof(CanEditSensorRoi));OnPropertyChanged(nameof(CanEditStrobe));
    }

    /// <summary>Reads the trigger form. Invalid combinations are rejected here, before RUN starts.</summary>
    public TriggerSettings BuildTriggerSettings()
    {
        var camera=TriggerKind switch
        {
            TriggerKind.CameraLine=>new CameraTrigger(CameraTriggerSource.Line,Whole(TriggerLine,"Trigger line"),
                TriggerRisingEdge,Number(TriggerDelay,"Trigger delay"),Number(TriggerDebouncer,"Trigger debouncer")),
            TriggerKind.Plc=>new CameraTrigger(CameraTriggerSource.Software),
            _=>CameraTrigger.FreeRun
        };
        var settings=new TriggerSettings(TriggerKind,TriggerMapping,camera,Whole(TriggerRepeatBlock,"Chặn trigger lặp"));
        if(settings.Validate() is{}error)throw new InvalidOperationException(error);
        return settings;
    }

    private static double Number(string text,string label)=>
        double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)
            ?value:throw new InvalidOperationException($"{label} không hợp lệ. Dùng dấu chấm thập phân.");
    private static int Whole(string text,string label)=>
        int.TryParse(text,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var value)
            ?value:throw new InvalidOperationException($"{label} phải là số nguyên.");
    [RelayCommand]private async Task AcquisitionAsync()
    {
        if(!CanToggleAcquisition)return;
        if(Acquiring){await StopAcquisitionAsync();return;}
        await GuardAsync(StartAcquisitionAsync);
    }

    private async Task StartAcquisitionAsync()
    {
        await Task.Run(()=>
        {
            Camera.Start();
        });
        {
            acquisition=new();Acquiring=true;CameraState=CameraUiState.Acquiring;
            SourceStatus="CAMERA LIVE";CameraStatus="ĐANG ACQUISITION · CHỜ FRAME";
            Message="ACQUISITION · Đã bắt đầu nhận ảnh camera.";
            var token=acquisition.Token;
            Diagnostics.BeginRun();OnPropertyChanged(nameof(AcquisitionSummary));
            Log.Write("acquisition","started",new Dictionary<string,object?>{["device"]=connectedDevice?.Name});
            acquisitionTask=Task.Run(()=>RunAcquisitionAsync(token));
        }
    }
    /// <summary>
    /// Keeps live frames flowing across a lost camera. Each failure faults any cycle in progress and then
    /// retries with a bounded backoff; the product that was interrupted is never silently continued.
    /// </summary>
    private async Task RunAcquisitionAsync(CancellationToken token)
    {
        var attempt=0;
        try
        {
            while(!token.IsCancellationRequested)
            {
                try{await GrabLoopAsync(token);break;}
                catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
                catch(Exception ex)
                {
                    Diagnostics.Failed(ex.Message);
                    Log.Write("acquisition","lost",new Dictionary<string,object?>{["error"]=ex.Message,["attempt"]=attempt+1});
                    await dispatcher.InvokeAsync(()=>OnAcquisitionLost(ex.Message));
                    if(attempt>=ReconnectAttempts)
                    {
                        await dispatcher.InvokeAsync(()=>OnAcquisitionFailed(ex.Message));
                        break;
                    }
                    var delay=ReconnectDelays[Math.Min(attempt,ReconnectDelays.Length-1)];
                    attempt++;
                    try{await Task.Delay(delay,token);}catch(OperationCanceledException){break;}
                    if(token.IsCancellationRequested)break;
                    try
                    {
                        Reopen();
                        attempt=0;Diagnostics.Reconnected();
                        Log.Write("acquisition","reconnected",new Dictionary<string,object?>{["device"]=connectedDevice?.Name});
                        await dispatcher.InvokeAsync(OnAcquisitionResumed);
                    }
                    catch(Exception retry)
                    {
                        Diagnostics.ReconnectFailed(retry.Message);
                        Log.Write("acquisition","reconnect-failed",new Dictionary<string,object?>{["error"]=retry.Message});
                        await dispatcher.InvokeAsync(()=>CameraStatus=$"ĐANG KẾT NỐI LẠI · {retry.Message}");
                    }
                }
            }
        }
        finally
        {
            try{Camera.Stop();}catch{/* Next connection reports native state. */}
            Log.Write("acquisition","stopped",new Dictionary<string,object?>
            {
                ["frames"]=Diagnostics.Snapshot().Frames,["reconnects"]=Diagnostics.Snapshot().Reconnects
            });
        }
    }

    private async Task GrabLoopAsync(CancellationToken token)
    {
        var lastReceived=MonotonicClock.Now;
        while(!token.IsCancellationRequested)
        {
            ImageFrame frame;
            try{frame=Camera.Grab(300);lastReceived=MonotonicClock.Now;Diagnostics.Frame();}
            catch(TimeoutException)
            {
                Diagnostics.Timeout();
                // A triggered camera is silent by design; only free-run silence means the link is gone.
                if(!cameraTrigger.IsTriggered&&MonotonicClock.MillisecondsSince(lastReceived)>3000)
                    throw new TimeoutException("Camera không có frame mới trong 3 giây.");
                continue;
            }
            var bitmap=ImageFiles.Bitmap(frame);
            var arrived=MonotonicClock.Now;
            await dispatcher.InvokeAsync(()=>
            {
                if(token.IsCancellationRequested)return;
                latest=frame;latestTimestamp=arrived;LiveImage=bitmap;
                CameraStatus=$"ĐANG ACQUISITION · {frame.Width} × {frame.Height}";
                if(CameraState==CameraUiState.Reconnecting)CameraState=CameraUiState.Acquiring;
                RefreshGrabState();
                // In triggered acquisition the arriving frame is the trigger event.
                if(cameraTrigger.IsTriggered&&activeTrigger is CameraLineTriggerSource line)line.Fire(null,"camera-line");
            });
            await Task.Delay(15,token);
        }
    }

    private void Reopen()
    {
        var device=connectedDevice??throw new InvalidOperationException("Không còn thông tin camera để kết nối lại.");
        try{Camera.Stop();}catch{/* The device is already gone; reopening is what matters. */}
        try{Camera.Close();}catch{}
        Camera.Open(device);
        if(appliedSettings is{}settings)Camera.ApplySettings(settings);
        if(cameraTrigger.IsTriggered)Camera.ConfigureTrigger(cameraTrigger);
        Camera.Start();
    }

    private void OnAcquisitionLost(string error)
    {
        latest=null;CameraState=CameraUiState.Reconnecting;
        CameraStatus="MẤT KẾT NỐI CAMERA · ĐANG THỬ LẠI";
        Message=$"ACQUISITION · {error} Đang thử kết nối lại.";
        // An interrupted product must never be completed with a frame from after the outage.
        if(Running&&Session.Fault("Mất kết nối camera giữa chu kỳ."))
        {
            Result1.Reset(runtimeRecipe!.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);
            RunStatus="MẤT KẾT NỐI CAMERA";
            Message="RUN · Sản phẩm đang kiểm bị hủy vì mất kết nối camera. Kiểm tra lại từ đầu 1.";
        }
        RefreshGrabState();RefreshState();OnPropertyChanged(nameof(AcquisitionSummary));
    }

    private void OnAcquisitionResumed()
    {
        CameraState=CameraUiState.Acquiring;CameraStatus="ĐÃ KẾT NỐI LẠI · ĐANG ACQUISITION";
        Message="ACQUISITION · Đã kết nối lại camera.";
        if(Running&&runtimeRecipe!=null)
        {
            Session.Begin(runtimeRecipe);
            Result1.Reset(runtimeRecipe.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);
            waitingSince=DateTimeOffset.UtcNow;RunStatus="CHỜ ĐẦU 1";
        }
        RefreshState();OnPropertyChanged(nameof(AcquisitionSummary));
    }

    private void OnAcquisitionFailed(string error)
    {
        Acquiring=false;latest=null;CameraState=CameraUiState.Error;
        CameraStatus="LỖI ACQUISITION";Message=$"ACQUISITION · {error}";
        if(Running){Session.Stop();Running=false;RunStatus="ERROR";}
        RefreshState();OnPropertyChanged(nameof(AcquisitionSummary));
    }

    public async Task StopAcquisitionAsync()
    {
        acquisition?.Cancel();
        if(acquisitionTask!=null)await acquisitionTask;
        acquisition?.Dispose();acquisition=null;acquisitionTask=null;Acquiring=false;latest=null;
        if(CameraConnected)
        {
            CameraState=CameraUiState.Connected;SourceStatus="CAMERA CONNECTED";
            CameraStatus=SelectedCamera==null?"ĐÃ DỪNG ACQUISITION":$"ĐÃ DỪNG ACQUISITION · {SelectedCamera.Name}";
            Message="ACQUISITION · Đã dừng nhận ảnh. Camera vẫn đang kết nối.";
        }
    }
    [RelayCommand]private async Task DisconnectAsync()
    {
        if(!CanDisconnectCamera)return;
        StopRun();await Task.Run(Camera.Close);CameraConnected=false;SourceStatus="OFFLINE";
        cameraParameters=[];CameraInfo="Chưa kết nối camera.";
        RefreshCameraCapabilities();
        CameraState=Cameras.Count>0?CameraUiState.Found:CameraUiState.NotFound;
        CameraStatus=Cameras.Count>0?"ĐÃ NGẮT CAMERA · SẴN SÀNG KẾT NỐI LẠI":"ĐÃ NGẮT CAMERA";
        Message="ACQUISITION · Đã ngắt kết nối camera.";
    }
    public async Task ShutdownAsync(){StopRun();try{await DisarmTriggerAsync();}catch{/* Shutdown continues. */}await StopAcquisitionAsync();await Task.Run(()=>{Camera.Dispose();Ocr.Dispose();});}
    private static string? ChooseImage()
    {
        var dialog=new OpenFileDialog{Filter="Images|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff",CheckFileExists=true};
        return dialog.ShowDialog()==true?dialog.FileName:null;
    }
    private void Guard(Action action){try{action();}catch(Exception ex){Message=ex.Message;}}
    private async Task GuardAsync(Func<Task> action)
    {
        if(Busy)return;Busy=true;
        try{await action();}catch(Exception ex){Message=ex.Message;}finally{Busy=false;RefreshState();}
    }
}
