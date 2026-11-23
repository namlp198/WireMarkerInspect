using System.Windows;
using System.Windows.Input;
using WireMarkerInspection.Controls.Localization;

namespace WireMarkerInspection.Desktop;

public partial class LoginWindow : Window
{
    private readonly Func<string,string,bool> authenticate;
    public LoginWindow(Func<string,string,bool> authenticate)
    {
        InitializeComponent();this.authenticate=authenticate;
        Loaded+=(_,_)=>UsernameInput.Focus();
    }
    private void Login(object sender,RoutedEventArgs e)=>TryLogin();
    private void PasswordKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter)TryLogin();}
    private void TryLogin()
    {
        if(authenticate(UsernameInput.Text,PasswordInput.Password)){DialogResult=true;return;}
        PasswordInput.Clear();ErrorText.Text=AppLocalizer.Text("InvalidCredentials");PasswordInput.Focus();
    }
}
