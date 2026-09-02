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
using WireMarkerInspection.Desktop.Security;
using WireMarkerInspection.Controls.Localization;

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
public sealed record PlcTransportOption(PlcTransport Value,string Label);
public sealed record PlcSerialProtocolOption(PlcSerialProtocol Value,string Label);
public sealed record PlcStopBitsOption(PlcSerialStopBits Value,string Label);
public enum CameraUiState { Idle, Simulator, Finding, NotFound, Found, Connected, Acquiring, Reconnecting, Error }
public enum PlcConnectionState { Disconnected, Connecting, Connected, Error }
public partial class MainViewModel : ObservableObject
{
    public FileRecipeStore Store{get;}
    public FileSettingsStore Settings{get;}
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
    public EndResultViewModel PreviousResult1{get;}=new(1);
    public EndResultViewModel PreviousResult2{get;}=new(2);
    public ObservableCollection<RecipeRow> Models{get;}=[];
    public ICollectionView ModelsView{get;}
    public ObservableCollection<CameraDevice> Cameras{get;}=[];
    public static CameraDevice SimulatorCamera{get;}=new("simulator","Simulator","offline-files",true);
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
    private PlcVerdictOutputWriter? plcVerdictWriter;
    private IPlcLink? plcLink;
    private PlcSettings? connectedPlcSettings;
    private readonly Func<PlcSettings,IPlcLink> plcFactory;
    private MachineSettings machineSettings=MachineSettings.Default;
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
    private CameraInspectionIo? runtimeIo;
    private readonly TimeSpan cameraSearchTimeout;
    private Task<IReadOnlyList<CameraDevice>>? cameraDiscoveryTask;
    private readonly bool simulatorEnabled;
    private bool runUsesSimulator;
    public IAuthenticationService Authentication{get;}
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
    [ObservableProperty]private string cameraStatus=AppLocalizer.Text("CameraNotConnected");
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
    [ObservableProperty]private string cameraInfo=AppLocalizer.Text("CameraNotConnectedPeriod");
    [ObservableProperty]private bool showAdvancedCamera;
    [ObservableProperty]private TriggerKind triggerKind=TriggerKind.Manual;
    [ObservableProperty]private TriggerMapping triggerMapping=TriggerMapping.Shared;
    [ObservableProperty]private string triggerLine="0";
    [ObservableProperty]private bool triggerRisingEdge=true;
    [ObservableProperty]private string triggerDelay="0";
    [ObservableProperty]private string triggerDebouncer="1000";
    [ObservableProperty]private string triggerRepeatBlock="250";
    [ObservableProperty]private string triggerStatus=AppLocalizer.Text("ManualTriggerStatus");
    [ObservableProperty]private string lastTrigger=AppLocalizer.Text("NoTriggerYet");
    [ObservableProperty]private string plcVendor="delta-dvp";
    [ObservableProperty]private PlcTransport plcTransport=PlcTransport.Com;
    [ObservableProperty]private string plcHost="192.168.1.5";
    [ObservableProperty]private string plcPort="502";
    [ObservableProperty]private string plcSerialPort="COM11";
    [ObservableProperty]private string plcBaudRate="9600";
    [ObservableProperty]private PlcSerialProtocol plcSerialProtocol=PlcSerialProtocol.ModbusAscii;
    [ObservableProperty]private string plcDataBits="7";
    [ObservableProperty]private PlcSerialParity plcParity=PlcSerialParity.Even;
    [ObservableProperty]private PlcSerialStopBits plcStopBits=PlcSerialStopBits.One;
    [ObservableProperty]private string plcUnitId="1";
    [ObservableProperty]private string plcPollMs="20";
    [ObservableProperty]private string plcTimeoutMs="1000";
    [ObservableProperty]private string plcTriggerAddress="X0";
    [ObservableProperty]private string plcEnd1Address="X0";
    [ObservableProperty]private string plcEnd2Address="X1";
    [ObservableProperty]private bool okOutputEnabled;
    [ObservableProperty]private PlcOutputMode okOutputMode=PlcOutputMode.Bit;
    [ObservableProperty]private string okOutputDevice="M";
    [ObservableProperty]private string okOutputIndex="1";
    [ObservableProperty]private string okOutputValue="1";
    [ObservableProperty]private string okOutputPulseMs="50";
    [ObservableProperty]private bool ngOutputEnabled;
    [ObservableProperty]private PlcOutputMode ngOutputMode=PlcOutputMode.Bit;
    [ObservableProperty]private string ngOutputDevice="M";
    [ObservableProperty]private string ngOutputIndex="2";
    [ObservableProperty]private string ngOutputValue="2";
    [ObservableProperty]private string ngOutputPulseMs="50";
    [ObservableProperty]private string plcStatus=AppLocalizer.Text("PlcNotConfigured");
    [ObservableProperty]private PlcConnectionState plcConnectionState=PlcConnectionState.Disconnected;
    [ObservableProperty]private string message=AppLocalizer.Text("SelectModelOrAdd");
    [ObservableProperty]private string runStatus=AppLocalizer.Text("NotRunning");
    [ObservableProperty]private bool hasPreviousResult;
    [ObservableProperty]private bool showPreviousResults;
    [ObservableProperty]private string previousResultLabel=AppLocalizer.Text("NoPreviousResult");
    [ObservableProperty]private string lastProductVerdict="—";
    [ObservableProperty]private string ocrStatus="";
    [ObservableProperty]private AccessLevel currentAccessLevel=AccessLevel.Operator;
    public bool CanEdit=>!Running&&!Busy;
    public bool IsAdmin=>CurrentAccessLevel==AccessLevel.Admin;
    public bool CanOperateAcquisition=>CanEdit;
    public bool CanSelectModel=>CanEdit;
    public bool CanCreateModel=>IsAdmin&&CanEdit;
    public bool CanConfigureModel=>IsAdmin&&CanEdit&&modelSetupActive;
    public bool CanSaveRecipe=>CanConfigureModel&&Dirty;
    public bool CanManageSelectedModel=>IsAdmin&&CanEdit&&SelectedModel!=null;
    public bool CanSearchCamera=>CanOperateAcquisition&&!FindingCamera&&!CameraConnected&&!Acquiring;
    public bool CanSelectCamera=>CanOperateAcquisition&&!FindingCamera&&!CameraConnected&&Cameras.Count>0;
    public bool IsSimulatorSelected=>SelectedCamera?.IsSimulation==true;
    public bool IsSimulatorRun=>runUsesSimulator&&Running;
    public bool CanConnectCamera=>CanSelectCamera&&SelectedCamera is {IsSimulation:false};
    public bool CanDisconnectCamera=>CanOperateAcquisition&&CameraConnected&&!Acquiring;
    public bool CanToggleAcquisition=>CanOperateAcquisition&&CameraConnected&&!IsSimulatorSelected;
    public bool CanEditCameraParameters=>IsAdmin&&CanEdit&&CameraConnected&&!Acquiring&&!IsSimulatorSelected;
    public bool HasLiveFrame=>Acquiring&&latest!=null&&DateTimeOffset.UtcNow-latest.CapturedAt<=LiveFrameMaxAge;
    public string ExposureRange=>Range("ExposureTime");
    public string GainRange=>Range("Gain");
    public string GammaRange=>Range("Gamma");
    public string BlackLevelRange=>Range("BlackLevel");
    public string SensorRange=>cameraParameters.Count==0?"":$"Width {Range("Width")} · Height {Range("Height")}";
    private string Range(string parameter)=>
        cameraParameters.FirstOrDefault(p=>p.Name==parameter) is{}info
            ?$"{info.Minimum:0.###} – {info.Maximum:0.###} {info.Unit}".TrimEnd()
            :cameraParameters.Count==0?"":AppLocalizer.Text("CameraUnsupported");
    private bool Supports(string parameter)=>cameraParameters.Any(p=>p.Name==parameter);
    public bool CanEditGamma=>CanEditCameraParameters&&Supports("Gamma");
    public bool CanEditBlackLevel=>CanEditCameraParameters&&Supports("BlackLevel");
    public bool CanEditSensorRoi=>CanEditCameraParameters&&Supports("Width")&&Supports("Height");
    // Strobe nodes only become readable after a line is selected, so availability cannot be probed up front.
    public bool CanEditStrobe=>CanEditCameraParameters;
    public bool CanGrabReference=>CanConfigureModel&&HasLiveFrame;
    public string AcquisitionActionLabel=>AppLocalizer.Text(Acquiring?"StopAcquisition":"StartAcquisition");
    public bool CanCapture=>Running&&!Busy&&Session.State is InspectionState.WaitingEnd1 or InspectionState.WaitingEnd2;
    public bool CanLoadRuntime=>CanCapture&&IsSimulatorRun;
    public bool CanCaptureFromCamera=>CanCapture&&!IsSimulatorRun;
    public string CaptureLabel=>AppLocalizer.Format("CaptureEndFormat",Session.NextEnd+1);
    public string ModelCount=>AppLocalizer.Format("ModelCountFormat",Models.Count);
    public LocalizedOption<TriggerKind>[] TriggerKindOptions{get;}=
    [
        new(TriggerKind.Manual,"TriggerManual"),
        new(TriggerKind.CameraLine,"TriggerCameraLine"),
        new(TriggerKind.Plc,"TriggerPlc")
    ];
    public IReadOnlyList<string> PlcVendors{get;}=PlcAddressMaps.Vendors;
    public PlcTransportOption[] PlcTransports{get;}=
    [
        new(PlcTransport.EthernetIp,"Ethernet IP"),
        new(PlcTransport.Com,"COM")
    ];
    public PlcSerialProtocolOption[] PlcSerialProtocols{get;}=
    [
        new(PlcSerialProtocol.ModbusAscii,"Modbus ASCII"),
        new(PlcSerialProtocol.ModbusRtu,"Modbus RTU")
    ];
    public PlcSerialParity[] PlcParities{get;}=[PlcSerialParity.None,PlcSerialParity.Even,PlcSerialParity.Odd];
    public PlcStopBitsOption[] PlcStopBitOptions{get;}=[new(PlcSerialStopBits.One,"1"),new(PlcSerialStopBits.Two,"2")];
    public ObservableCollection<string> PlcSerialPorts{get;}=[];
    public bool PlcSelected=>TriggerKind==TriggerKind.Plc;
    public bool PlcUsesPerEnd=>TriggerMapping==TriggerMapping.PerEnd;
    public bool PlcUsesNetwork=>PlcTransport==PlcTransport.EthernetIp;
    public bool PlcUsesSerial=>PlcTransport==PlcTransport.Com;
    public bool PlcConnected=>PlcConnectionState==PlcConnectionState.Connected&&plcLink?.IsConnected==true;
    public bool CanConfigurePlc=>CanEditTrigger&&PlcConnectionState is not PlcConnectionState.Connecting and not PlcConnectionState.Connected;
    public bool CanConnectPlc=>CanEditTrigger&&PlcConnectionState is PlcConnectionState.Disconnected or PlcConnectionState.Error;
    public bool CanDisconnectPlc=>CanEditTrigger&&PlcConnected;
    public bool CanEditRecipeIo=>CanConfigureModel&&!PlcConnected;
    public LocalizedOption<PlcOutputMode>[] PlcOutputModeOptions{get;}=
    [
        new(PlcOutputMode.Bit,"OutputBit"),
        new(PlcOutputMode.Register,"OutputRegister")
    ];
    public string[] BitOutputDevices{get;}=["M","Y"];
    public string[] RegisterOutputDevices{get;}=["D"];
    public IReadOnlyList<string> OkOutputDevices=>OkOutputMode==PlcOutputMode.Bit?BitOutputDevices:RegisterOutputDevices;
    public IReadOnlyList<string> NgOutputDevices=>NgOutputMode==PlcOutputMode.Bit?BitOutputDevices:RegisterOutputDevices;
    public bool OkUsesBit=>OkOutputMode==PlcOutputMode.Bit;
    public bool NgUsesBit=>NgOutputMode==PlcOutputMode.Bit;
    private PlcOutputs plcOutputs=new();
    public LocalizedOption<TriggerMapping>[] TriggerMappingOptions{get;}=
    [
        new(TriggerMapping.Shared,"MappingShared"),
        new(TriggerMapping.PerEnd,"MappingPerEnd")
    ];
    public EndResultViewModel DisplayResult1=>ShowPreviousResults?PreviousResult1:Result1;
    public EndResultViewModel DisplayResult2=>ShowPreviousResults?PreviousResult2:Result2;
    public string RunCameraStatus=>runUsesSimulator?AppLocalizer.Text("SimulatorRunCamera"):AppLocalizer.Text(CameraConnected
        ?Acquiring?"CameraAcquiring":"CameraConnected"
        :"CameraOffline");
    public string RunPlcStatus=>runUsesSimulator?AppLocalizer.Text("SimulatorRunPlc"):runtimeIo?.UsesPlc==true
        ?AppLocalizer.Text(PlcConnected?"PlcConnected":"PlcOffline")
        :AppLocalizer.Text("PlcUnused");
    public bool CanEditTrigger=>IsAdmin&&CanEdit&&!Running;
    public bool CanRetakeEnd=>Running&&!Busy&&Session.State==InspectionState.WaitingEnd2;
    public string CycleTimingText
    {
        get
        {
            var (count,average,p95,max)=CycleTimes.Summary();
            return count==0?AppLocalizer.Text("NoTimingData")
                :AppLocalizer.Format("CycleTimingFormat",CycleTimes.Last.ToString("0"),average.ToString("0"),p95.ToString("0"),max.ToString("0"),count);
        }
    }
    public string AcquisitionSummary
    {
        get
        {
            var snapshot=Diagnostics.Snapshot();
            if(snapshot.Frames==0&&snapshot.Uptime==TimeSpan.Zero)return AppLocalizer.Text("AcquisitionNotStarted");
            var text=AppLocalizer.Format("AcquisitionMetricsFormat",snapshot.Frames,snapshot.Timeouts,snapshot.Reconnects);
            if(snapshot.FramesPerSecond is{}fps)text+=$" · {fps:0.0} fps";
            if(snapshot.ReconnectFailures>0)text+=$" · {AppLocalizer.Format("ReconnectFailuresFormat",snapshot.ReconnectFailures)}";
            return snapshot.LastError is{}error?$"{text} · {AppLocalizer.Format("LastErrorFormat",error)}":text;
        }
    }
    public string CycleLabel=>Session.CycleId==Guid.Empty?"—":Session.CycleId.ToString("N")[..12].ToUpperInvariant();
    public bool IsCameraOnline=>CameraConnected&&CameraState is CameraUiState.Connected or CameraUiState.Acquiring;
    public bool HasSelectedModel=>SelectedModel!=null;
    public string SelectedModelCode=>SelectedModel?.Code??AppLocalizer.Text("NoModelSelected");
    public string SelectedModelName=>SelectedModel?.Name??AppLocalizer.Text("SelectModelInstruction");
    public string SelectedModelRevision=>SelectedModel is null?string.Empty:AppLocalizer.Format("RevisionFormat",SelectedModel.Recipe.Revision);
    public string ActiveModel=>runtimeRecipe is null?AppLocalizer.Text("NotSelected"):$"{runtimeRecipe.ModelCode} / v{runtimeRecipe.Revision}";
    public string ActiveModelName=>runtimeRecipe?.Name??AppLocalizer.Text("SelectModelInstruction");
    public string AccessRoleLabel=>AppLocalizer.Text(IsAdmin?"RoleAdmin":"RoleOperator");
    public string AccessSummary=>AppLocalizer.Text(IsAdmin?"AdminLoggedIn":"OperatorMode");
    public AppLanguage CurrentLanguage
    {
        get=>AppLocalizer.CurrentLanguage;
        set{if(AppLocalizer.CurrentLanguage!=value)AppLocalizer.CurrentLanguage=value;OnPropertyChanged();}
    }
    public AppLanguage[] LanguageOptions{get;}=[AppLanguage.Vietnamese,AppLanguage.English,AppLanguage.Korean];
    public Func<string,bool>? Confirm{get;set;}

