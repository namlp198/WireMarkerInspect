using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using WireMarkerInspection.Controls;
using WireMarkerInspection.Desktop.ViewModels;
namespace WireMarkerInspection.Desktop;
public partial class MainWindow : Window
{
    private bool closing;
    private Window? liveCameraWindow;
    public MainViewModel Model{get;}
    public MainWindow(MainViewModel model)
    {
        InitializeComponent();Model=model;DataContext=model;
        model.Confirm=message=>MessageBox.Show(this,message,"Wire Marker Inspection",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
        Loaded+=OnLoaded;Closing+=OnClosing;
    }
    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        Loaded-=OnLoaded;
        if(Model.AutoDiscoverCameraOnLoad)await Model.InitializeCameraAsync();
    }
    private void ExpandLiveCamera(object? sender,EventArgs e)
    {
        if(liveCameraWindow is {IsVisible:true}){liveCameraWindow.Activate();return;}
        var viewer=new ImageViewer();
        viewer.SetBinding(ImageViewer.SourceProperty,new Binding(nameof(MainViewModel.LiveImage)){Source=Model});
        var hud=new ImageHud{Caption="LIVE CAMERA · EXPANDED",Viewer=viewer,CanExpand=false,Margin=new Thickness(16)};
        liveCameraWindow=new Window
        {
            Title="Live Camera",Width=1280,Height=920,MinWidth=800,MinHeight=600,Owner=this,Content=hud,
            Background=(System.Windows.Media.Brush)FindResource("Brush.Background.App"),WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        liveCameraWindow.Closed+=(_,_)=>liveCameraWindow=null;
        liveCameraWindow.Show();
    }
    private void AddModel(object sender,RoutedEventArgs e)
    {
        if(!Model.CanEdit)return;
        var dialog=new ModelDetailsWindow("ADD MODEL","","",identity=>Model.ValidateModelIdentity(identity)){Owner=this};
        if(dialog.ShowDialog()==true)Model.NewModelCommand.Execute(dialog.Identity);
    }
    private void EditModel(object sender,RoutedEventArgs e)
    {
        var selected=Model.SelectedModel;
        if(!Model.CanEdit||selected==null)return;
        var dialog=new ModelDetailsWindow("EDIT MODEL",selected.Code,selected.Name,
            identity=>Model.ValidateModelIdentity(identity,selected.Recipe.Id)){Owner=this};
        if(dialog.ShowDialog()==true)Model.EditModelCommand.Execute(dialog.Identity);
    }
    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(closing)return;
        e.Cancel=true;
        if(Model.Dirty&&Model.Confirm?.Invoke("Thoát và bỏ thay đổi chưa lưu?")==false)return;
        closing=true;
        try{await Model.ShutdownAsync();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Shutdown");}
        Close();
    }
}
