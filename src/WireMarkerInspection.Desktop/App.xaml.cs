using System.Windows;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Infrastructure;
namespace WireMarkerInspection.Desktop;
public partial class App : System.Windows.Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==2&&e.Args[0]=="--offline-smoke")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Smoke.Run(e.Args[1]);return;
        }
        if(e.Args.Length==4&&e.Args[0]=="--real-image-smoke")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            RealImageSmoke.Run(e.Args[1],e.Args[2],e.Args[3]);return;
        }
        instance=new Mutex(true,"Local\\WireMarkerInspection."+Environment.UserName,out var created);
        if(!created)
        {
            MessageBox.Show("Wire Marker Inspection đang chạy. Chỉ mở một phiên cho mỗi người dùng.","Wire Marker Inspection");
            instance.Dispose();instance=null;Shutdown();return;
        }
        var vm=new MainViewModel(JsonFiles.DataRoot);
        var window=new MainWindow(vm);MainWindow=window;window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if(instance!=null){instance.ReleaseMutex();instance.Dispose();}
        base.OnExit(e);
    }
}