    public bool TryLogin(string username,string password)
    {
        if(!Authentication.Authenticate(username,password))return false;
        CurrentAccessLevel=AccessLevel.Admin;Message=AppLocalizer.Text("AdminLoggedIn");return true;
    }

    [RelayCommand]private async Task LogoutAsync()
    {
        if(!IsAdmin||Busy||Running)return;
        if(Dirty&&Confirm?.Invoke(AppLocalizer.Text("DiscardChangesForLogout"))!=true)return;
        Busy=true;
        try
        {
            if(PlcConnected)await DisconnectPlcCoreAsync();
            CurrentAccessLevel=AccessLevel.Operator;
            if(Dirty)
            {
                if(saved!=null)Load(saved);else ClearModelSetup();
            }
            Message=AppLocalizer.Text("OperatorMode");
        }
        catch(Exception exception){Message=exception.Message;}
        finally{Busy=false;RefreshState();}
    }
    public MainViewModel(string dataRoot,ICamera? camera=null,bool autoDiscoverCameraOnLoad=true,TimeSpan? cameraSearchTimeout=null,
        Func<PlcSettings,IPlcLink>? plcFactory=null,IAuthenticationService? authentication=null,bool? enableSimulator=null)
    {
        simulatorEnabled=enableSimulator??camera==null;
        Camera=camera??new HikrobotMvsCamera();Authentication=authentication??new LocalAuthenticationService();
        this.plcFactory=plcFactory??(settings=>new ModbusPlcLink(settings,PlcAddressMaps.For(settings.Vendor)));
        // The view model is created on the UI thread; the acquisition loop marshals frames back through this dispatcher.
        dispatcher=System.Windows.Application.Current?.Dispatcher??System.Windows.Threading.Dispatcher.CurrentDispatcher;
        AutoDiscoverCameraOnLoad=autoDiscoverCameraOnLoad;
        this.cameraSearchTimeout=cameraSearchTimeout??TimeSpan.FromSeconds(5);
        if(this.cameraSearchTimeout<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(cameraSearchTimeout));
        Store=new(dataRoot);Ocr=new(Path.Combine(AppContext.BaseDirectory,"assets","ocr"));
        Log=new FileDiagnosticsLog(dataRoot);
        Settings=new(dataRoot);
        Session=new(Ocr,new FileResultStore(dataRoot));
        ModelsView=CollectionViewSource.GetDefaultView(Models);
        ModelsView.Filter=o=>o is RecipeRow r&&(r.Code.Contains(Search,StringComparison.OrdinalIgnoreCase)||r.Name.Contains(Search,StringComparison.OrdinalIgnoreCase));
        End1.Changed+=(_,_)=>{if(!loading)Dirty=true;};End2.Changed+=(_,_)=>{if(!loading)Dirty=true;};
        AppLocalizer.LanguageChanged+=OnLanguageChanged;
        if(simulatorEnabled){Cameras.Add(SimulatorCamera);SelectedCamera=SimulatorCamera;}
        Reload();RefreshOcr();LoadMachineSettings();ReloadPlcPorts();
    }

