using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Desktop.Services;

namespace WireMarkerInspection.Desktop.ViewModels;
public sealed record OrientationChoice(TextOrientation Value,string Label);
public partial class EndEditorViewModel(int number) : ObservableObject
{
    public int Number {get;}=number;
    public string Key=>Number.ToString();
    public string Title=>$"ĐẦU {Number}";
    public ImageFrame? Frame {get;private set;}
    public event EventHandler? Changed;
    private bool loading;
    [ObservableProperty] private BitmapSource? image;
    [ObservableProperty] private SearchRoi? roi;
    [ObservableProperty] private string expectedText="";
    [ObservableProperty] private TextOrientation orientation;
    [ObservableProperty] private bool applied;
    [ObservableProperty] private string message="Load ảnh mẫu để bắt đầu.";
    [ObservableProperty] private OcrRegion[]? regions;
    public ObservableCollection<RegionViewModel> Previews {get;}=[];
    public OrientationChoice[] Orientations {get;}=[new(TextOrientation.Degrees0,"0° · cố định"),new(TextOrientation.Degrees180,"180° · cố định"),new(TextOrientation.Auto,"Auto · 0° / 180°")];
    public string RoiSummary => Roi is null?"Chưa có ROI":$"{Roi.Shape} · {Roi.Bounds.Width:F0} × {Roi.Bounds.Height:F0} px";
    partial void OnRoiChanged(SearchRoi? value) {OnPropertyChanged(nameof(RoiSummary));Dirty();}
    partial void OnExpectedTextChanged(string value)=>Dirty();
    partial void OnOrientationChanged(TextOrientation value)=>Dirty();
    private void Dirty()
    {
        if(loading)return;
        Applied=false;Regions=null;Previews.Clear();Message="Có thay đổi · cần Apply.";Changed?.Invoke(this,EventArgs.Empty);
    }
    public void SetFrame(ImageFrame frame)
    {
        Frame=frame;Image=ImageFiles.Bitmap(frame);
        Roi=null;Applied=false;Regions=null;Previews.Clear();
        Message="Vẽ một ROI lớn bao quanh tất cả vùng text.";Changed?.Invoke(this,EventArgs.Empty);
    }
    public void Clear()
    {
        loading=true;
        try {Frame=null;Image=null;Roi=null;ExpectedText="";Orientation=TextOrientation.Degrees0;Applied=false;Regions=null;Previews.Clear();Message="Load ảnh mẫu để bắt đầu.";}
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
            Applied=true;Regions=null;Previews.Clear();Message="Recipe đã lưu.";
        }
        finally {loading=false;}
    }
    public EndRecipe Spec()
    {
        if(Frame==null || Roi==null)throw new InvalidOperationException($"Đầu {Number}: cần ảnh mẫu và ROI.");
        var lines=ExpectedText.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        return new("",Frame.Width,Frame.Height,Roi.Copy(),lines,Orientation);
    }
    public void Apply()
    {
        var spec=Spec();
        if(spec.Validate() is {} error)throw new InvalidOperationException(error);
        Applied=true;Message="Đã Apply · Save Recipe để lưu cả hai đầu.";Changed?.Invoke(this,EventArgs.Empty);
    }
    public void ShowReading(OcrReading reading)
    {
        Regions=reading.Regions;Previews.Clear();
        for(int i=0;i<reading.Regions.Length;i++)Previews.Add(new(i+1,reading.Regions[i]));
        Message=$"Detect {reading.Regions.Length} vùng · xoay {reading.Rotation}° · text mẫu không bị thay đổi.";
    }
}
public sealed class RegionViewModel(int number,OcrRegion region)
{
    public string Label=>$"OCR {number} · {region.Confidence:P1}";
    public string Text=>region.Text;
    public BitmapSource Image {get;}=ImageFiles.Decode(region.CropPng);
}
public partial class EndResultViewModel(int number) : ObservableObject
{
    public string Title=>$"ẢNH ĐẦU {number}";
    [ObservableProperty]private BitmapSource? image;
    [ObservableProperty]private SearchRoi? roi;
    [ObservableProperty]private OcrRegion[]? regions;
    [ObservableProperty]private string status="CHỜ ẢNH";
    [ObservableProperty]private string expected="";
    [ObservableProperty]private string actual="—";
    [ObservableProperty]private string detail="";
    public ObservableCollection<RegionViewModel> Previews{get;}=[];
    public void Reset(EndRecipe recipe)
    {
        Image=null;Roi=recipe.Roi.Copy();Regions=null;Status="CHỜ ẢNH";Expected=string.Join("\n",recipe.ExpectedLines);Actual="—";Detail="";Previews.Clear();
    }
    public void Show(ImageFrame frame,EndResult? result)
    {
        Image=ImageFiles.Bitmap(frame);
        if(result==null){Status="ĐANG OCR";return;}
        Status=result.Verdict==Verdict.Ok?"OK":"NG";Actual=string.Join("\n",result.Reading.Regions.Select(r=>r.Text));
        Regions=result.Reading.Regions;Previews.Clear();
        for(int i=0;i<result.Reading.Regions.Length;i++)Previews.Add(new(i+1,result.Reading.Regions[i]));
        Detail=result.Differences.Length==0?result.Reason:string.Join(" · ",result.Differences.Select(d=>$"OCR {d.Region}: khác tại ký tự {d.FirstMismatch+1}"));
    }
}
