using System.Windows;
using System.Windows.Controls;
using WireMarkerInspection.Controls;
using WireMarkerInspection.Controls.Localization;
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
}
