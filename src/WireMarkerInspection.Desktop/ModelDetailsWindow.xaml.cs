using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using WireMarkerInspection.Desktop.ViewModels;
namespace WireMarkerInspection.Desktop;
public partial class ModelDetailsWindow : Window
{
    private readonly Func<ModelIdentity,string?> validate;
    private readonly ModelDetailsViewModel model;
    public ModelIdentity Identity=>new(model.Code.Trim(),model.Name.Trim());
    public ModelDetailsWindow(string title,string code,string name,Func<ModelIdentity,string?> validate)
    {
        InitializeComponent();Title=title;this.validate=validate;
        model=new(code,name);DataContext=model;
    }
    private void Done(object sender,RoutedEventArgs e)
    {
        model.Error=validate(Identity)??"";
        if(model.Error.Length!=0)return;
        DialogResult=true;
    }
}
public partial class ModelDetailsViewModel(string code,string name):ObservableObject
{
    [ObservableProperty]private string code=code;
    [ObservableProperty]private string name=name;
    [ObservableProperty]private string error="";
}