    private void LoadMachineSettings()
    {
        var machine=Settings.Load();machineSettings=machine;
        loadingCamera=true;
        try
        {
            TriggerKind=machine.Trigger.Kind;TriggerMapping=machine.Trigger.Mapping;
            var camera=machine.Trigger.CameraTrigger;
            TriggerLine=camera.Line.ToString();TriggerRisingEdge=camera.RisingEdge;
            TriggerDelay=camera.DelayUs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            TriggerDebouncer=camera.DebouncerUs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            TriggerRepeatBlock=machine.Trigger.RepeatBlockMs.ToString();
            var plc=machine.Plc;
            PlcVendor=plc.Vendor;PlcTransport=plc.Transport;
            PlcHost=plc.Host;PlcPort=plc.Port.ToString();PlcSerialPort=plc.SerialPort;
            PlcBaudRate=plc.BaudRate.ToString();PlcSerialProtocol=plc.SerialProtocol;PlcDataBits=plc.DataBits.ToString();
            PlcParity=plc.Parity;PlcStopBits=plc.StopBits;PlcUnitId=plc.UnitId.ToString();
            PlcPollMs=plc.PollMs.ToString();PlcTimeoutMs=plc.TimeoutMs.ToString();
            PlcTriggerAddress=plc.TriggerAddress.Length>0?plc.TriggerAddress:PlcTriggerAddress;
            PlcEnd1Address=plc.End1Address.Length>0?plc.End1Address:PlcEnd1Address;
            PlcEnd2Address=plc.End2Address.Length>0?plc.End2Address:PlcEnd2Address;
            plcOutputs=plc.Writes;
        }
        finally{loadingCamera=false;}
        if(Settings.LoadError is{}error)Message=AppLocalizer.Format("SettingsLoadFailedFormat",error);
    }

    /// <summary>Builds the machine's physical PLC connection; legacy logical fields are preserved only for migration.</summary>
    public PlcSettings BuildPlcSettings()=>new(
        false,PlcVendor,PlcTransport,PlcHost.Trim(),Whole(PlcPort,"PLC port"),PlcSerialPort.Trim(),
        Whole(PlcBaudRate,"Baud rate"),checked((byte)Whole(PlcUnitId,"Unit ID")),Whole(PlcPollMs,AppLocalizer.Text("PlcPollMs")),
        PlcTriggerAddress.Trim(),PlcEnd1Address.Trim(),PlcEnd2Address.Trim(),plcOutputs,PlcSerialProtocol,
        Whole(PlcDataBits,"Data bits"),PlcParity,PlcStopBits,Whole(PlcTimeoutMs,"Timeout PLC"));

    public CameraInspectionIo BuildRecipeIo()
    {
        var trigger=new RecipeTriggerProfile((RecipeTriggerKind)(int)TriggerKind,(RecipeTriggerMapping)(int)TriggerMapping,
            Whole(TriggerLine,"Trigger line"),TriggerRisingEdge,Number(TriggerDelay,"Trigger delay"),
            Number(TriggerDebouncer,AppLocalizer.Text("DebouncerUs")),Whole(TriggerRepeatBlock,AppLocalizer.Text("RepeatBlockMs")),
            Whole(PlcPollMs,AppLocalizer.Text("PlcPollMs")),PlcTriggerAddress.Trim(),PlcEnd1Address.Trim(),PlcEnd2Address.Trim());
        var outputs=new VerdictOutputProfile(
            new PlcOutputAction(OkOutputEnabled,OkOutputMode,OkOutputDevice,Whole(OkOutputIndex,"OK output index"),
                checked((short)Whole(OkOutputValue,"OK register value")),Whole(OkOutputPulseMs,"OK pulse")),
            new PlcOutputAction(NgOutputEnabled,NgOutputMode,NgOutputDevice,Whole(NgOutputIndex,"NG output index"),
                checked((short)Whole(NgOutputValue,"NG register value")),Whole(NgOutputPulseMs,"NG pulse")));
        var io=new CameraInspectionIo(trigger,outputs);
        if(io.Validate() is{}error)throw new InvalidOperationException(error);
        return io;
    }

    private void ShowRecipeIo(CameraInspectionIo io)
    {
        var trigger=io.TriggerProfile;
        TriggerKind=(TriggerKind)(int)trigger.Kind;TriggerMapping=(TriggerMapping)(int)trigger.Mapping;
        TriggerLine=trigger.CameraLine.ToString();TriggerRisingEdge=trigger.RisingEdge;
        TriggerDelay=trigger.DelayUs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TriggerDebouncer=trigger.DebouncerUs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TriggerRepeatBlock=trigger.RepeatBlockMs.ToString();PlcPollMs=trigger.PollMs.ToString();
        PlcTriggerAddress=trigger.SharedAddress;PlcEnd1Address=trigger.End1Address;PlcEnd2Address=trigger.End2Address;
        ShowOutput(io.VerdictOutputs.OkAction,true);ShowOutput(io.VerdictOutputs.NgAction,false);
    }

    private void ShowOutput(PlcOutputAction action,bool ok)
    {
        if(ok)
        {
            OkOutputEnabled=action.Enabled;OkOutputMode=action.Mode;OkOutputDevice=action.Device;
            OkOutputIndex=action.Index.ToString();OkOutputValue=action.RegisterValue.ToString();OkOutputPulseMs=action.PulseMs.ToString();
        }
        else
        {
            NgOutputEnabled=action.Enabled;NgOutputMode=action.Mode;NgOutputDevice=action.Device;
            NgOutputIndex=action.Index.ToString();NgOutputValue=action.RegisterValue.ToString();NgOutputPulseMs=action.PulseMs.ToString();
        }
    }

    private CameraInspectionIo LegacyRecipeIo()
    {
        var trigger=machineSettings.Trigger;var plc=machineSettings.Plc;var outputs=plc.Writes;
        PlcOutputAction Legacy(string address,int fallbackIndex)
        {
            if(string.IsNullOrWhiteSpace(address))return new PlcOutputAction(Index:fallbackIndex,RegisterValue:(short)fallbackIndex);
            var text=address.Trim().ToUpperInvariant();var device=text[..1];
            var index=int.TryParse(text[1..],out var parsed)?parsed:fallbackIndex;
            return new PlcOutputAction(true,PlcOutputMode.Bit,device,index,(short)fallbackIndex,
                outputs.ClearAfterMs>0?outputs.ClearAfterMs:50);
        }
        return new CameraInspectionIo(
            new((RecipeTriggerKind)(int)trigger.Kind,(RecipeTriggerMapping)(int)trigger.Mapping,
                trigger.CameraTrigger.Line,trigger.CameraTrigger.RisingEdge,trigger.CameraTrigger.DelayUs,
                trigger.CameraTrigger.DebouncerUs,trigger.RepeatBlockMs,plc.PollMs,
                string.IsNullOrWhiteSpace(plc.TriggerAddress)?"X0":plc.TriggerAddress,
                string.IsNullOrWhiteSpace(plc.End1Address)?"X0":plc.End1Address,
                string.IsNullOrWhiteSpace(plc.End2Address)?"X1":plc.End2Address),
            new(Legacy(outputs.OkBit,1),Legacy(outputs.NgBit,2)));
    }

    /// <summary>Persists only the machine-level physical PLC configuration.</summary>
    [RelayCommand]private void SaveMachineSettings()=>Guard(()=>
    {
        if(!CanConfigurePlc)return;
        var plc=BuildPlcSettings();
        if(plc.ValidateConnection() is{}error)throw new InvalidOperationException(error);
        // Only the physical link belongs to the machine. Legacy logical fields remain readable until
        // every old recipe has been saved as schema v2, but new trigger/output edits live in the recipe.
        var legacy=machineSettings.Plc;
        plc=plc with
        {
            Enabled=legacy.Enabled,TriggerAddress=legacy.TriggerAddress,End1Address=legacy.End1Address,
            End2Address=legacy.End2Address,Outputs=legacy.Outputs
        };
        machineSettings=machineSettings with {Plc=plc};Settings.Save(machineSettings);
        Message=AppLocalizer.Text("PlcConnectionSaved");
    });

    /// <summary>Opens the selected physical link and verifies communication by reading one configured input.</summary>
    [RelayCommand]private async Task ConnectPlcAsync()
    {
        if(!CanConnectPlc)return;
        Busy=true;PlcConnectionState=PlcConnectionState.Connecting;
        PlcStatus=AppLocalizer.Format("PlcConnectingFormat",PlcUsesSerial?"COM":"ETHERNET IP");
        try
        {
            var settings=BuildPlcSettings() with {Enabled=true};
            var io=modelSetupActive?BuildRecipeIo():LegacyRecipeIo();
            var address=io.TriggerProfile.Mapping==RecipeTriggerMapping.Shared
                ?io.TriggerProfile.SharedAddress:io.TriggerProfile.End1Address;
            await ConnectPlcCoreAsync(settings,address);
            Message=AppLocalizer.Text("PlcConnectedRead");
        }
        catch(Exception ex)
        {
            PlcStatus=AppLocalizer.Format("PlcConnectionErrorFormat",ex.Message);Message=PlcStatus;
        }
        finally{Busy=false;RefreshState();}
    }

