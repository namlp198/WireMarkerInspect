using System.Windows;
using System.Windows.Controls;
using WireMarkerInspection.Controls;
using WireMarkerInspection.Controls.Localization;
using WireMarkerInspection.Desktop.ViewModels;
namespace WireMarkerInspection.Desktop.Views;
public partial class EndEditorView : UserControl
{
    private ImageEditor Editor=>(ImageEditor)EditorHud.Viewer!;
    public EndEditorView()
    {
        InitializeComponent();
        DataContextChanged+=(_,_)=>Editor.ResetHistory();
    }
    private void Expand(object? s,EventArgs e)
    {
        var editor=new EndEditorView{DataContext=DataContext,Tag=Tag,Margin=new Thickness(16)};
        editor.EditorHud.CanExpand=false;
        var window=new Window{Title=AppLocalizer.Text("RecipeImageEditorTitle"),Width=1250,Height=900,MinWidth=800,MinHeight=650,
            Owner=Window.GetWindow(this),Content=editor,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        window.ShowDialog();Editor.ResetHistory();
    }
    private void SetupTemplate(object sender,RoutedEventArgs e)
    {
        if(Tag is not MainViewModel main||!main.CanConfigureModel||DataContext is not EndEditorViewModel end)return;
        using var draft=new TemplateEditorViewModel(end,main.Matcher);
        var window=new TemplateWindow{Owner=Window.GetWindow(this),DataContext=draft};
        if(window.ShowDialog()==true&&main.CanConfigureModel)end.SetTerminal(draft.Build());
    }
}
