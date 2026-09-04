using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Desktop.Services;
using WireMarkerInspection.Controls.Localization;

namespace WireMarkerInspection.Desktop.ViewModels;
public partial class EndEditorViewModel(int number) : ObservableObject
{
    public int Number {get;}=number;
    public string Key=>Number.ToString();
    public string Title=>AppLocalizer.Format("EndTitleFormat",Number);
    public ImageFrame? Frame {get;private set;}
    public TerminalTemplate Terminal {get;private set;}=new();
    public string TerminalSummary=>AppLocalizer.Text(Terminal.Enabled?"MatchingRequired":"MatchingDisabled")+(Terminal.Enabled?" · "+AppLocalizer.Text($"MatchingAlgorithm{Terminal.Algorithm}"):"");
    public void SetTerminal(TerminalTemplate template){Terminal=template.Copy();OnPropertyChanged(nameof(TerminalSummary));Dirty();}
    public event EventHandler? Changed;
    private bool loading;
    private string messageKey="LoadReferenceFirst";
    private object[] messageArgs=[];
    [ObservableProperty] private BitmapSource? image;
    [ObservableProperty] private SearchRoi? roi;
    [ObservableProperty] private string expectedText="";
    [ObservableProperty] private TextOrientation orientation;
    [ObservableProperty] private bool applied;
    [ObservableProperty] private string message=AppLocalizer.Text("LoadReferenceFirst");
    [ObservableProperty] private OcrRegion[]? regions;
    public ObservableCollection<RegionViewModel> Previews {get;}=[];
    public LocalizedOption<TextOrientation>[] Orientations{get;}=[
        new(TextOrientation.Degrees0,"DirectionUpright"),
        new(TextOrientation.Degrees180,"DirectionInverted"),
        new(TextOrientation.Auto,"DirectionAuto")];
    public string RoiSummary => Roi is null?AppLocalizer.Text("NoRoi"):
        AppLocalizer.Format("RoiSummaryFormat",Roi.Shape,Roi.Bounds.Width.ToString("F0"),Roi.Bounds.Height.ToString("F0"));
    partial void OnRoiChanged(SearchRoi? value) {OnPropertyChanged(nameof(RoiSummary));Dirty();}
    partial void OnExpectedTextChanged(string value)=>Dirty();
    partial void OnOrientationChanged(TextOrientation value)=>Dirty();
    private void Dirty()
    {
        if(loading)return;
        Applied=false;Regions=null;Previews.Clear();SetMessage("ChangesNeedApply");Changed?.Invoke(this,EventArgs.Empty);
    }
    public void SetFrame(ImageFrame frame)
    {
        if(Frame!=null&&(Frame.Width!=frame.Width||Frame.Height!=frame.Height))Terminal=Terminal with {SearchRoi=null};
        Frame=frame;Image=ImageFiles.Bitmap(frame);
        Roi=null;Applied=false;Regions=null;Previews.Clear();
        SetMessage("DrawSearchRoi");Changed?.Invoke(this,EventArgs.Empty);
    }
    public void Clear()
    {
        loading=true;
        try {Frame=null;Image=null;Roi=null;Terminal=new();OnPropertyChanged(nameof(TerminalSummary));ExpectedText="";Orientation=TextOrientation.Degrees0;Applied=false;Regions=null;Previews.Clear();SetMessage("LoadReferenceFirst");}
        finally {loading=false;}
    }
    public void Load(EndRecipe recipe, byte[] png)
    {
        loading=true;
        try
        {
            Image=ImageFiles.Decode(png);Frame=ImageFiles.Frame(Image,"REFERENCE");
            if(Frame.Width!=recipe.Width || Frame.Height!=recipe.Height)throw new InvalidDataException("Reference dimensions do not match recipe.");
            Roi=recipe.Roi.Copy();ExpectedText=string.Join("\n",recipe.ExpectedLines);Orientation=recipe.Orientation;
            Terminal=recipe.Terminal?.Copy()??new();OnPropertyChanged(nameof(TerminalSummary));
            Applied=true;Regions=null;Previews.Clear();SetMessage("SavedRecipeLoaded");
        }
        finally {loading=false;}
    }
    public EndRecipe Spec()
    {
        if(Frame==null || Roi==null)throw new InvalidOperationException(AppLocalizer.Format("ReferenceRequiredFormat",Number));
        var lines=ExpectedText.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        return new("",Frame.Width,Frame.Height,Roi.Copy(),lines,Orientation,Terminal.Copy());
    }
    public void Apply()
    {
        var spec=Spec();
        if(spec.Validate() is {} error)throw new InvalidOperationException(error);
        Applied=true;SetMessage("AppliedNeedSave");Changed?.Invoke(this,EventArgs.Empty);
    }
    public void ShowReading(OcrReading reading)
    {
        // Decode before changing the draft: a malformed OCR preview must not partially teach a recipe.
        var previews=reading.Regions.Select((region,index)=>new RegionViewModel(index+1,region)).ToArray();
        var hasText=reading.Regions.Length>0&&reading.Regions.All(region=>!string.IsNullOrWhiteSpace(region.Text));
        if(hasText)ExpectedText=string.Join("\n",reading.Regions.Select(region=>region.Text));
        Regions=reading.Regions;Previews.Clear();
        foreach(var preview in previews)Previews.Add(preview);
        SetMessage(hasText?"OcrSampleFilledFormat":"OcrSampleUnchanged",reading.Regions.Length,reading.Rotation);
    }
    private void SetMessage(string key,params object[] args){messageKey=key;messageArgs=args;Message=AppLocalizer.Format(key,args);}
    public void RefreshLanguage(){OnPropertyChanged(nameof(Title));OnPropertyChanged(nameof(TerminalSummary));foreach(var option in Orientations)option.RefreshLabel();OnPropertyChanged(nameof(RoiSummary));Message=AppLocalizer.Format(messageKey,messageArgs);}
}
public sealed class RegionViewModel(int number,OcrRegion region)
{
    public string Label=>$"OCR {number} · {region.Confidence:P1}";
    public string Text=>region.Text;
    public BitmapSource Image {get;}=ImageFiles.Decode(region.CropPng);
}
public partial class EndResultViewModel(int number) : ObservableObject
{
    private TextOrientation requiredOrientation;
    private EndResult? lastResult;
    public string Title=>AppLocalizer.Format("ImageEndFormat",number);
    [ObservableProperty]private BitmapSource? image;
    [ObservableProperty]private SearchRoi? roi;
    [ObservableProperty]private OcrRegion[]? regions;
    [ObservableProperty]private string status=AppLocalizer.Text("WaitingImage");
    [ObservableProperty]private string expected="";
    [ObservableProperty]private string actual="—";
    [ObservableProperty]private string detail="";
    public ObservableCollection<RegionViewModel> Previews{get;}=[];
    public BitmapSource? TerminalAligned=>lastResult?.Terminal is {AlignedPng.Length:>0} t?ImageFiles.Decode(t.AlignedPng):null;
    public BitmapSource? TerminalReference=>lastResult?.Terminal is {TemplatePng.Length:>0} t?ImageFiles.Decode(t.TemplatePng):null;
    public SearchRoi? TerminalOutline=>lastResult?.Terminal is {Corners.Length:4} t&&t.Diagnostics?.PoseEvaluated!=false?new(RoiShape.Polygon,t.Corners):null;
    public bool HasTerminal=>lastResult?.Terminal!=null;
    public bool? TextPassed=>lastResult==null?null:lastResult.Reading.Regions.Length>0&&lastResult.Differences.Length==0;
    public InspectionCheck TextCheck=>lastResult==null?new("",null):new((TextPassed==true?"TEXT · OK · ":"TEXT · NG · ")+
        (lastResult.Reading.Regions.Length==0?AppLocalizer.Text("NoTextDetected"):TextPassed==true?AppLocalizer.Text("MatchingTextExact"):
        AppLocalizer.Text("TextMismatch")+" · "+string.Join(" · ",lastResult.Differences.Select(d=>AppLocalizer.Format("DifferenceFormat",d.Region,d.FirstMismatch+1)))),TextPassed);
    public InspectionCheck OrientationCheck
    {
        get
        {
            if(lastResult==null)return new("",null);
            if(lastResult.Reading.Regions.Length==0)return new(AppLocalizer.Text("RequiredDirection")+" · "+AppLocalizer.Text("MatchingNotEvaluated"),null);
            int? required=requiredOrientation switch{TextOrientation.Degrees0=>0,TextOrientation.Degrees180=>180,_=>null};
            int actual=lastResult.Reading.Rotation;bool valid=actual is 0 or 180;bool pass=valid&&(!required.HasValue||required==actual);
            return new(AppLocalizer.Text("RequiredDirection")+" · "+(pass?"OK":"NG")+" · "+
                (pass?$"{actual}°":OrientationReason(required,actual,valid)),pass);
        }
    }
    public IReadOnlyList<InspectionCheck> TerminalChecks=>lastResult==null?[]:lastResult.Terminal==null?
        [new(AppLocalizer.Text("MatchingDisabled"),null)]:MatchingPresentation.Checks(lastResult.Terminal);
    private void RefreshChecks(){OnPropertyChanged(nameof(TextPassed));OnPropertyChanged(nameof(TextCheck));OnPropertyChanged(nameof(OrientationCheck));OnPropertyChanged(nameof(TerminalChecks));}
    private void RefreshTerminal(){OnPropertyChanged(nameof(TerminalAligned));OnPropertyChanged(nameof(TerminalReference));OnPropertyChanged(nameof(TerminalOutline));OnPropertyChanged(nameof(HasTerminal));RefreshChecks();}
    public void Reset(EndRecipe recipe)
    {
        requiredOrientation=recipe.Orientation;lastResult=null;
        Image=null;Roi=recipe.Roi.Copy();Regions=null;Status=AppLocalizer.Text("WaitingImage");Expected=string.Join("\n",recipe.ExpectedLines);Actual="—";Detail="";Previews.Clear();
        RefreshTerminal();
    }
    public void Show(ImageFrame frame,EndResult? result)
    {
        Image=ImageFiles.Bitmap(frame);
        if(result==null){lastResult=null;Status=AppLocalizer.Text("ProcessingOcr");Actual="—";Detail="";Regions=null;Previews.Clear();RefreshTerminal();return;}
        lastResult=result;
        Status=result.Verdict==Verdict.Ok?"OK":"NG";Actual=string.Join("\n",result.Reading.Regions.Select(r=>r.Text));
        Regions=result.Reading.Regions;Previews.Clear();
        for(int i=0;i<result.Reading.Regions.Length;i++)Previews.Add(new(i+1,result.Reading.Regions[i]));
        Detail=BuildDetail(result);
        RefreshTerminal();
    }
    public void CopyFrom(EndResultViewModel source)
    {
        requiredOrientation=source.requiredOrientation;lastResult=source.lastResult;
        Image=source.Image;Roi=source.Roi?.Copy();Regions=source.Regions is null?null:[..source.Regions];
        Status=source.Status;Expected=source.Expected;Actual=source.Actual;Detail=source.Detail;
        RefreshTerminal();
        Previews.Clear();
        if(Regions is null)return;
        for(int i=0;i<Regions.Length;i++)Previews.Add(new(i+1,Regions[i]));
    }
    private string BuildDetail(EndResult result)
    {
        var required=requiredOrientation switch {TextOrientation.Degrees0=>0,TextOrientation.Degrees180=>180,_=>(int?)null};
        var valid=result.Reading.Rotation is 0 or 180;
        var orientationMatches=valid&&(required==null||result.Reading.Rotation==required);
        var reason=result.Differences.Length==0&&orientationMatches&&result.Reading.Regions.Length>0?AppLocalizer.Text("ExactMatch"):
            result.Reading.Regions.Length==0?AppLocalizer.Text("NoTextDetected"):
            result.Differences.Length>0&&!orientationMatches?AppLocalizer.Format("TextAndOrientationMismatchFormat",OrientationReason(required,result.Reading.Rotation,valid)):
            result.Differences.Length>0?AppLocalizer.Text("TextMismatch"):
            OrientationReason(required,result.Reading.Rotation,valid);
        var differences=string.Join(" · ",result.Differences.Select(d=>AppLocalizer.Format("DifferenceFormat",d.Region,d.FirstMismatch+1)));
        var text=result.Differences.Length==0?reason:$"{reason} · {differences}";
        return text+Environment.NewLine+(result.Terminal==null?AppLocalizer.Text("MatchingDisabled"):MatchingPresentation.Describe(result.Terminal));
    }
    private static string OrientationReason(int? required,int actual,bool valid)=>!valid||required==null
        ?AppLocalizer.Format("InvalidRotationFormat",actual)
        :AppLocalizer.Format("OrientationMismatchFormat",required,actual);
    public void RefreshLanguage(){OnPropertyChanged(nameof(Title));RefreshChecks();if(lastResult!=null)Detail=BuildDetail(lastResult);else Status=AppLocalizer.Text(Image==null?"WaitingImage":"ProcessingOcr");}
}