    private async Task ConnectPlcCoreAsync(PlcSettings settings,string probeAddress)
    {
        if(settings.ValidateConnection() is{}error)throw new InvalidOperationException(error);
        if(PlcConnected&&connectedPlcSettings is{}current&&SamePhysicalConnection(settings,current))return;
        await DisconnectPlcCoreAsync();
        IPlcLink? candidate=null;
        PlcConnectionState=PlcConnectionState.Connecting;
        PlcStatus=AppLocalizer.Format("PlcConnectingFormat",settings.Describe());
        try
        {
            candidate=plcFactory(settings);await candidate.ConnectAsync(CancellationToken.None);
            var address=string.IsNullOrWhiteSpace(probeAddress)?"X0":probeAddress;
            var value=await candidate.ReadBitAsync(address,CancellationToken.None);
            plcLink=candidate;candidate=null;connectedPlcSettings=settings;PlcConnectionState=PlcConnectionState.Connected;
            PlcStatus=AppLocalizer.Format("PlcConnectedDetailFormat",settings.Describe(),address,value?"ON":"OFF");
        }
        catch
        {
            if(candidate!=null)try{await candidate.DisposeAsync();}catch{/* Keep the original connection error. */}
            plcLink=null;connectedPlcSettings=null;PlcConnectionState=PlcConnectionState.Error;
            throw;
        }
    }

    private async Task DisconnectPlcCoreAsync()
    {
        try{if(plcLink!=null)await plcLink.DisposeAsync();}
        finally
        {
            plcLink=null;connectedPlcSettings=null;PlcConnectionState=PlcConnectionState.Disconnected;
        }
    }

    [RelayCommand]private async Task DisconnectPlcAsync()
    {
        if(!CanDisconnectPlc)return;
        Busy=true;
        try
        {
            await DisconnectPlcCoreAsync();
            PlcStatus=AppLocalizer.Text("PlcDisconnectedStatus");Message=AppLocalizer.Text("PlcDisconnectedMessage");
        }
        catch(Exception ex)
        {
            plcLink=null;connectedPlcSettings=null;PlcConnectionState=PlcConnectionState.Error;
            PlcStatus=AppLocalizer.Format("PlcDisconnectErrorFormat",ex.Message);Message=PlcStatus;
        }
        finally{Busy=false;RefreshState();}
    }

