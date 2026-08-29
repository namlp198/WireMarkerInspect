using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Controls;
public enum EditorTool { Select, Rectangle, Circle, Polygon, Pan }

public sealed class ImageEditor : ImageViewer
{
    private readonly Stack<SearchRoi?> undo=[];
    private readonly Stack<SearchRoi?> redo=[];
    private readonly List<PixelPoint> polygon=[];
    private SearchRoi? draft;
    private SearchRoi? before;
    private PixelPoint start;
    private int handle=-1;
    private bool dragging;
    private bool moving;
    private EditorTool tool=EditorTool.Select;
    public EditorTool Tool
    {
        get=>tool;
        set
        {
            Cancel();tool=value;ShowOverlays=true;
            Cursor=value==EditorTool.Pan?Cursors.Hand:value==EditorTool.Select?Cursors.Arrow:Cursors.Cross;
            EditStateChanged?.Invoke(this,EventArgs.Empty);InvalidateVisual();
        }
    }
    public string? ValidationMessage { get; private set; }
    public event EventHandler? EditStateChanged;
    public bool CanUndo => undo.Count>0;
    public bool CanRedo => redo.Count>0;
    public bool IsDrawingPolygon => polygon.Count>0;
    public bool CanFinishPolygon => polygon.Count>=3;
    protected override SearchRoi? DisplayRoi => draft??Roi;
    protected override void OnSourceChanged() {ResetHistory();base.OnSourceChanged();}
    public void ResetHistory() {undo.Clear();redo.Clear();Cancel();}
    public void Undo() {Cancel();if(undo.Count==0)return;redo.Push(Roi?.Copy());SetCurrentValue(RoiProperty,undo.Pop());EditStateChanged?.Invoke(this,EventArgs.Empty);}
    public void Redo() {Cancel();if(redo.Count==0)return;undo.Push(Roi?.Copy());SetCurrentValue(RoiProperty,redo.Pop());EditStateChanged?.Invoke(this,EventArgs.Empty);}
    public void DeleteRoi() {Cancel();Commit(null);}
    public void FullImage() {if(Source!=null)Commit(SearchRoi.FullImage(Source.PixelWidth,Source.PixelHeight));}
    public void Cancel()
    {
        draft=null; polygon.Clear();dragging=false;moving=false;handle=-1;
        if(IsMouseCaptured)ReleaseMouseCapture();InvalidateVisual();
        ValidationMessage=null;EditStateChanged?.Invoke(this,EventArgs.Empty);
    }
    public void UndoPoint() {if(polygon.Count>0)polygon.RemoveAt(polygon.Count-1);draft=polygon.Count>1?new(RoiShape.Polygon,[..polygon]):null;ValidationMessage=null;EditStateChanged?.Invoke(this,EventArgs.Empty);InvalidateVisual();}
    public void Finish()
    {
        if(polygon.Count<3) {ValidationMessage="Polygon needs at least 3 points.";EditStateChanged?.Invoke(this,EventArgs.Empty);return;}
        if(Commit(new(RoiShape.Polygon,[..polygon]))) Tool=EditorTool.Select;
    }
    private bool Commit(SearchRoi? value)
    {
        ValidationMessage=value?.Validate(Source?.PixelWidth??0,Source?.PixelHeight??0);
        if(ValidationMessage!=null) {EditStateChanged?.Invoke(this,EventArgs.Empty);return false;}
        undo.Push(Roi?.Copy());redo.Clear();SetCurrentValue(RoiProperty,value?.Copy());
        EditStateChanged?.Invoke(this,EventArgs.Empty);InvalidateVisual();return true;
    }
    private PixelPoint At(MouseEventArgs e)
    {
        var p=ViewToImage(e.GetPosition(this));
        return new(Math.Clamp(p.X,0,Source!.PixelWidth),Math.Clamp(p.Y,0,Source.PixelHeight));
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if(Source==null)return;
        if(Tool==EditorTool.Pan || Keyboard.IsKeyDown(Key.Space) || e.ChangedButton==MouseButton.Middle)
        {base.OnMouseDown(e);return;}
        if(e.ChangedButton!=MouseButton.Left)return;
        if(!ShowOverlays){base.OnMouseDown(e);return;}
        Focus();var p=At(e);ValidationMessage=null;
        if(Tool==EditorTool.Polygon)
        {
            if(e.ClickCount==2) {Finish();e.Handled=true;return;}
            if(polygon.Count==0 || polygon[^1]!=p)polygon.Add(p);
            draft=polygon.Count>1?new(RoiShape.Polygon,[..polygon]):null;EditStateChanged?.Invoke(this,EventArgs.Empty);InvalidateVisual();e.Handled=true;return;
        }
        before=Roi?.Copy();start=p;handle=-1;moving=false;
        if(Tool==EditorTool.Select)
        {
            if(Roi==null){base.OnMouseDown(e);return;}
            for(var i=0;i<Roi.Points.Length;i++)
                if((ImageToView(Roi.Points[i])-e.GetPosition(this)).Length<=9){handle=i;break;}
            var b=Roi.Bounds;
            moving=handle<0 && p.X>=b.X && p.Y>=b.Y && p.X<=b.X+b.Width && p.Y<=b.Y+b.Height;
            if(handle<0&&!moving){base.OnMouseDown(e);return;}
            draft=before?.Copy();
        }
        else draft=new(Tool==EditorTool.Circle?RoiShape.Circle:RoiShape.Rectangle,[p,p]);
        dragging=true;CaptureMouse();e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(Source==null)return;
        if(IsPanning){base.OnMouseMove(e);return;}
        if(dragging)
        {
            var p=At(e);
            if(moving && before!=null)
            {
                var b=before.Bounds;
                double dx=Math.Clamp(p.X-start.X,-b.X,Source.PixelWidth-b.X-b.Width);
                double dy=Math.Clamp(p.Y-start.Y,-b.Y,Source.PixelHeight-b.Y-b.Height);
                draft=before with {Points=before.Points.Select(q=>new PixelPoint(q.X+dx,q.Y+dy)).ToArray()};
            }
            else if(handle>=0 && before!=null) {var pts=(PixelPoint[])before.Points.Clone();pts[handle]=p;draft=before with {Points=pts};}
            else if(draft!=null) draft=draft with {Points=[start,p]};
            InvalidateVisual();e.Handled=true;
        }
        else if(Tool==EditorTool.Polygon && polygon.Count>0)
        {
            draft=new(RoiShape.Polygon,[..polygon,At(e)]);InvalidateVisual();
        }
        else base.OnMouseMove(e);
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if(dragging)
        {
            var candidate=draft;dragging=false;ReleaseMouseCapture();
            if(candidate!=null)Commit(candidate);
            draft=null;tool=EditorTool.Select;Cursor=Cursors.Arrow;EditStateChanged?.Invoke(this,EventArgs.Empty);InvalidateVisual();e.Handled=true;
        }
        else base.OnMouseUp(e);
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if(dragging){dragging=false;draft=null;InvalidateVisual();}
        base.OnLostMouseCapture(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.Escape)Cancel();
        else if(e.Key==Key.Enter && polygon.Count>0)Finish();
        else if(e.Key==Key.Back && polygon.Count>0)UndoPoint();
        else if(e.Key==Key.Delete)DeleteRoi();
        else if(e.Key==Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {if(IsDrawingPolygon)UndoPoint();else Undo();}
        else if(e.Key==Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))Redo();
        else {base.OnKeyDown(e);return;}
        e.Handled=true;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(ShowOverlays && DisplayRoi is { } roi)DrawRoi(dc,roi,Token("Brush.Brand.Primary"),true);
    }
}
