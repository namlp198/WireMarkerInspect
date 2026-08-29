using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Controls;

/// <summary>Image-space rendering with read-only overlays. This class never mutates ROI data.</summary>
public class ImageViewer : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(BitmapSource),
        typeof(ImageViewer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, SourceChanged));
    public static readonly DependencyProperty RoiProperty = DependencyProperty.Register(nameof(Roi), typeof(SearchRoi),
        typeof(ImageViewer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d,_) => ((ImageViewer)d).ViewStateChanged?.Invoke(d,EventArgs.Empty)));
    public static readonly DependencyProperty RegionsProperty = DependencyProperty.Register(nameof(Regions), typeof(OcrRegion[]),
        typeof(ImageViewer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OverlayBrushProperty = DependencyProperty.Register(nameof(OverlayBrush), typeof(Brush),
        typeof(ImageViewer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public BitmapSource? Source { get => (BitmapSource?)GetValue(SourceProperty); set => SetValue(SourceProperty,value); }
    public SearchRoi? Roi { get => (SearchRoi?)GetValue(RoiProperty); set => SetValue(RoiProperty,value); }
    public OcrRegion[]? Regions { get => (OcrRegion[]?)GetValue(RegionsProperty); set => SetValue(RegionsProperty,value); }
    public Brush? OverlayBrush { get => (Brush?)GetValue(OverlayBrushProperty); set => SetValue(OverlayBrushProperty,value); }
    public static readonly DependencyProperty ShowOverlaysProperty = DependencyProperty.Register(nameof(ShowOverlays), typeof(bool),
        typeof(ImageViewer), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender,
            (d,_) => ((ImageViewer)d).ViewStateChanged?.Invoke(d,EventArgs.Empty)));
    public bool ShowOverlays { get => (bool)GetValue(ShowOverlaysProperty); set => SetValue(ShowOverlaysProperty,value); }
    public event EventHandler? ViewStateChanged;
    public double Zoom { get; private set; } = 1;
    protected Vector Offset;
    private Point panStart;
    private Vector panOffset;
    private bool panning;
    protected bool IsPanning => panning;
    private bool fitted = true;
    protected virtual SearchRoi? DisplayRoi => Roi;
    public ImageViewer()
    {
        ClipToBounds=true; Focusable=true; Cursor=Cursors.Hand;
        SizeChanged += (_,_) => { if(fitted) Fit(); else InvalidateVisual(); };
        Loaded += (_,_) => {if(fitted) Fit();};
    }
    private static void SourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view=(ImageViewer)d;
        var previous=(BitmapSource?)e.OldValue; var current=(BitmapSource?)e.NewValue;
        if(previous?.PixelWidth!=current?.PixelWidth || previous?.PixelHeight!=current?.PixelHeight) view.Fit();
        view.OnSourceChanged();
        view.ViewStateChanged?.Invoke(view,EventArgs.Empty);
    }
    protected virtual void OnSourceChanged() { }
    public PixelPoint ViewToImage(Point point) => new((point.X-Offset.X)/Zoom,(point.Y-Offset.Y)/Zoom);
    public Point ImageToView(PixelPoint point) => new(point.X*Zoom+Offset.X,point.Y*Zoom+Offset.Y);
    public void Fit()
    {
        fitted=true;
        if(Source==null || ActualWidth<=0 || ActualHeight<=0) {InvalidateVisual();return;}
        Zoom=Math.Max(0.001,Math.Min(ActualWidth/Source.PixelWidth,ActualHeight/Source.PixelHeight));
        Offset=new((ActualWidth-Source.PixelWidth*Zoom)/2,(ActualHeight-Source.PixelHeight*Zoom)/2);
        ViewStateChanged?.Invoke(this,EventArgs.Empty);
        InvalidateVisual();
    }
    public void ActualSize() => ZoomAt(1,new(ActualWidth/2,ActualHeight/2));
    public void ZoomBy(double factor) => ZoomAt(Zoom*factor,new(ActualWidth/2,ActualHeight/2));
    private void ZoomAt(double target,Point anchor)
    {
        if(Source==null)return;
        var pixel=ViewToImage(anchor); Zoom=Math.Clamp(target,0.01,32); fitted=false;
        Offset=new(anchor.X-pixel.X*Zoom,anchor.Y-pixel.Y*Zoom); InvalidateVisual();
        ViewStateChanged?.Invoke(this,EventArgs.Empty);
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomAt(Zoom*Math.Pow(1.15,e.Delta/120.0),e.GetPosition(this)); e.Handled=true;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            Focus(); panStart=e.GetPosition(this); panOffset=Offset; panning=true; fitted=false; CaptureMouse(); e.Handled=true;
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(panning) {Offset=panOffset+(e.GetPosition(this)-panStart);InvalidateVisual();e.Handled=true;}
        base.OnMouseMove(e);
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if(panning) {panning=false;ReleaseMouseCapture();e.Handled=true;}
        base.OnMouseUp(e);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) {panning=false;base.OnLostMouseCapture(e);}
    protected Brush Token(string key) => TryFindResource(key) as Brush ?? SystemColors.ControlTextBrush;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Token("Brush.Background.App"),null,new Rect(RenderSize));
        if(Source==null)
        {
            DrawText(dc,"NO IMAGE",new Point(Math.Max(8,ActualWidth/2-42),Math.Max(8,ActualHeight/2-12)),Token("Brush.Text.Muted"),14);
            return;
        }
        dc.DrawImage(Source,new Rect(Offset.X,Offset.Y,Source.PixelWidth*Zoom,Source.PixelHeight*Zoom));
        if(ShowOverlays && DisplayRoi is { } roi) DrawRoi(dc,roi,Token("Brush.Brand.Primary"),false);
        if(ShowOverlays && Regions!=null)
        {
            var color=OverlayBrush??Token("Brush.Status.Info");
            for(var i=0;i<Regions.Length;i++)
            {
                DrawRoi(dc,new(RoiShape.Polygon,Regions[i].Box),color,false);
                if(Regions[i].Box.Length>0) DrawText(dc,$"OCR {i+1}",ImageToView(Regions[i].Box[0]),color,12);
            }
        }
    }
    protected void DrawRoi(DrawingContext dc, SearchRoi roi, Brush brush, bool handles)
    {
        if(roi.Points.Length<2)return;
        var pen=new Pen(brush,1.5);
        if(roi.Shape==RoiShape.Circle)
        {
            var b=roi.Bounds;
            dc.DrawEllipse(null,pen,ImageToView(roi.Points[0]),b.Width/2*Zoom,b.Height/2*Zoom);
        }
        else if(roi.Shape==RoiShape.Rectangle)
        {
            var b=roi.Bounds; dc.DrawRectangle(null,pen,new Rect(ImageToView(new(b.X,b.Y)),new Size(b.Width*Zoom,b.Height*Zoom)));
        }
        else
        {
            var geometry=new StreamGeometry();
            using(var context=geometry.Open())
            {
                context.BeginFigure(ImageToView(roi.Points[0]),false,true);
                context.PolyLineTo(roi.Points.Skip(1).Select(ImageToView).ToArray(),true,false);
            }
            dc.DrawGeometry(null,pen,geometry);
        }
        if(handles) foreach(var p in roi.Points)
            dc.DrawRectangle(Token("Brush.Background.App"),pen,new Rect(ImageToView(p)-new Vector(4,4),new Size(8,8)));
    }
    protected void DrawText(DrawingContext dc,string text,Point p,Brush brush,double size,bool backdrop=false)
    {
        var formatted=new FormattedText(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
            new Typeface(TryFindResource("Font.Primary") as FontFamily ?? SystemFonts.MessageFontFamily,
                FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if(backdrop) dc.DrawRectangle(Token("Brush.Background.Surface"),null,new Rect(p-new Vector(3,1),new Size(formatted.Width+6,formatted.Height+2)));
        dc.DrawText(formatted,p);
    }
}
