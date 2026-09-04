using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WireMarkerInspection.Application;
using WireMarkerInspection.Controls.Localization;
using WireMarkerInspection.Desktop.Services;
using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Desktop.ViewModels;

public sealed class MatchParameterViewModel(MatchParameterDefinition definition,double value) : ObservableObject
{
    private string text=value.ToString("G",CultureInfo.InvariantCulture);
    public MatchParameter Key=>definition.Key;
    public bool IsImportant=>Key is MatchParameter.Score or MatchParameter.Ncc or MatchParameter.Ssim or MatchParameter.Edge
        or MatchParameter.AngleMin or MatchParameter.AngleMax or MatchParameter.ScaleMin or MatchParameter.ScaleMax
        or MatchParameter.Ratio or MatchParameter.MaxDistance or MatchParameter.MinMatches or MatchParameter.MinInliers
        or MatchParameter.InlierRatio or MatchParameter.Coverage or MatchParameter.Reprojection
        or MatchParameter.DetectorThreshold or MatchParameter.Contrast or MatchParameter.Keypoints
        or MatchParameter.ValidPixels or MatchParameter.Distortion;
    public string Label=>(IsImportant?"* ":"")+AppLocalizer.Text($"MatchParam{Key}");
    public string Hint=>$"{definition.Min} … {definition.Max}\n"+AppLocalizer.Text(Key switch
    {
        MatchParameter.Score=>"MatchingHintScore",
        MatchParameter.Ncc or MatchParameter.Ssim or MatchParameter.Edge=>"MatchingHintAppearance",
        MatchParameter.Ratio or MatchParameter.MaxDistance=>"MatchingHintDescriptor",
        MatchParameter.MinMatches or MatchParameter.MinInliers or MatchParameter.InlierRatio or MatchParameter.Coverage=>"MatchingHintEvidence",
        MatchParameter.Reprojection=>"MatchingHintRansac",
        MatchParameter.DetectorThreshold or MatchParameter.Contrast or MatchParameter.FastThreshold=>"MatchingHintDetector",
        MatchParameter.Keypoints=>"MatchingHintKeypoints",
        MatchParameter.AngleMin or MatchParameter.AngleMax or MatchParameter.ScaleMin or MatchParameter.ScaleMax or MatchParameter.Distortion or MatchParameter.ValidPixels=>"MatchingHintGeometry",
        _=>"MatchingHintAdvanced"
    });
    public string Text{get=>text;set=>SetProperty(ref text,value);}
    public double Value=>double.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)?v:double.NaN;
    public void RefreshLanguage(){OnPropertyChanged(nameof(Label));OnPropertyChanged(nameof(Hint));}
}

