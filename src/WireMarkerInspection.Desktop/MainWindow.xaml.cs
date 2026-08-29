using System.ComponentModel;
using System.Windows;
using WireMarkerInspection.Desktop.ViewModels;
namespace WireMarkerInspection.Desktop;
public partial class MainWindow : Window
{
    private bool closing;
    public MainViewModel Model{get;}
    public MainWindow(MainViewModel model)
    {
        InitializeComponent();Model=model;DataContext=model;
        model.Confirm=message=>MessageBox.Show(this,message,"Wire Marker Inspection",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
        Closing+=OnClosing;
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