    [RelayCommand]private void RefreshPlcPorts()
    {
        if(!CanConfigurePlc)return;
        ReloadPlcPorts();
    }
    private void ReloadPlcPorts()
    {
        var selected=PlcSerialPort;
        PlcSerialPorts.Clear();
        foreach(var port in ModbusPlcLink.AvailableSerialPorts())PlcSerialPorts.Add(port);
        if(selected.Length>0&&!PlcSerialPorts.Contains(selected,StringComparer.OrdinalIgnoreCase))PlcSerialPorts.Add(selected);
        PlcSerialPort=selected;
    }
    partial void OnSearchChanged(string value)=>ModelsView.Refresh();
    partial void OnTriggerKindChanged(TriggerKind value)=>OnPropertyChanged(nameof(PlcSelected));
    partial void OnTriggerMappingChanged(TriggerMapping value)=>OnPropertyChanged(nameof(PlcUsesPerEnd));
    partial void OnShowPreviousResultsChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayResult1));OnPropertyChanged(nameof(DisplayResult2));
    }
    partial void OnOkOutputModeChanged(PlcOutputMode value)
    {
        OkOutputDevice=value==PlcOutputMode.Bit&&BitOutputDevices.Contains(OkOutputDevice)?OkOutputDevice:
            value==PlcOutputMode.Register?"D":"M";
        OnPropertyChanged(nameof(OkOutputDevices));OnPropertyChanged(nameof(OkUsesBit));
    }
    partial void OnNgOutputModeChanged(PlcOutputMode value)
    {
        NgOutputDevice=value==PlcOutputMode.Bit&&BitOutputDevices.Contains(NgOutputDevice)?NgOutputDevice:
            value==PlcOutputMode.Register?"D":"M";
        OnPropertyChanged(nameof(NgOutputDevices));OnPropertyChanged(nameof(NgUsesBit));
    }
    partial void OnPlcTransportChanged(PlcTransport value)
    {
        OnPropertyChanged(nameof(PlcUsesNetwork));OnPropertyChanged(nameof(PlcUsesSerial));
    }
    partial void OnPlcConnectionStateChanged(PlcConnectionState value)=>RefreshPlcState();
    partial void OnModelCodeChanged(string value){if(!loading)Dirty=true;}
    partial void OnModelNameChanged(string value){if(!loading)Dirty=true;}
    partial void OnDirtyChanged(bool value)=>OnPropertyChanged(nameof(CanSaveRecipe));
    partial void OnBusyChanged(bool value)=>RefreshState();
    partial void OnRunningChanged(bool value)=>RefreshState();
    partial void OnCurrentAccessLevelChanged(AccessLevel value)=>RefreshState();
    partial void OnSelectedCameraChanged(CameraDevice? value)
    {
        OnPropertyChanged(nameof(IsSimulatorSelected));
        if(!CameraConnected&&!FindingCamera)
        {
            if(value?.IsSimulation==true)
            {
                CameraState=CameraUiState.Simulator;CameraStatus=AppLocalizer.Text("SimulatorReady");
                CameraInfo=AppLocalizer.Text("SimulatorNoParameters");
            }
            else if(value!=null)
            {
                CameraState=CameraUiState.Found;CameraStatus=AppLocalizer.Text("CameraReadyToConnect");
                CameraInfo=AppLocalizer.Text("CameraNotConnectedPeriod");
            }
        }
        RefreshCameraState();
    }
    partial void OnCameraConnectedChanged(bool value)=>RefreshCameraState();
    partial void OnCameraStateChanged(CameraUiState value)=>OnPropertyChanged(nameof(IsCameraOnline));
    partial void OnAcquiringChanged(bool value)=>RefreshCameraState();
    partial void OnFindingCameraChanged(bool value)=>RefreshCameraState();
    private static readonly HashSet<string> CameraDraftProperties=
    [
        nameof(Exposure),nameof(Gain),nameof(GammaEnabled),nameof(Gamma),nameof(BlackLevelEnabled),nameof(BlackLevel),
        nameof(SensorRoiEnabled),nameof(SensorOffsetX),nameof(SensorOffsetY),nameof(SensorWidth),nameof(SensorHeight),
        nameof(StrobeEnabled),nameof(StrobeLine),nameof(StrobeDuration),nameof(StrobeDelay)
    ];
    private static readonly HashSet<string> RecipeIoDraftProperties=
    [
        nameof(TriggerKind),nameof(TriggerMapping),nameof(TriggerLine),nameof(TriggerRisingEdge),nameof(TriggerDelay),
        nameof(TriggerDebouncer),nameof(TriggerRepeatBlock),nameof(PlcPollMs),nameof(PlcTriggerAddress),
        nameof(PlcEnd1Address),nameof(PlcEnd2Address),nameof(OkOutputEnabled),nameof(OkOutputMode),
        nameof(OkOutputDevice),nameof(OkOutputIndex),nameof(OkOutputValue),nameof(OkOutputPulseMs),
        nameof(NgOutputEnabled),nameof(NgOutputMode),nameof(NgOutputDevice),nameof(NgOutputIndex),
        nameof(NgOutputValue),nameof(NgOutputPulseMs)
    ];
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Camera settings are stored with the recipe, so changing them is an unsaved recipe change.
        if(IsAdmin&&!loading&&modelSetupActive&&e.PropertyName is{}name&&
           ((!loadingCamera&&CameraDraftProperties.Contains(name))||RecipeIoDraftProperties.Contains(name)))
            Dirty=true;
    }
    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanEdit));OnPropertyChanged(nameof(IsAdmin));OnPropertyChanged(nameof(CanOperateAcquisition));OnPropertyChanged(nameof(CanSelectModel));
        OnPropertyChanged(nameof(CanCreateModel));OnPropertyChanged(nameof(CanConfigureModel));OnPropertyChanged(nameof(CanSaveRecipe));OnPropertyChanged(nameof(CanManageSelectedModel));
        OnPropertyChanged(nameof(AccessRoleLabel));OnPropertyChanged(nameof(AccessSummary));
        OnPropertyChanged(nameof(CanCapture));OnPropertyChanged(nameof(CanLoadRuntime));OnPropertyChanged(nameof(CanCaptureFromCamera));
        OnPropertyChanged(nameof(CanEditTrigger));OnPropertyChanged(nameof(CanEditRecipeIo));OnPropertyChanged(nameof(CanRetakeEnd));
        RefreshPlcState();
        RetakeEndCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CaptureLabel));OnPropertyChanged(nameof(CycleLabel));OnPropertyChanged(nameof(ActiveModel));
        OnPropertyChanged(nameof(IsSimulatorRun));OnPropertyChanged(nameof(RunCameraStatus));OnPropertyChanged(nameof(RunPlcStatus));
        RefreshCameraState();
    }

    private void OnLanguageChanged(object? sender,EventArgs args)
    {
        RefreshLocalizedState();
        RefreshOcr();
        OnPropertyChanged(nameof(CurrentLanguage));OnPropertyChanged(nameof(AcquisitionActionLabel));
        foreach(var option in TriggerKindOptions)option.RefreshLabel();
        foreach(var option in TriggerMappingOptions)option.RefreshLabel();
        foreach(var option in PlcOutputModeOptions)option.RefreshLabel();
        OnPropertyChanged(nameof(ModelCount));OnPropertyChanged(nameof(RunCameraStatus));OnPropertyChanged(nameof(RunPlcStatus));
        OnPropertyChanged(nameof(SelectedModelCode));OnPropertyChanged(nameof(SelectedModelName));OnPropertyChanged(nameof(SelectedModelRevision));
        OnPropertyChanged(nameof(ActiveModel));OnPropertyChanged(nameof(ActiveModelName));OnPropertyChanged(nameof(AccessRoleLabel));OnPropertyChanged(nameof(AccessSummary));
        End1.RefreshLanguage();End2.RefreshLanguage();Result1.RefreshLanguage();Result2.RefreshLanguage();PreviousResult1.RefreshLanguage();PreviousResult2.RefreshLanguage();
    }
    private void RefreshLocalizedState()
    {
        CameraStatus=CameraState switch
        {
            CameraUiState.Finding=>AppLocalizer.Text("FindingCamera"),
            CameraUiState.Simulator=>AppLocalizer.Text("SimulatorReady"),
            CameraUiState.NotFound=>AppLocalizer.Text("CameraNotFound"),
            CameraUiState.Found=>AppLocalizer.Format("CameraFoundFormat",Cameras.Count),
            CameraUiState.Connected=>AppLocalizer.Format("CameraConnectedDetailFormat",SelectedCamera?.Name??"—"),
            CameraUiState.Acquiring=>AppLocalizer.Text("CameraAcquiringStatus"),
            CameraUiState.Reconnecting=>AppLocalizer.Text("CameraReconnecting"),
            CameraUiState.Idle=>AppLocalizer.Text("CameraNotConnected"),
            _=>CameraStatus
        };
        if(!CameraConnected)CameraInfo=AppLocalizer.Text(IsSimulatorSelected?"SimulatorNoParameters":"CameraNotConnectedPeriod");
        if(PlcConnectionState==PlcConnectionState.Disconnected)PlcStatus=AppLocalizer.Text("PlcNotConfigured");
        if(TriggerKind==TriggerKind.Manual)TriggerStatus=AppLocalizer.Text("ManualTriggerStatus");
        if(Session.CycleId==Guid.Empty)LastTrigger=AppLocalizer.Text("NoTriggerYet");
        if(!HasPreviousResult)PreviousResultLabel=AppLocalizer.Text("NoPreviousResult");
        Message=CameraState switch
        {
            CameraUiState.Finding=>AppLocalizer.Text("AcquisitionFindingMessage"),
            CameraUiState.Simulator=>AppLocalizer.Text("SimulatorReadyMessage"),
            CameraUiState.NotFound=>AppLocalizer.Text("AcquisitionNotFoundMessage"),
            CameraUiState.Found=>AppLocalizer.Text("AcquisitionFoundMessage"),
            CameraUiState.Connected=>AppLocalizer.Text("CameraConnectSuccess"),
            CameraUiState.Acquiring=>AppLocalizer.Text("AcquisitionStarted"),
            CameraUiState.Reconnecting=>AppLocalizer.Text("CameraReconnecting"),
            CameraUiState.Idle when SelectedModel==null=>AppLocalizer.Text("SelectModelOrAdd"),
            _=>Message
        };
        RunStatus=Session.State switch
        {
            InspectionState.WaitingEnd1=>AppLocalizer.Text("WaitingEnd1"),
            InspectionState.WaitingEnd2=>AppLocalizer.Text("WaitingEnd2"),
            InspectionState.ProcessingEnd1 or InspectionState.ProcessingEnd2=>AppLocalizer.Text("ProcessingOcr"),
            InspectionState.Stopped=>AppLocalizer.Text("NotRunning"),
            _=>RunStatus
        };
        OnPropertyChanged(nameof(CycleTimingText));OnPropertyChanged(nameof(AcquisitionSummary));OnPropertyChanged(nameof(CaptureLabel));
    }
    private void RefreshPlcState()
    {
        OnPropertyChanged(nameof(PlcConnected));OnPropertyChanged(nameof(CanConfigurePlc));
        OnPropertyChanged(nameof(CanConnectPlc));OnPropertyChanged(nameof(CanDisconnectPlc));OnPropertyChanged(nameof(CanEditRecipeIo));
        ConnectPlcCommand.NotifyCanExecuteChanged();DisconnectPlcCommand.NotifyCanExecuteChanged();
    }
    private void RefreshCameraState()
    {
        OnPropertyChanged(nameof(IsCameraOnline));
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
        OnPropertyChanged(nameof(CanManageSelectedModel));OnPropertyChanged(nameof(HasSelectedModel));
        OnPropertyChanged(nameof(SelectedModelCode));OnPropertyChanged(nameof(SelectedModelName));OnPropertyChanged(nameof(SelectedModelRevision));
        if(loading)return;
        if(Dirty&&Confirm?.Invoke(AppLocalizer.Text(newValue==null?"DiscardAndDeselect":"DiscardAndOpen"))==false)
        {
            RestoreSelection(oldValue,newValue);
            Message=AppLocalizer.Text("KeepUnsaved");
            return;
        }
        if(newValue==null)
        {
            ClearModelSetup();
            Message=AppLocalizer.Text("SelectModelOrAdd");
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
        modelSetupActive=value;OnPropertyChanged(nameof(CanConfigureModel));OnPropertyChanged(nameof(CanSaveRecipe));
        OnPropertyChanged(nameof(CanEditRecipeIo));RefreshGrabState();
    }
    private void ClearModelSetup()
    {
        loading=true;
        try
        {
            saved=null;draftId=Guid.NewGuid();ModelCode="";ModelName="";End1.Clear();End2.Clear();
            ShowRecipeIo(new CameraInspectionIo());Dirty=false;SetModelSetupActive(false);
        }
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
        try
        {
            saved=recipe;draftId=recipe.Id;ModelCode=recipe.ModelCode;ModelName=recipe.Name;
            End1.Load(recipe.Ends[0],p1);End2.Load(recipe.Ends[1],p2);
            ShowRecipeIo(recipe.Io??LegacyRecipeIo());Dirty=false;
        }
        finally {loading=false;}
        Message=AppLocalizer.Format("RecipeLoadedFormat",recipe.ModelCode,recipe.Revision);
        if(recipe.Camera is not{}settings)return;
        ShowCameraSettings(settings);
        // Operator model selection is read-only. RUN applies the frozen recipe automatically.
        if(!IsAdmin)return;
        if(!CameraConnected)return;
        if(Acquiring){Message+=AppLocalizer.Text("StopAcquisitionToApply");return;}
        try{Camera.ApplySettings(settings);appliedSettings=settings;RefreshCameraParameters();Message+=AppLocalizer.Text("CameraSettingsAppliedSuffix");}
        catch(Exception ex){Message+=AppLocalizer.Format("CameraSettingsApplyFailedFormat",ex.Message);}
    }
    private void Reload()
    {
        Models.Clear();foreach(var recipe in Store.LoadAll())Models.Add(new(recipe,Store));
        OnPropertyChanged(nameof(ModelCount));
        if(Store.LoadErrors.Count>0)Message=AppLocalizer.Format("RecipeLoadErrorsFormat",string.Join(" | ",Store.LoadErrors));
    }
    [RelayCommand]private void RefreshOcr()=>OcrStatus=Ocr.AvailabilityError??AppLocalizer.Text("OcrAssetsReady");
    public string? ValidateModelIdentity(ModelIdentity identity,Guid? existingId=null)
    {
        var code=identity.Code.Trim();var name=identity.Name.Trim();
        if(code.Length==0)return AppLocalizer.Text("ModelCodeRequired");
        if(name.Length==0)return AppLocalizer.Text("ModelNameRequired");
        if(code.Length>64)return AppLocalizer.Text("ModelCodeTooLong");
        if(name.Length>128)return AppLocalizer.Text("ModelNameTooLong");
        if(Models.Any(row=>row.Recipe.Id!=existingId&&string.Equals(row.Code,code,StringComparison.OrdinalIgnoreCase)))
            return AppLocalizer.Format("DuplicateModelCodeFormat",code);
        return null;
    }
    [RelayCommand]private void NewModel(ModelIdentity identity)=>Guard(()=>
    {
        if(!CanCreateModel || (Dirty&&Confirm?.Invoke(AppLocalizer.Text("DiscardForNewModel"))==false))return;
        if(ValidateModelIdentity(identity) is { } error)throw new InvalidOperationException(error);
        loading=true;
        try
        {
            SelectedModel=null;saved=null;draftId=Guid.NewGuid();
            ModelCode=identity.Code.Trim();ModelName=identity.Name.Trim();
            End1.Clear();End2.Clear();ShowRecipeIo(new CameraInspectionIo());Dirty=true;SetModelSetupActive(true);
        }
        finally{loading=false;}
        Message=AppLocalizer.Format("NewModelFormat",ModelCode);
    });
    [RelayCommand]private void EditModel(ModelIdentity identity)=>Guard(()=>
    {
        if(!CanManageSelectedModel||SelectedModel==null)return;
        var target=SelectedModel.Recipe;
        if(Dirty && Confirm?.Invoke(AppLocalizer.Text("DiscardAndOpen"))==false)return;
        if(ValidateModelIdentity(identity,target.Id) is { } error)throw new InvalidOperationException(error);
        Load(target);
        loading=true;
        try
        {
            ModelCode=identity.Code.Trim();ModelName=identity.Name.Trim();
            Dirty=ModelCode!=target.ModelCode||ModelName!=target.Name;
        }
        finally{loading=false;}
        Message=Dirty?AppLocalizer.Format("EditingModelFormat",target.ModelCode):AppLocalizer.Format("RecipeLoadedFormat",target.ModelCode,target.Revision);
    });
    [RelayCommand]private void SaveRecipe()=>Guard(()=>
    {
        if(!CanSaveRecipe)return;
        if(!End1.Applied||!End2.Applied)throw new InvalidOperationException(AppLocalizer.Text("ApplyBothEndsBeforeSave"));
        var draft=new Recipe(draftId,ModelCode.Trim(),ModelName.Trim(),saved?.Revision??0,[End1.Spec(),End2.Spec()],
            DateTimeOffset.UtcNow,2,BuildCameraSettings(),BuildRecipeIo());
        var updated=Store.Save(draft,[ImageFiles.Png(End1.Image!),ImageFiles.Png(End2.Image!)]);
        loading=true;try{Reload();SelectedModel=Models.First(r=>r.Recipe.Id==updated.Id);}finally{loading=false;}
        Load(updated);Message=AppLocalizer.Format("RecipeSavedFormat",updated.ModelCode,updated.Revision);
    });
    [RelayCommand]private void DeleteModel()=>Guard(()=>
    {
        if(!CanManageSelectedModel||SelectedModel==null)return;
        var target=SelectedModel.Recipe;
        if(Confirm?.Invoke(AppLocalizer.Format("DeleteModelConfirmFormat",target.ModelCode))!=true)return;
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
                ?AppLocalizer.Text("LiveFrameStale")
                :AppLocalizer.Text("LiveFrameMissing"));
        // Each end owns its own copy; ends must never share a captured buffer.
        var editor=Editor(end);
        editor.SetFrame(live with {Bgr=[..live.Bgr],Id=Guid.NewGuid()});
        Message=AppLocalizer.Format("GrabbedFrameFormat",editor.Number,live.Width,live.Height,live.Source);
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
    [RelayCommand]private async Task SettingAsync()
    {
        if(Busy)return;
        Busy=true;
        try
        {
            if(Running)await StopRuntimeCoreAsync();
            RunPage=false;Message=AppLocalizer.Text("SettingEntered");
        }
        catch(Exception ex){RunPage=false;Message=AppLocalizer.Format("SettingExitErrorFormat",ex.Message);}
        finally{Busy=false;RefreshState();}
    }
    [RelayCommand]private async Task StartRunAsync()
    {
        if(Busy||Running)return;
        RunPage=true;RefreshOcr();
        Busy=true;var started=false;
        try
        {
            if(saved==null||Dirty)throw new InvalidOperationException(AppLocalizer.Text("SaveBeforeRun"));
            if(Ocr.AvailabilityError is{}error)throw new InvalidOperationException(error);
            runtimeRecipe=saved.Copy();
            var io=runtimeRecipe.Io??LegacyRecipeIo();runtimeIo=io;
            runUsesSimulator=IsSimulatorSelected;
            if(runUsesSimulator)
            {
                if(Acquiring)await StopAcquisitionAsync();
                if(PlcConnected)await DisconnectPlcCoreAsync();
                await DisarmTriggerAsync();
                router=new(new TriggerSettings(TriggerKind.Manual,TriggerMapping.Shared));
                TriggerStatus=AppLocalizer.Text("SimulatorTriggerStatus");
                PlcStatus=AppLocalizer.Text("SimulatorRunPlc");
                CameraStatus=AppLocalizer.Text("SimulatorReady");SourceStatus="SIMULATOR";
            }
            else
            {
                if(io.Validate() is{}ioError)throw new InvalidOperationException(ioError);
                await EnsureRunCameraAsync(runtimeRecipe);
                if(!Acquiring)await StartAcquisitionAsync();
                if(io.UsesPlc)
                {
                    var plc=BuildPlcSettings() with
                    {
                        Enabled=true,PollMs=io.TriggerProfile.PollMs,
                        TriggerAddress=io.TriggerProfile.SharedAddress,End1Address=io.TriggerProfile.End1Address,
                        End2Address=io.TriggerProfile.End2Address,Outputs=new PlcOutputs()
                    };
                    var probe=io.TriggerProfile.Kind==RecipeTriggerKind.Plc
                        ?io.TriggerProfile.Mapping==RecipeTriggerMapping.Shared?io.TriggerProfile.SharedAddress:io.TriggerProfile.End1Address
                        :"X0";
                    await ConnectPlcCoreAsync(plc,probe);
                    plcVerdictWriter=new(plcLink!,PlcAddressMaps.For(plc.Vendor),io.VerdictOutputs,Log);
                    if(plcVerdictWriter.Validate() is{}outputError)throw new InvalidOperationException(outputError);
                    await plcVerdictWriter.ClearBitsAsync(CancellationToken.None);
                }
                var settings=BuildTriggerSettings();
                await ArmTriggerAsync(settings);
            }
            Session.Begin(runtimeRecipe);Running=true;started=true;
            HasPreviousResult=false;ShowPreviousResults=false;PreviousResultLabel=AppLocalizer.Text("NoPreviousResult");LastProductVerdict="—";
            Result1.Reset(runtimeRecipe.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);waitingSince=DateTimeOffset.UtcNow;
            RunStatus=AppLocalizer.Text("WaitingEnd1");
        }
        catch(Exception ex)
        {
            Message=AppLocalizer.Format("RunStartFailedFormat",ex.Message);
            await RollbackRunStartAsync();
        }
        finally
        {
            Busy=false;
            if(started)Message=AppLocalizer.Format("RunStartedFormat",TriggerStatus);
            RefreshState();
        }
    }
    [RelayCommand]private async Task StopRunAsync()
    {
        if(Busy)return;
        Busy=true;
        try{await StopRuntimeCoreAsync();Message=AppLocalizer.Text("RunStoppedMessage");}
        catch(Exception ex){Message=AppLocalizer.Format("RunStopErrorFormat",ex.Message);}
        finally{Busy=false;RefreshState();}
    }
    public void StopRun(){Session.Stop();Running=false;runUsesSimulator=false;RunStatus=AppLocalizer.Text("Stopped");RefreshState();}

    private async Task EnsureRunCameraAsync(Recipe recipe)
    {
        if(!CameraConnected)
        {
            if(SelectedCamera==null)
            {
                var devices=await Task.Run(Camera.Enumerate).WaitAsync(cameraSearchTimeout);
                Cameras.Clear();foreach(var device in devices)Cameras.Add(device);
                SelectedCamera=Cameras.FirstOrDefault();
            }
            var target=SelectedCamera??throw new InvalidOperationException(AppLocalizer.Text("RunCameraNotFound"));
            await Task.Run(()=>Camera.Open(target));connectedDevice=target;CameraConnected=true;
            CameraState=CameraUiState.Connected;SourceStatus=target.IsSimulation?"SIMULATION":"CAMERA CONNECTED";
            CameraStatus=AppLocalizer.Format("CameraConnectedDetailFormat",target.Name);RefreshCameraParameters();
        }
        if(Acquiring)await StopAcquisitionAsync();
        if(recipe.Camera is{}settings)
        {
            ValidateAgainstCamera(settings);await Task.Run(()=>Camera.ApplySettings(settings));appliedSettings=settings;
        }
        await Task.Run(()=>Camera.ConfigureTrigger(CameraTrigger.FreeRun));cameraTrigger=CameraTrigger.FreeRun;
    }

    private async Task RollbackRunStartAsync()
    {
        Session.Stop();Running=false;RunStatus="ERROR";
        try{await DisarmTriggerAsync();}catch(Exception ex){Log.Write("run","rollback-trigger-failed",new Dictionary<string,object?>{{"error",ex.Message}});}
        try{if(Acquiring)await StopAcquisitionAsync();}catch(Exception ex){Log.Write("run","rollback-camera-failed",new Dictionary<string,object?>{{"error",ex.Message}});}
        try{await DisconnectPlcCoreAsync();}catch(Exception ex){Log.Write("run","rollback-plc-failed",new Dictionary<string,object?>{{"error",ex.Message}});}
        plcVerdictWriter=null;runUsesSimulator=false;
    }

    private async Task StopRuntimeCoreAsync()
    {
        Session.Stop();Running=false;RunStatus=AppLocalizer.Text("Stopped");
        Exception? failure=null;
        try{await DisarmTriggerAsync();}catch(Exception ex){failure=ex;}
        try{if(Acquiring)await StopAcquisitionAsync();}catch(Exception ex){failure??=ex;}
        try{await DisconnectPlcCoreAsync();}catch(Exception ex){failure??=ex;}
        plcVerdictWriter=null;runUsesSimulator=false;
        PlcStatus=AppLocalizer.Text("PlcDisconnectedStatus");
        if(failure!=null)throw failure;
    }

    /// <summary>
    /// Switching between free-run and a triggered source is an acquisition lifecycle change: MVS will not
    /// accept a trigger-source change while grabbing, so acquisition is stopped and restarted around it.
    /// </summary>
    public async Task ArmTriggerAsync(TriggerSettings settings)
    {
        await DisarmTriggerAsync();
        router=new(settings);
        ITriggerSource source;
        if(settings.Kind==TriggerKind.CameraLine)source=new CameraLineTriggerSource(settings.CameraTrigger);
        else if(settings.Kind==TriggerKind.Plc)
        {
            var io=runtimeIo??runtimeRecipe?.Io??(modelSetupActive?BuildRecipeIo():LegacyRecipeIo());
            var plc=BuildPlcSettings() with
            {
                Enabled=true,PollMs=io.TriggerProfile.PollMs,TriggerAddress=io.TriggerProfile.SharedAddress,
                End1Address=io.TriggerProfile.End1Address,End2Address=io.TriggerProfile.End2Address,
                Outputs=new PlcOutputs()
            };
            if(plc.Validate(settings.Mapping) is{}invalid)throw new InvalidOperationException(invalid);
            if(!PlcConnected||plcLink==null||connectedPlcSettings==null)
                throw new InvalidOperationException(AppLocalizer.Text("ConnectPlcBeforeRun"));
            if(!SamePhysicalConnection(plc,connectedPlcSettings))
                throw new InvalidOperationException(AppLocalizer.Text("PlcConfigChanged"));
            source=new PlcTriggerSource(plcLink,plc,settings.Mapping,Log,manageLinkLifecycle:false);
        }
        else source=manualTrigger;
        source.Fired+=OnTriggerFired;activeTrigger=source;
        if(settings.CameraTrigger.IsTriggered)
        {
            if(!CameraConnected)throw new InvalidOperationException(AppLocalizer.Text("HardwareTriggerNeedsCamera"));
            var resume=Acquiring;if(resume)await StopAcquisitionAsync();
            await Task.Run(()=>Camera.ConfigureTrigger(settings.CameraTrigger));cameraTrigger=settings.CameraTrigger;
            if(resume)await StartAcquisitionAsync();
            await source.StartAsync(CancellationToken.None);
        }
        else await source.StartAsync(CancellationToken.None);
        TriggerStatus=source.Status;
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
        if(cameraTrigger.IsTriggered&&CameraConnected)
            await SwitchAcquisitionAsync(CameraTrigger.FreeRun,async()=>
            {
                await source.StopAsync();
                await Task.Run(()=>Camera.ConfigureTrigger(CameraTrigger.FreeRun));
            });
        else await source.StopAsync();
        if(plcLink!=null&&!plcLink.IsConnected)
        {
            try{await plcLink.DisposeAsync();}catch{/* The link is already unusable. */}
            plcLink=null;connectedPlcSettings=null;PlcConnectionState=PlcConnectionState.Error;
            PlcStatus=AppLocalizer.Text("PlcConnectionLost");
        }
        else if(PlcConnected&&connectedPlcSettings!=null)PlcStatus=AppLocalizer.Format("PlcConnectedSettingsFormat",connectedPlcSettings.Describe());
        if(!ReferenceEquals(source,manualTrigger))await source.DisposeAsync();
        TriggerStatus=manualTrigger.Status;
        Log.Write("trigger","disarmed",null);
    }

    private static bool SamePhysicalConnection(PlcSettings left,PlcSettings right) =>
        left.Vendor==right.Vendor&&left.Transport==right.Transport&&left.Host==right.Host&&left.Port==right.Port&&
        left.SerialPort==right.SerialPort&&left.BaudRate==right.BaudRate&&left.UnitId==right.UnitId&&
        left.SerialProtocol==right.SerialProtocol&&left.DataBits==right.DataBits&&left.Parity==right.Parity&&
        left.StopBits==right.StopBits&&left.TimeoutMs==right.TimeoutMs;

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
        _=CaptureTriggeredAsync();
    }

    private async Task CaptureTriggeredAsync()
    {
        if(!CanCapture)return;
        await GuardAsync(async()=>
        {
            var frame=await RequestFrameAsync();
            await InspectAsync(frame,MonotonicClock.MillisecondsSince(latestTimestamp));
        });
    }

    /// <summary>
    /// Returns the frame this trigger is about. A camera-line pulse already delivered one; a PLC signal
    /// has to ask the camera for it, and waiting for a genuinely new frame is what keeps the image tied
    /// to the signal that requested it.
    /// </summary>
    private async Task<ImageFrame> RequestFrameAsync()
    {
        if(cameraTrigger.Source!=CameraTriggerSource.Software)
            return latest??throw new InvalidOperationException(AppLocalizer.Text("NoNewCameraFrame"));
        var previous=latest?.Id;
        await Task.Run(Camera.ExecuteSoftwareTrigger);
        var started=MonotonicClock.Now;
        while(MonotonicClock.MillisecondsSince(started)<3000)
        {
            if(latest is{}frame&&frame.Id!=previous)return frame;
            await Task.Delay(5);
        }
        throw new TimeoutException(AppLocalizer.Text("PlcTriggeredFrameTimeout"));
    }

    /// <summary>Drops a bad first image so the operator can shoot it again without losing the product.</summary>
    [RelayCommand(CanExecute=nameof(CanRetakeEnd))]private void RetakeEnd()=>Guard(()=>
    {
        if(!CanRetakeEnd||runtimeRecipe==null)return;
        if(!Session.RetakeLastEnd())return;
        Result1.Reset(runtimeRecipe.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);
        router.Reset();waitingSince=DateTimeOffset.UtcNow;RunStatus=AppLocalizer.Text("WaitingEnd1");
        Message=AppLocalizer.Text("RetakeEnd1");RefreshState();
    });
    [RelayCommand]private void TogglePreviousResults()
    {
        if(!HasPreviousResult)return;
        ShowPreviousResults=!ShowPreviousResults;
    }
    [RelayCommand]private async Task LoadRuntimeAsync()
    {
        if(!CanLoadRuntime)return;
        var file=ChooseImage();if(file==null)return;
        await GuardAsync(()=>InspectAsync(ImageFiles.Load(file)));
    }
    /// <summary>Manual capture is the manual trigger source, so it follows the same routing rules.</summary>
    [RelayCommand]private void ManualTrigger()
    {
        if(!Running){Message=AppLocalizer.Text("RunNotStarted");return;}
        if(!CanCaptureFromCamera)return;
        manualTrigger.Fire(TriggerMapping==TriggerMapping.PerEnd?Session.NextEnd:null,AppLocalizer.Text("ManualAction"));
    }
    [RelayCommand]private async Task CaptureRuntimeAsync()
    {
        if(!CanCaptureFromCamera)return;
        await GuardAsync(async()=>
        {
            if(!Acquiring||latest==null||latest.CapturedAt<waitingSince||DateTimeOffset.UtcNow-latest.CapturedAt>TimeSpan.FromSeconds(2))
                throw new InvalidOperationException(AppLocalizer.Text("NoNewCameraFrame"));
            if(latest.Source=="SIMULATION")throw new InvalidOperationException(AppLocalizer.Text("SimulationNotAllowed"));
            await InspectAsync(latest,MonotonicClock.MillisecondsSince(latestTimestamp));
        });
    }
    private async Task InspectAsync(ImageFrame frame,double frameAgeMs=0)
    {
        if(Session.State==InspectionState.WaitingEnd1&&HasPreviousResult)
        {
            Result1.Reset(runtimeRecipe!.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);ShowPreviousResults=false;
        }
        var end=Session.NextEnd;var target=end==0?Result1:Result2;target.Show(frame,null);
        RunStatus=AppLocalizer.Format("ProcessingEndFormat",end+1);
        try
        {
            var result=await Session.AcceptAsync(frame,frameAgeMs);
            if(result==null)return;
            target.Show(frame,result);waitingSince=DateTimeOffset.UtcNow;
            RecordTimings(result);
            if(Session.Result is{}product)
            {
                var outputOk=await ReportToPlcAsync(product);
                PreviousResult1.CopyFrom(Result1);PreviousResult2.CopyFrom(Result2);
                HasPreviousResult=true;ShowPreviousResults=true;
                LastProductVerdict=product.Verdict.ToString().ToUpperInvariant();
                PreviousResultLabel=AppLocalizer.Format("PreviousResultFormat",product.Verdict.ToString().ToUpperInvariant(),product.CycleId.ToString("N")[..12].ToUpperInvariant());
                if(!outputOk)
                {
                    var outputError=PlcStatus;
                    try{await StopRuntimeCoreAsync();}
                    catch(Exception ex){Log.Write("run","output-failure-cleanup",new Dictionary<string,object?>{["error"]=ex.Message});}
                    RunStatus=AppLocalizer.Text("PlcOutputError");PlcStatus=outputError;
                    Message=AppLocalizer.Format("PlcOutputStoppedFormat",outputError);
                    return;
                }
                Session.Begin(runtimeRecipe!);router.Reset();waitingSince=DateTimeOffset.UtcNow;
                RunStatus=AppLocalizer.Text("WaitingEnd1");
                Message=AppLocalizer.Format("CycleReadyFormat",product.Verdict.ToString().ToUpperInvariant());
            }
            else
            {
                RunStatus=AppLocalizer.Text("WaitingEnd2");Message=AppLocalizer.Text("End1Complete");
            }
        }
        catch {target.Status="ERROR";RunStatus="ERROR";throw;}
        finally{RefreshState();}
    }
    private async Task<bool> ReportToPlcAsync(ProductResult product)
    {
        try
        {
            if(plcVerdictWriter is{}writer)
            {
                await writer.ReportAsync(product.Verdict,CancellationToken.None);
                PlcStatus=writer.LastError??PlcStatus;
                return writer.LastError==null;
            }
            return true;
        }
        catch(Exception ex)
        {
            // A PLC write must never take down an inspection that already produced a verdict.
            PlcStatus=AppLocalizer.Format("PlcWriteErrorFormat",ex.Message);
            Log.Write("plc","report-failed",new Dictionary<string,object?>{["error"]=ex.Message});
            return false;
        }
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
        CameraStatus=AppLocalizer.Text("FindingCamera");
        Message=AppLocalizer.Text("AcquisitionFindingMessage");
        Cameras.Clear();SelectedCamera=null;
        if(simulatorEnabled)Cameras.Add(SimulatorCamera);
        RefreshCameraState();
        try
        {
            cameraDiscoveryTask??=Task.Run(Camera.Enumerate);
            _=cameraDiscoveryTask.ContinueWith(task=>{_ = task.Exception;},CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            var devices=await cameraDiscoveryTask.WaitAsync(cameraSearchTimeout);
            cameraDiscoveryTask=null;
            var physicalDevices=devices.Where(device=>!device.IsSimulation).ToArray();
            foreach(var device in physicalDevices)Cameras.Add(device);
            if(simulatorEnabled)
            {
                SelectedCamera=SimulatorCamera;CameraState=CameraUiState.Simulator;
                CameraStatus=physicalDevices.Length==0?AppLocalizer.Text("SimulatorReady"):AppLocalizer.Format("SimulatorReadyWithCamerasFormat",physicalDevices.Length);
                Message=AppLocalizer.Text("SimulatorReadyMessage");
            }
            else
            {
                SelectedCamera=Cameras.FirstOrDefault();
                if(Cameras.Count==0)
                {
                    CameraState=CameraUiState.NotFound;
                    CameraStatus=AppLocalizer.Text("CameraNotFound");
                    Message=AppLocalizer.Text("AcquisitionNotFoundMessage");
                }
                else
                {
                    CameraState=CameraUiState.Found;
                    CameraStatus=AppLocalizer.Format("CameraFoundFormat",Cameras.Count);
                    Message=AppLocalizer.Text("AcquisitionFoundMessage");
                }
            }
        }
        catch(TimeoutException)
        {
            if(simulatorEnabled)
            {
                SelectedCamera=SimulatorCamera;CameraState=CameraUiState.Simulator;
                CameraStatus=AppLocalizer.Text("SimulatorReady");Message=AppLocalizer.Text("SimulatorSearchTimeoutMessage");
            }
            else
            {
                CameraState=CameraUiState.NotFound;
                CameraStatus=AppLocalizer.Format("CameraSearchTimeoutFormat",cameraSearchTimeout.TotalSeconds.ToString("0.#"));
                Message=AppLocalizer.Text("CameraSearchTimeoutMessage");
            }
        }
        catch(Exception ex)
        {
            cameraDiscoveryTask=null;
            if(simulatorEnabled)
            {
                SelectedCamera=SimulatorCamera;CameraState=CameraUiState.Simulator;
                CameraStatus=AppLocalizer.Text("SimulatorReady");Message=AppLocalizer.Format("SimulatorSearchErrorFormat",ex.Message);
            }
            else
            {
                CameraState=CameraUiState.Error;CameraStatus=AppLocalizer.Text("CameraSearchError");
                Message=$"ACQUISITION · {ex.Message}";
            }
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
            CameraStatus=AppLocalizer.Format("CameraConnectedDetailFormat",target.Name);
            RefreshCameraParameters();
            Message=AppLocalizer.Text("CameraConnectSuccess");
        });
        if(!CameraConnected)
        {
            CameraState=CameraUiState.Error;CameraStatus=AppLocalizer.Text("CameraConnectFailed");
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
            Message=AppLocalizer.Text("CameraParametersApplied");
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
        Message=AppLocalizer.Text("CameraParametersRead");
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
        catch(Exception){CameraInfo=AppLocalizer.Text("CameraInfoUnavailable");}
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
            // A PLC bit asks the camera for a frame, so the camera waits on its software trigger.
            TriggerKind.Plc when CameraConnected=>new CameraTrigger(CameraTriggerSource.Software),
            _=>CameraTrigger.FreeRun
        };
        var settings=new TriggerSettings(TriggerKind,TriggerMapping,camera,Whole(TriggerRepeatBlock,AppLocalizer.Text("RepeatBlockMs")));
        if(settings.Validate() is{}error)throw new InvalidOperationException(error);
        return settings;
    }

    private static double Number(string text,string label)=>
        double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)
            ?value:throw new InvalidOperationException(AppLocalizer.Format("InvalidDecimalFormat",label));
    private static int Whole(string text,string label)=>
        int.TryParse(text,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var value)
            ?value:throw new InvalidOperationException(AppLocalizer.Format("IntegerRequiredFormat",label));
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
            SourceStatus="CAMERA LIVE";CameraStatus=AppLocalizer.Text("CameraAcquiringStatus");
            Message=AppLocalizer.Text("AcquisitionStarted");
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
                        await dispatcher.InvokeAsync(()=>CameraStatus=AppLocalizer.Format("ReconnectingDetailFormat",retry.Message));
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
                    throw new TimeoutException(AppLocalizer.Text("CameraFrameTimeout"));
                continue;
            }
            var bitmap=ImageFiles.Bitmap(frame);
            var arrived=MonotonicClock.Now;
            await dispatcher.InvokeAsync(()=>
            {
                if(token.IsCancellationRequested)return;
                latest=frame;latestTimestamp=arrived;LiveImage=bitmap;
                CameraStatus=AppLocalizer.Format("AcquisitionFrameFormat",frame.Width,frame.Height);
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
        var device=connectedDevice??throw new InvalidOperationException(AppLocalizer.Text("CameraReconnectInfoMissing"));
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
        CameraStatus=AppLocalizer.Text("CameraReconnecting");
        Message=AppLocalizer.Format("AcquisitionLostMessageFormat",error);
        // An interrupted product must never be completed with a frame from after the outage.
        if(Running&&Session.Fault(AppLocalizer.Text("CameraLostDuringCycle")))
        {
            Result1.Reset(runtimeRecipe!.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);
            RunStatus=AppLocalizer.Text("RunCameraLostStatus");
            Message=AppLocalizer.Text("RunCameraLost");
        }
        RefreshGrabState();RefreshState();OnPropertyChanged(nameof(AcquisitionSummary));
    }

    private void OnAcquisitionResumed()
    {
        CameraState=CameraUiState.Acquiring;CameraStatus=AppLocalizer.Text("CameraReconnectedStatus");
        Message=AppLocalizer.Text("CameraReconnectedMessage");
        if(Running&&runtimeRecipe!=null)
        {
            Session.Begin(runtimeRecipe);
            Result1.Reset(runtimeRecipe.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);
            waitingSince=DateTimeOffset.UtcNow;RunStatus=AppLocalizer.Text("WaitingEnd1");
        }
        RefreshState();OnPropertyChanged(nameof(AcquisitionSummary));
    }

    private void OnAcquisitionFailed(string error)
    {
        Acquiring=false;latest=null;CameraState=CameraUiState.Error;
        CameraStatus=AppLocalizer.Text("AcquisitionError");Message=$"ACQUISITION · {error}";
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
            CameraStatus=SelectedCamera==null?AppLocalizer.Text("AcquisitionStopped"):AppLocalizer.Format("AcquisitionStoppedFormat",SelectedCamera.Name);
            Message=AppLocalizer.Text("AcquisitionStoppedMessage");
        }
    }
    [RelayCommand]private async Task DisconnectAsync()
    {
        if(!CanDisconnectCamera)return;
        StopRun();await Task.Run(Camera.Close);CameraConnected=false;SourceStatus="OFFLINE";
        cameraParameters=[];CameraInfo=AppLocalizer.Text("CameraNotConnectedPeriod");
        RefreshCameraCapabilities();
        CameraState=Cameras.Count>0?CameraUiState.Found:CameraUiState.NotFound;
        CameraStatus=AppLocalizer.Text(Cameras.Count>0?"CameraDisconnectedReady":"CameraDisconnected");
        Message=AppLocalizer.Text("CameraDisconnectedMessage");
    }
    public async Task ShutdownAsync()
    {
        AppLocalizer.LanguageChanged-=OnLanguageChanged;
        StopRun();try{await DisarmTriggerAsync();}catch{/* Shutdown continues. */}
        if(plcLink!=null)try{await plcLink.DisposeAsync();}catch{/* Shutdown continues. */}
        plcLink=null;connectedPlcSettings=null;PlcConnectionState=PlcConnectionState.Disconnected;
        await StopAcquisitionAsync();await Task.Run(()=>{Camera.Dispose();Ocr.Dispose();});
    }
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