/// <summary>Isolated modal draft. Cancel/test never modifies the recipe or its OCR ROI.</summary>
public partial class TemplateEditorViewModel : ObservableObject,IDisposable
{
    private readonly ITemplateMatcher matcher;
    private readonly ImageFrame? reference;
    private readonly Dictionary<MatchingAlgorithm,Dictionary<MatchParameter,double>> profiles;
    private readonly CancellationTokenSource lifetime=new();
    private bool ready;
    private bool messageFailed;
    private TemplateMatchResult? result;
    private ImageFrame? testFrame;
    [ObservableProperty]private bool enabled;
    [ObservableProperty]private bool busy;
    [ObservableProperty]private MatchingAlgorithm algorithm;
    [ObservableProperty]private BitmapSource? templateImage;
    [ObservableProperty]private BitmapSource? testImage;
    [ObservableProperty]private BitmapSource? alignedImage;
    [ObservableProperty]private BitmapSource? croppedTemplate;
    [ObservableProperty]private SearchRoi? learnRoi;
    [ObservableProperty]private SearchRoi? searchRoi;
    [ObservableProperty]private SearchRoi? matchedRoi;
    [ObservableProperty]private string message="";
    public bool CanEdit=>!Busy;
    public bool Passed=>result?.Passed==true;
    public TemplateMatchResult? Result=>result;
    public IReadOnlyList<InspectionCheck> Checks=>result==null?[new(Message,messageFailed?false:null)]:MatchingPresentation.Checks(result);
    partial void OnMessageChanged(string value)=>OnPropertyChanged(nameof(Checks));
    public LocalizedOption<MatchingAlgorithm>[] Algorithms{get;}=Enum.GetValues<MatchingAlgorithm>().Select(a=>new LocalizedOption<MatchingAlgorithm>(a,"MatchingAlgorithm"+a)).ToArray();
    public ObservableCollection<MatchParameterViewModel> Parameters{get;}=[];
    public IEnumerable<MatchParameterViewModel> BasicParameters=>Parameters.Where(p=>p.IsImportant);
    public IEnumerable<MatchParameterViewModel> AdvancedParameters=>Parameters.Where(p=>!p.IsImportant);
    public TemplateEditorViewModel(EndEditorViewModel end,ITemplateMatcher matcher)
    {
        this.matcher=matcher;reference=end.Frame;
        var template=end.Terminal.Copy();Enabled=template.Enabled;Algorithm=template.Algorithm;
        profiles=template.Profiles??[];LearnRoi=template.LearnRoi;SearchRoi=template.SearchRoi;
        if(template.TemplatePng.Length>0)TemplateImage=ImageFiles.Decode(template.TemplatePng);
        testFrame=reference;TestImage=end.Image;
        LoadParameters();ready=true;Message=AppLocalizer.Text("MatchingSetupHint");
        AppLocalizer.LanguageChanged+=LanguageChanged;
    }
    partial void OnBusyChanged(bool value){OnPropertyChanged(nameof(CanEdit));TestCommand.NotifyCanExecuteChanged();}
    partial void OnAlgorithmChanging(MatchingAlgorithm value){if(ready)SaveParameters();}
    partial void OnAlgorithmChanged(MatchingAlgorithm value){if(ready){LoadParameters();Invalidate();}}
    partial void OnLearnRoiChanged(SearchRoi? value)=>Invalidate();
    partial void OnEnabledChanged(bool value)=>Invalidate();
    partial void OnSearchRoiChanged(SearchRoi? value)=>Invalidate();
    private void Invalidate(){messageFailed=false;result=null;MatchedRoi=null;AlignedImage=null;CroppedTemplate=null;OnPropertyChanged(nameof(Passed));if(ready)Message=AppLocalizer.Text("MatchingSetupHint");OnPropertyChanged(nameof(Checks));}
    private void Fail(Exception ex){Invalidate();messageFailed=true;Message=AppLocalizer.Text("MatchingError")+": "+ex.Message;}
    private void SaveParameters()
    {
        if(!profiles.TryGetValue(Algorithm,out var values))profiles[Algorithm]=values=MatchingParameters.Defaults(Algorithm);
        foreach(var p in Parameters)values[p.Key]=p.Value;
    }
    private void LoadParameters()
    {
        Parameters.Clear();var values=profiles.GetValueOrDefault(Algorithm)??MatchingParameters.Defaults(Algorithm);
        foreach(var d in MatchingParameters.Definitions.Where(d=>Relevant(d.Key,Algorithm)))
        {
            var row=new MatchParameterViewModel(d,values.GetValueOrDefault(d.Key,d.Default));
            row.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(row.Text))Invalidate();};Parameters.Add(row);
        }
        OnPropertyChanged(nameof(BasicParameters));OnPropertyChanged(nameof(AdvancedParameters));
    }
    private static bool Relevant(MatchParameter p,MatchingAlgorithm a)=>p switch
    {
        MatchParameter.AngleStep or MatchParameter.ScaleStep or MatchParameter.FineAngle or MatchParameter.FineScale or MatchParameter.Method=>a==MatchingAlgorithm.Normal,
        MatchParameter.Ratio or MatchParameter.MaxDistance or MatchParameter.MinMatches or MatchParameter.MinInliers or MatchParameter.InlierRatio or MatchParameter.Reprojection or MatchParameter.Confidence or MatchParameter.Iterations or MatchParameter.Coverage or MatchParameter.Resize=>a!=MatchingAlgorithm.Normal,
        MatchParameter.Keypoints=>a is MatchingAlgorithm.Sift or MatchingAlgorithm.Orb or MatchingAlgorithm.OrbMaxStable,
        MatchParameter.DetectorThreshold or MatchParameter.Octaves=>a==MatchingAlgorithm.Akaze,
        MatchParameter.Layers=>a is MatchingAlgorithm.Sift or MatchingAlgorithm.Akaze,
        MatchParameter.Contrast or MatchParameter.Sigma=>a==MatchingAlgorithm.Sift,
        MatchParameter.EdgeThreshold=>a is MatchingAlgorithm.Sift or MatchingAlgorithm.Orb or MatchingAlgorithm.OrbMaxStable,
        MatchParameter.PyramidScale or MatchParameter.Levels or MatchParameter.FastThreshold or MatchParameter.PatchSize=>a is MatchingAlgorithm.Orb or MatchingAlgorithm.OrbMaxStable,
        _=>true
    };
    public TerminalTemplate Build()
    {
        SaveParameters();
        var template=new TerminalTemplate(Enabled,Algorithm,Width:TemplateImage?.PixelWidth??0,Height:TemplateImage?.PixelHeight??0,
            LearnRoi:LearnRoi?.Copy(),SearchRoi:SearchRoi?.Copy(),Profiles:profiles.ToDictionary(p=>p.Key,p=>new Dictionary<MatchParameter,double>(p.Value)))
            {TemplatePng=TemplateImage==null?[]:ImageFiles.Png(TemplateImage)};
        if(template.Validate(reference?.Width??0,reference?.Height??0)is{}error)throw new InvalidOperationException(error);
        return template;
    }
    [RelayCommand(CanExecute=nameof(CanEdit))]private void UseReference()
    {
        if(reference==null){Message=AppLocalizer.Text("LoadReferenceFirst");return;}
        SetTemplate(ImageFiles.Bitmap(reference));
    }
    public void SetTemplate(BitmapSource image){TemplateImage=image;LearnRoi=null;Invalidate();}
    public void SetTestFrame(ImageFrame frame)
    {
        if(reference==null||frame.Width!=reference.Width||frame.Height!=reference.Height)throw new InvalidOperationException(AppLocalizer.Text("MatchingDimensionMismatch"));
        testFrame=frame;TestImage=ImageFiles.Bitmap(frame);Invalidate();
    }
    [RelayCommand(CanExecute=nameof(CanEdit))]private void LoadTemplate()=>Load(true);
    [RelayCommand(CanExecute=nameof(CanEdit))]private void LoadTest()=>Load(false);
    private void Load(bool template)
    {
        var dialog=new OpenFileDialog{Filter="Images|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff"};if(dialog.ShowDialog()!=true)return;
        try{var frame=ImageFiles.Load(dialog.FileName);if(template)SetTemplate(ImageFiles.Bitmap(frame));else SetTestFrame(frame);}
        catch(Exception ex){Fail(ex);}
    }
    [RelayCommand(CanExecute=nameof(CanEdit))]private void ResetProfile()
    {
        profiles[Algorithm]=MatchingParameters.Defaults(Algorithm);LoadParameters();Invalidate();
    }
    [RelayCommand(CanExecute=nameof(CanEdit))]private async Task Test()
    {
        try
        {
            if(testFrame==null)throw new InvalidOperationException(AppLocalizer.Text("LoadReferenceFirst"));
            var template=Build();Busy=true;Invalidate();Message=AppLocalizer.Text("MatchingRunning");
            result=await matcher.MatchAsync(testFrame,template,lifetime.Token);
            if(lifetime.IsCancellationRequested)return;
            AlignedImage=result.AlignedPng.Length>0?ImageFiles.Decode(result.AlignedPng):null;
            CroppedTemplate=result.TemplatePng.Length>0?ImageFiles.Decode(result.TemplatePng):null;
            MatchedRoi=result.Corners.Length==4&&result.Diagnostics?.PoseEvaluated!=false?new(RoiShape.Polygon,result.Corners):null;
            Message=MatchingPresentation.Describe(result);OnPropertyChanged(nameof(Passed));
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception ex){Fail(ex);}
        finally{Busy=false;}
    }
    private void LanguageChanged(object? sender,EventArgs e)
    {
        foreach(var option in Algorithms)option.RefreshLabel();foreach(var p in Parameters)p.RefreshLanguage();
        Message=result==null?AppLocalizer.Text("MatchingSetupHint"):MatchingPresentation.Describe(result);
    }
    public void Dispose(){AppLocalizer.LanguageChanged-=LanguageChanged;lifetime.Cancel();lifetime.Dispose();}
}
