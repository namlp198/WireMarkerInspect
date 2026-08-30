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
public enum CameraUiState { Idle, Finding, NotFound, Found, Connected, Acquiring, Error }
public partial class MainViewModel : ObservableObject
{
    public FileRecipeStore Store{get;}
    public NativeOcrEngine Ocr{get;}
    public ICamera Camera{get;}
    public bool AutoDiscoverCameraOnLoad{get;}
    public InspectionSession Session{get;}
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
    public bool CanGrabReference=>CanConfigureModel&&HasLiveFrame;
    public string AcquisitionActionLabel=>Acquiring?"Stop Acquisition":"Start Acquisition";
    public bool CanCapture=>Running&&!Busy&&Session.State is InspectionState.WaitingEnd1 or InspectionState.WaitingEnd2;
    public bool CanNext=>Running&&!Busy&&Session.State==InspectionState.Completed;
    public string CaptureLabel=>$"Nhận ảnh đầu {Session.NextEnd+1}";
    public string ModelCount=>$"{Models.Count} MODELS";
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
    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanEdit));OnPropertyChanged(nameof(CanConfigureModel));OnPropertyChanged(nameof(CanSaveRecipe));OnPropertyChanged(nameof(CanManageSelectedModel));
        OnPropertyChanged(nameof(CanCapture));OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CaptureLabel));OnPropertyChanged(nameof(CycleLabel));OnPropertyChanged(nameof(ActiveModel));
        RefreshCameraState();
    }
    private void RefreshCameraState()
    {
        OnPropertyChanged(nameof(CanSearchCamera));OnPropertyChanged(nameof(CanSelectCamera));OnPropertyChanged(nameof(CanConnectCamera));
        OnPropertyChanged(nameof(CanDisconnectCamera));OnPropertyChanged(nameof(CanToggleAcquisition));OnPropertyChanged(nameof(CanEditCameraParameters));
        OnPropertyChanged(nameof(AcquisitionActionLabel));
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
        var draft=new Recipe(draftId,ModelCode.Trim(),ModelName.Trim(),saved?.Revision??0,[End1.Spec(),End2.Spec()],DateTimeOffset.UtcNow);
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
    [RelayCommand]private void StartRun()=>Guard(()=>
    {
        if(Busy||Running)return;
        RunPage=true;RefreshOcr();
        if(saved==null||Dirty)throw new InvalidOperationException("Cần lưu recipe trước khi RUN.");
        if(Ocr.AvailabilityError is{}error)throw new InvalidOperationException(error);
        runtimeRecipe=saved.Copy();Session.Begin(runtimeRecipe);Running=true;
        Result1.Reset(runtimeRecipe.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);waitingSince=DateTimeOffset.UtcNow;
        RunStatus="CHỜ ĐẦU 1";Message="RUN đã bắt đầu. Nạp ảnh offline hoặc nhận frame camera.";RefreshState();
    });
    [RelayCommand]private void StopRun()
    {
        Session.Stop();Running=false;RunStatus="ĐÃ DỪNG";RefreshState();
    }
    [RelayCommand]private void NextProduct()=>Guard(()=>
    {
        if(!CanNext)return;
        Session.Begin(runtimeRecipe!);Result1.Reset(runtimeRecipe!.Ends[0]);Result2.Reset(runtimeRecipe.Ends[1]);
        waitingSince=DateTimeOffset.UtcNow;RunStatus="CHỜ ĐẦU 1";RefreshState();
    });
    [RelayCommand]private async Task LoadRuntimeAsync()
    {
        if(!CanCapture)return;
        var file=ChooseImage();if(file==null)return;
        await GuardAsync(()=>InspectAsync(ImageFiles.Load(file)));
    }
    [RelayCommand]private async Task CaptureRuntimeAsync()
    {
        if(!CanCapture)return;
        await GuardAsync(async()=>
        {
            if(!Acquiring||latest==null||latest.CapturedAt<waitingSince||DateTimeOffset.UtcNow-latest.CapturedAt>TimeSpan.FromSeconds(2))
                throw new InvalidOperationException("Chưa có camera frame mới cho đầu này.");
            if(latest.Source=="SIMULATION")throw new InvalidOperationException("Không dùng ảnh camera giả lập cho RUN sản xuất.");
            await InspectAsync(latest);
        });
    }
    private async Task InspectAsync(ImageFrame frame)
    {
        var end=Session.NextEnd;var target=end==0?Result1:Result2;target.Show(frame,null);
        RunStatus=$"ĐANG OCR ĐẦU {end+1}";
        try
        {
            var result=await Session.AcceptAsync(frame);
            if(result==null)return;
            target.Show(frame,result);waitingSince=DateTimeOffset.UtcNow;
            RunStatus=Session.Result?.Verdict.ToString().ToUpperInvariant()??"CHỜ ĐẦU 2";
            Message=Session.Result!=null?"Đã lưu kết quả và ảnh nguồn. Sản phẩm tiếp theo hoặc Stop.":"Đã kiểm tra đầu 1. Chờ đầu 2 cùng sản phẩm.";
        }
        catch {target.Status="ERROR";RunStatus="ERROR";throw;}
        finally{RefreshState();}
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
            CameraConnected=true;CameraState=CameraUiState.Connected;
            SourceStatus=target.IsSimulation?"SIMULATION":"CAMERA CONNECTED";
            CameraStatus=$"ĐÃ KẾT NỐI · {target.Name}";
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
            if(!double.TryParse(Exposure,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var exp)||!double.IsFinite(exp)||exp<=0||
               !double.TryParse(Gain,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var g)||!double.IsFinite(g)||g<0)
                throw new InvalidOperationException("Exposure/Gain không hợp lệ. Dùng dấu chấm thập phân.");
            Camera.SetParameter("ExposureTime",exp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Camera.SetParameter("Gain",g.ToString(System.Globalization.CultureInfo.InvariantCulture));
            applied=true;
        }));
        if(applied)Message="ACQUISITION · Đã áp dụng Exposure và Gain.";
    }
    [RelayCommand]private async Task AcquisitionAsync()
    {
        if(!CanToggleAcquisition)return;
        if(Acquiring){await StopAcquisitionAsync();return;}
        await GuardAsync(async()=>
        {
            await Task.Run(Camera.Start);acquisition=new();Acquiring=true;CameraState=CameraUiState.Acquiring;
            SourceStatus="CAMERA LIVE";CameraStatus="ĐANG ACQUISITION · CHỜ FRAME";
            Message="ACQUISITION · Đã bắt đầu nhận ảnh camera.";
            var token=acquisition.Token;
            acquisitionTask=Task.Run(async()=>
            {
                try
                {
                    var lastReceived=DateTimeOffset.UtcNow;
                    while(!token.IsCancellationRequested)
                    {
                        ImageFrame frame;
                        try{frame=Camera.Grab(300);lastReceived=DateTimeOffset.UtcNow;}
                        catch(TimeoutException)
                        {
                            if(DateTimeOffset.UtcNow-lastReceived>TimeSpan.FromSeconds(3))
                                throw new TimeoutException("Camera không có frame mới trong 3 giây. Dừng và kết nối lại camera.");
                            continue;
                        }
                        var bitmap=ImageFiles.Bitmap(frame);
                        await dispatcher.InvokeAsync(()=>
                        {
                            if(!token.IsCancellationRequested)
                            {
                                latest=frame;LiveImage=bitmap;
                                CameraStatus=$"ĐANG ACQUISITION · {frame.Width} × {frame.Height}";
                                RefreshGrabState();
                            }
                        });
                        await Task.Delay(15,token);
                    }
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){}
                catch(Exception ex)
                {
                    await dispatcher.InvokeAsync(()=>
                    {
                        Acquiring=false;latest=null;CameraState=CameraUiState.Error;
                        CameraStatus="LỖI ACQUISITION";Message=$"ACQUISITION · {ex.Message}";
                        if(Running){Session.Stop();Running=false;RunStatus="ERROR";}
                    });
                }
                finally{try{Camera.Stop();}catch{/* Next connection reports native state. */}}
            });
        });
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
        CameraState=Cameras.Count>0?CameraUiState.Found:CameraUiState.NotFound;
        CameraStatus=Cameras.Count>0?"ĐÃ NGẮT CAMERA · SẴN SÀNG KẾT NỐI LẠI":"ĐÃ NGẮT CAMERA";
        Message="ACQUISITION · Đã ngắt kết nối camera.";
    }
    public async Task ShutdownAsync(){StopRun();await StopAcquisitionAsync();await Task.Run(()=>{Camera.Dispose();Ocr.Dispose();});}
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
