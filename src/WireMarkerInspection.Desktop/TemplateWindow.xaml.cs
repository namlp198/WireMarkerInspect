using System.Windows;
using System.Windows.Data;
using WireMarkerInspection.Controls;
using WireMarkerInspection.Desktop.ViewModels;

namespace WireMarkerInspection.Desktop;
public partial class TemplateWindow : Window
{
    public TemplateWindow()=>InitializeComponent();
    private void Apply(object sender,RoutedEventArgs e)
    {
        if(DataContext is not TemplateEditorViewModel vm||vm.Busy)return;
        try{vm.Build();DialogResult=true;}catch(Exception ex){vm.Message=ex.Message;}
    }
    private void Expand(object? sender,EventArgs e)
    {
        if(sender is not ImageHud original)return;
        var viewer=new ImageEditor();var learn=ReferenceEquals(original,LearnHud);
        viewer.SetBinding(ImageViewer.SourceProperty,new Binding(learn?"TemplateImage":"TestImage"));
        viewer.SetBinding(ImageViewer.RoiProperty,new Binding(learn?"LearnRoi":"SearchRoi"){Mode=BindingMode.TwoWay});
        var hud=new ImageHud{Viewer=viewer,ShowCaption=false,DataContext=DataContext};
        new Window{Owner=this,Title=Title,Width=1200,Height=850,Content=hud,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();
        if(original.Viewer is ImageEditor editor)editor.ResetHistory();
    }
}
