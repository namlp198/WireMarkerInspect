using System.IO;
using WireMarkerInspection.Application;
using WireMarkerInspection.Desktop.Services;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Infrastructure;
using Xunit;

namespace WireMarkerInspection.Tests;

[Collection(DispatcherTestHost.Collection)]
public sealed class RunLifecycleTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-run-lifecycle-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public void RunOwnsCameraAcquisitionAndPlcConnection()=>DispatcherTestHost.Sta(() =>
    {
        var camera=new LifecycleCamera();var plc=new LifecyclePlcLink();
        SeedRecipeAndMachineSettings();
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,plcFactory:_=>plc){Confirm=_=>true};
        try
        {
            Assert.False(vm.IsAdmin);Assert.True(vm.CanSelectModel);
            vm.SelectedModel=Assert.Single(vm.Models);
            Assert.False(vm.CameraConnected);Assert.False(plc.IsConnected);

            DispatcherTestHost.Wait(vm.StartRunCommand.ExecuteAsync(null));

            Assert.True(vm.Running);Assert.True(vm.CameraConnected);Assert.True(vm.Acquiring);
            Assert.True(plc.IsConnected);Assert.Equal(1,plc.ConnectCount);
            Assert.Contains("CHỜ ĐẦU 1",vm.RunStatus);

            DispatcherTestHost.Wait(vm.SettingCommand.ExecuteAsync(null));

            Assert.False(vm.Running);Assert.False(vm.Acquiring);Assert.True(vm.CameraConnected);
            Assert.False(plc.IsConnected);Assert.False(vm.RunPage);
            Assert.True(camera.StopCount>0);Assert.Equal(1,plc.DisposeCount);
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    [Fact]
    public void SimulatorIsDefaultAndRunsSavedRecipeWithoutCameraOrPlc()=>DispatcherTestHost.Sta(() =>
    {
        var camera=new LifecycleCamera();var plcCreated=false;
        SeedRecipeAndMachineSettings();
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,plcFactory:_=>
        {
            plcCreated=true;return new LifecyclePlcLink();
        },enableSimulator:true);
        try
        {
            Assert.True(vm.IsSimulatorSelected);Assert.Equal(MainViewModel.SimulatorCamera,vm.SelectedCamera);
            DispatcherTestHost.Wait(vm.InitializeCameraAsync());
            Assert.Equal(2,vm.Cameras.Count);Assert.True(vm.IsSimulatorSelected);
            Assert.Contains(vm.Cameras,device=>!device.IsSimulation);
            vm.SelectedModel=Assert.Single(vm.Models);

            DispatcherTestHost.Wait(vm.StartRunCommand.ExecuteAsync(null));

            Assert.True(vm.Running);Assert.True(vm.IsSimulatorRun);Assert.True(vm.CanLoadRuntime);
            Assert.False(vm.CanCaptureFromCamera);Assert.False(vm.CameraConnected);Assert.False(vm.Acquiring);
            Assert.False(plcCreated);Assert.Equal(0,camera.OpenCount);Assert.Equal(0,camera.StartCount);
            Assert.Contains("SIMULATOR",vm.RunCameraStatus);Assert.Contains("SIMULATOR",vm.RunPlcStatus);

            DispatcherTestHost.Wait(vm.SettingCommand.ExecuteAsync(null));
            Assert.False(vm.Running);Assert.False(vm.IsSimulatorRun);
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    private void SeedRecipeAndMachineSettings()
    {
        var width=20;var height=20;var stride=width*3;
        var frame=new ImageFrame(width,height,stride,new byte[stride*height],Guid.NewGuid(),DateTimeOffset.UtcNow,"TEST");
        var end=new EndRecipe("pending.png",width,height,SearchRoi.FullImage(width,height),["A"]);
        var io=new CameraInspectionIo(new RecipeTriggerProfile(),
            new VerdictOutputProfile(new PlcOutputAction(true,PlcOutputMode.Bit,"M",1,PulseMs:10)));
        var recipe=new Recipe(Guid.NewGuid(),"RUN-1","Lifecycle",0,[end,end.Copy()],DateTimeOffset.UtcNow,2,
            new CameraSettings(10000,0),io);
        var bitmap=ImageFiles.Bitmap(frame);
        new FileRecipeStore(root).Save(recipe,[ImageFiles.Png(bitmap),ImageFiles.Png(bitmap)]);
        new FileSettingsStore(root).Save(new MachineSettings(new TriggerSettings(),new PlcSettings(
            Transport:PlcTransport.Com,SerialPort:"COM11",BaudRate:9600,SerialProtocol:PlcSerialProtocol.ModbusAscii,
            DataBits:7,Parity:PlcSerialParity.Even,StopBits:PlcSerialStopBits.One)));
    }

    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    private sealed class LifecycleCamera:ICamera
    {
        private volatile bool grabbing;
        public int StopCount{get;private set;}
        public int OpenCount{get;private set;}
        public int StartCount{get;private set;}
        public IReadOnlyList<CameraDevice> Enumerate()=>[new("lifecycle","Lifecycle camera","test",false)];
        public void Open(CameraDevice device)=>OpenCount++;
        public CameraInfo ReadInfo()=>new("LIFECYCLE","SN-RUN","BGR8",20,20,30,null);
        public IReadOnlyList<CameraParameterInfo> DescribeParameters()=>
            [new("ExposureTime","us",10,100000,0,10000,true),new("Gain","dB",0,20,0,0,true)];
        public CameraSettings ReadSettings()=>new(10000,0);
        public void ApplySettings(CameraSettings settings){}
        public void ConfigureTrigger(CameraTrigger trigger)
        {
            if(grabbing)throw new InvalidOperationException("Trigger changed while grabbing.");
        }
        public void ExecuteSoftwareTrigger(){}
        public void Start(){grabbing=true;StartCount++;}
        public ImageFrame Grab(int timeoutMs)
        {
            if(!grabbing)throw new InvalidOperationException("Not acquiring.");
            Thread.Sleep(5);var stride=60;
            return new(20,20,stride,new byte[stride*20],Guid.NewGuid(),DateTimeOffset.UtcNow,"LIFECYCLE");
        }
        public void Stop(){grabbing=false;StopCount++;}
        public void Close(){}
        public void Dispose(){}
    }

    private sealed class LifecyclePlcLink:IPlcLink
    {
        public bool IsConnected{get;private set;}
        public string Status=>IsConnected?"connected":"offline";
        public int ConnectCount{get;private set;}
        public int DisposeCount{get;private set;}
        public Task ConnectAsync(CancellationToken token){IsConnected=true;ConnectCount++;return Task.CompletedTask;}
        public Task DisconnectAsync(){IsConnected=false;return Task.CompletedTask;}
        public Task<bool> ReadBitAsync(string address,CancellationToken token)=>Task.FromResult(false);
        public Task WriteBitAsync(string address,bool value,CancellationToken token)=>Task.CompletedTask;
        public Task WriteWordAsync(string address,short value,CancellationToken token)=>Task.CompletedTask;
        public ValueTask DisposeAsync(){IsConnected=false;DisposeCount++;return ValueTask.CompletedTask;}
    }
}
