using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WireMarkerInspection.Controls;

/// <summary>Reusable RoboStation HUD chrome. Only viewport/geometry actions belong here.</summary>
public partial class ImageHud : UserControl
{
    public static readonly DependencyProperty ViewerProperty = DependencyProperty.Register(nameof(Viewer),typeof(ImageViewer),typeof(ImageHud),
        new PropertyMetadata(null,(d,e)=>((ImageHud)d).Attach((ImageViewer?)e.OldValue,(ImageViewer?)e.NewValue)));
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption),typeof(string),typeof(ImageHud),new PropertyMetadata("IMAGE · READ ONLY"));
    public static readonly DependencyProperty CanExpandProperty = DependencyProperty.Register(nameof(CanExpand),typeof(bool),typeof(ImageHud),
        new PropertyMetadata(false,(d,_)=>((ImageHud)d).Refresh()));
    public static readonly DependencyProperty ShowCaptionProperty = DependencyProperty.Register(nameof(ShowCaption),typeof(bool),typeof(ImageHud),
        new PropertyMetadata(true,(d,_)=>((ImageHud)d).Refresh()));
    public ImageViewer? Viewer {get=>(ImageViewer?)GetValue(ViewerProperty);set=>SetValue(ViewerProperty,value);}
    public string Caption {get=>(string)GetValue(CaptionProperty);set=>SetValue(CaptionProperty,value);}
    public bool CanExpand {get=>(bool)GetValue(CanExpandProperty);set=>SetValue(CanExpandProperty,value);}
    public bool ShowCaption {get=>(bool)GetValue(ShowCaptionProperty);set=>SetValue(ShowCaptionProperty,value);}
    public event EventHandler? ExpandRequested;
    private ImageEditor? Editor=>Viewer as ImageEditor;

    public ImageHud(){InitializeComponent();Loaded+=(_,_)=>Refresh();}
    private void Attach(ImageViewer? oldView,ImageViewer? newView)
    {
        if(oldView!=null)oldView.ViewStateChanged-=OnStateChanged;
        if(oldView is ImageEditor oldEditor)oldEditor.EditStateChanged-=OnStateChanged;
        if(newView!=null)newView.ViewStateChanged+=OnStateChanged;
        if(newView is ImageEditor newEditor)newEditor.EditStateChanged+=OnStateChanged;
        CaptionPanel.SetResourceReference(MarginProperty,newView is ImageEditor?"Hud.EditorCaptionInset":"Hud.Inset");
        Refresh();
    }
    private void OnStateChanged(object? sender,EventArgs e)=>Refresh();
    private void Refresh()
    {
        if(ImageInfo==null)return;
        var source=Viewer?.Source;
        ImageInfo.Text=source==null?"NO IMAGE":$"{source.PixelWidth} × {source.PixelHeight}  ·  {Viewer!.Zoom:P0}";
        NavigationButtons.IsEnabled=source!=null;
        CaptionPanel.Visibility=ShowCaption?Visibility.Visible:Visibility.Collapsed;
        DrawingRail.Visibility=Editor==null?Visibility.Collapsed:Visibility.Visible;
        DrawingRail.IsEnabled=source!=null;
        ExpandPanel.Visibility=CanExpand?Visibility.Visible:Visibility.Collapsed;
        PolygonStrip.Visibility=Editor?.IsDrawingPolygon==true?Visibility.Visible:Visibility.Collapsed;
        FinishButton.IsEnabled=Editor?.CanFinishPolygon==true;
        UndoButton.IsEnabled=Editor?.CanUndo==true || Editor?.IsDrawingPolygon==true;
        RedoButton.IsEnabled=Editor?.CanRedo==true && Editor?.IsDrawingPolygon!=true;
        DeleteButton.IsEnabled=Editor?.Roi!=null && Editor?.IsDrawingPolygon!=true;
        foreach(var button in new[]{SelectButton,RectangleButton,CircleButton,PolygonButton,PanButton})
            button.IsChecked=Editor!=null && button.Uid==Editor.Tool.ToString();
    }
    private void SelectTool(object sender,RoutedEventArgs e)
    {
        if(Editor==null)return;
        Editor.Tool=Enum.Parse<EditorTool>(((ToggleButton)sender).Uid);Editor.Focus();
    }
    private void Undo(object s,RoutedEventArgs e){if(Editor?.IsDrawingPolygon==true)Editor.UndoPoint();else Editor?.Undo();Viewer?.Focus();}
    private void Redo(object s,RoutedEventArgs e){Editor?.Redo();Viewer?.Focus();}
    private void Delete(object s,RoutedEventArgs e){Editor?.DeleteRoi();Viewer?.Focus();}
    private void Finish(object s,RoutedEventArgs e){Editor?.Finish();Viewer?.Focus();}
    private void UndoPoint(object s,RoutedEventArgs e){Editor?.UndoPoint();Viewer?.Focus();}
    private void Cancel(object s,RoutedEventArgs e){Editor?.Cancel();Viewer?.Focus();}
    private void ZoomOut(object s,RoutedEventArgs e)=>Viewer?.ZoomBy(0.8);
    private void ZoomIn(object s,RoutedEventArgs e)=>Viewer?.ZoomBy(1.25);
    private void ResetView(object s,RoutedEventArgs e)=>Viewer?.Fit();
    private void Expand(object s,RoutedEventArgs e)=>ExpandRequested?.Invoke(this,EventArgs.Empty);
}
