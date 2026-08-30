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
public partial class MainViewModel : ObservableObject
{
    public FileRecipeStore Store{get;}
    public NativeOcrEngine Ocr{get;}
    public NAcquireCamera Camera{get;}=new();
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
    private CancellationTokenSource? acquisition;
    private Task? acquisitionTask;
    private DateTimeOffset waitingSince;
    private Recipe? runtimeRecipe;
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
    public bool CanCapture=>Running&&!Busy&&Session.State is InspectionState.WaitingEnd1 or InspectionState.WaitingEnd2;
    public bool CanNext=>Running&&!Busy&&Session.State==InspectionState.Completed;
    public string CaptureLabel=>$"Nhận ảnh đầu {Session.NextEnd+1}";
    public string ModelCount=>$"{Models.Count} MODELS";
    public string CycleLabel=>Session.CycleId==Guid.Empty?"—":Session.CycleId.ToString("N")[..12].ToUpperInvariant();
    public string ActiveModel=>runtimeRecipe is null?"CHƯA CHỌN":$"{runtimeRecipe.ModelCode} / v{runtimeRecipe.Revision}";
    public Func<string,bool>? Confirm{get;set;}
    public MainViewModel(string dataRoot)
    {
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
    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanEdit));OnPropertyChanged(nameof(CanConfigureModel));OnPropertyChanged(nameof(CanSaveRecipe));OnPropertyChanged(nameof(CanManageSelectedModel));
        OnPropertyChanged(nameof(CanCapture));OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CaptureLabel));OnPropertyChanged(nameof(CycleLabel));OnPropertyChanged(nameof(ActiveModel));
    }
    partial void OnSelectedModelChanged(RecipeRow? oldValue,RecipeRow? newValue)
    {
        OnPropertyChanged(nameof(CanManageSelectedModel));
        if(loading)return;
        if(Dirty&&Confirm?.Invoke(newValue==null?"Bỏ thay đổi chưa lưu và bỏ chọn model?":"Bỏ thay đổi chưa lưu và mở model đã chọn?")==false)
        {
            loading=true;try{SelectedModel=oldValue;}finally{loading=false;}
            OnPropertyChanged(nameof(CanManageSelectedModel));
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
            loading=true;try{SelectedModel=oldValue;}finally{loading=false;}
            OnPropertyChanged(nameof(CanManageSelectedModel));Message=ex.Message;
        }
    }
    private void SetModelSetupActive(bool value)
    {
        if(modelSetupActive==value)return;
        modelSetupActive=value;OnPropertyChanged(nameof(CanConfigureModel));OnPropertyChanged(nameof(CanSaveRecipe));
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
    [RelayCommand]private void GrabReference(string end)=>Guard(()=>
    {
        if(!CanConfigureModel)return;
        if(latest==null||!Acquiring||DateTimeOffset.UtcNow-latest.CapturedAt>TimeSpan.FromSeconds(2))
            throw new InvalidOperationException("Cần live frame mới. Start Acquisition hoặc Load Image.");
        Editor(end).SetFrame(latest with {Bgr=[..latest.Bgr]});
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
    [RelayCommand]private async Task ScanCamerasAsync()
    {
        if(!CanEdit||Acquiring)return;
        await GuardAsync(async()=>{var devices=await Task.Run(Camera.Enumerate);Cameras.Clear();foreach(var d in devices)Cameras.Add(d);SelectedCamera=Cameras.FirstOrDefault();CameraStatus=$"{Cameras.Count} thiết bị. Backend giả lập được đánh dấu trong tên nguồn.";});
    }
    [RelayCommand]private async Task ConnectAsync()
    {
        if(!CanEdit||SelectedCamera==null||Acquiring)return;
        await GuardAsync(async()=>{await Task.Run(()=>Camera.Open(SelectedCamera));CameraConnected=true;SourceStatus=SelectedCamera.IsSimulation?"SIMULATION":"CAMERA";CameraStatus=SelectedCamera.Name;});
    }
    [RelayCommand]private async Task CameraParametersAsync()
    {
        if(!CanEdit||!CameraConnected||Acquiring)return;
        await GuardAsync(()=>Task.Run(()=>
        {
            if(!double.TryParse(Exposure,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var exp)||!double.IsFinite(exp)||exp<=0||
               !double.TryParse(Gain,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var g)||!double.IsFinite(g)||g<0)
                throw new InvalidOperationException("Exposure/Gain không hợp lệ. Dùng dấu chấm thập phân.");
            Camera.SetParameter("ExposureTime",exp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Camera.SetParameter("Gain",g.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }));
    }
    [RelayCommand]private async Task AcquisitionAsync()
    {
        if(Busy)return;
        if(Acquiring){await StopAcquisitionAsync();return;}
        if(!CameraConnected){Message="Kết nối camera trước.";return;}
        await GuardAsync(async()=>
        {
            await Task.Run(Camera.Start);acquisition=new();Acquiring=true;
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
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>
                        {
                            if(!token.IsCancellationRequested){latest=frame;LiveImage=bitmap;CameraStatus=frame.Source;}
                        });
                        await Task.Delay(15,token);
                    }
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){}
                catch(Exception ex)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>
                    {
                        Acquiring=false;latest=null;CameraStatus="Camera error";Message=ex.Message;
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
    }
    [RelayCommand]private async Task DisconnectAsync()
    {
        if(Busy)return;
        StopRun();await StopAcquisitionAsync();await Task.Run(Camera.Close);CameraConnected=false;SourceStatus="OFFLINE";CameraStatus="Đã ngắt camera";
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
