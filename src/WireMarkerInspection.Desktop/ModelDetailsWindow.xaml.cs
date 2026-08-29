using System.Windows;
namespace WireMarkerInspection.Desktop;
public partial class ModelDetailsWindow : Window
{
    public ModelDetailsWindow(){InitializeComponent();}
    private void Done(object sender,RoutedEventArgs e){DialogResult=true;Close();}
}
