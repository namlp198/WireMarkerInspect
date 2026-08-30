using System.IO;
using WireMarkerInspection.Application;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using Xunit;

namespace WireMarkerInspection.Tests;

[Collection(DispatcherTestHost.Collection)]
public sealed class CameraAcquisitionStateTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-camera-state-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public void DiscoveryAndConnectionExposeOnlyValidControls()=>DispatcherTestHost.Sta(async()=>
    {
        var device=new CameraDevice("camera-1","MV-CE120-10GM · TEST","hikrobot-mvs-gige",false);
        var camera=new FakeCamera([device]);
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,TimeSpan.FromSeconds(1));
        try
        {
            Assert.True(vm.CanSearchCamera);
            Assert.False(vm.CanConnectCamera);
            Assert.False(vm.CanDisconnectCamera);
            Assert.False(vm.CanToggleAcquisition);
            Assert.False(vm.CanEditCameraParameters);

            await vm.InitializeCameraAsync();

            Assert.Equal(CameraUiState.Found,vm.CameraState);
            Assert.Equal(device,vm.SelectedCamera);
            Assert.True(vm.CanSearchCamera);
            Assert.True(vm.CanSelectCamera);
            Assert.True(vm.CanConnectCamera);
            Assert.False(vm.CanDisconnectCamera);
            Assert.False(vm.CanToggleAcquisition);

            await vm.ConnectCommand.ExecuteAsync(null);

            Assert.Equal(CameraUiState.Connected,vm.CameraState);
            Assert.True(vm.CameraConnected);
            Assert.False(vm.CanSearchCamera);
            Assert.False(vm.CanConnectCamera);
            Assert.True(vm.CanDisconnectCamera);
            Assert.True(vm.CanToggleAcquisition);
            Assert.True(vm.CanEditCameraParameters);
            Assert.Equal(1,camera.OpenCount);

            vm.Acquiring=true;vm.CameraState=CameraUiState.Acquiring;
            Assert.Equal("Stop Acquisition",vm.AcquisitionActionLabel);
            Assert.False(vm.CanDisconnectCamera);
            Assert.True(vm.CanToggleAcquisition);
            Assert.False(vm.CanEditCameraParameters);
            vm.Acquiring=false;vm.CameraState=CameraUiState.Connected;

            await vm.DisconnectCommand.ExecuteAsync(null);

            Assert.False(vm.CameraConnected);
            Assert.Equal(CameraUiState.Found,vm.CameraState);
            Assert.True(vm.CanConnectCamera);
            Assert.Equal(1,camera.CloseCount);
        }
        finally{await vm.ShutdownAsync();}
    });

    [Fact]
    public void DiscoveryTimeoutLeavesOnlySearchAndRetryUsesCompletedResult()=>DispatcherTestHost.Sta(async()=>
    {
        var device=new CameraDevice("slow-camera","Slow camera","hikrobot-mvs-gige",false);
        using var discovery=new ManualResetEventSlim(false);
        var camera=new FakeCamera([device],discovery);
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,TimeSpan.FromMilliseconds(50));
        try
        {
            await vm.InitializeCameraAsync();
            Assert.Equal(CameraUiState.NotFound,vm.CameraState);
            Assert.True(vm.CanSearchCamera);
            Assert.False(vm.CanSelectCamera);
            Assert.False(vm.CanConnectCamera);
            Assert.False(vm.CanDisconnectCamera);
            Assert.False(vm.CanToggleAcquisition);
            Assert.False(vm.CanEditCameraParameters);

            discovery.Set();
            Assert.True(camera.Finished.Wait(TimeSpan.FromSeconds(10)),"Discovery never completed.");
            await vm.InitializeCameraAsync();
            Assert.Equal(CameraUiState.Found,vm.CameraState);
            Assert.Equal(device,vm.SelectedCamera);
            Assert.True(vm.CanConnectCamera);
        }
        finally{await vm.ShutdownAsync();}
    });

    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    private sealed class FakeCamera(IReadOnlyList<CameraDevice> devices,ManualResetEventSlim? gate=null):ICamera
    {
        public int OpenCount{get;private set;}
        public int CloseCount{get;private set;}
        /// <summary>Signals that a discovery call has returned, so a test can wait for it instead of sleeping.</summary>
        public ManualResetEventSlim Finished{get;}=new(false);
        public IReadOnlyList<CameraDevice> Enumerate()
        {
            try
            {
                if(gate!=null&&!gate.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException("Discovery gate was never released.");
                return devices;
            }
            finally{Finished.Set();}
        }
        public void Open(CameraDevice device)=>OpenCount++;
        public CameraSettings? Applied{get;private set;}
        public CameraInfo ReadInfo()=>new("FAKE-MODEL","SN-FAKE","Mono8",64,48,30,35);
        public IReadOnlyList<CameraParameterInfo> DescribeParameters()=>
        [
            new("ExposureTime","us",10,1000000,0,10000,true),
            new("Gain","dB",0,20,0,0,true),
            new("Width","px",8,64,8,64,true),
            new("Height","px",8,48,8,48,true)
        ];
        public CameraSettings ReadSettings()=>Applied??new(10000,0);
        public void ApplySettings(CameraSettings settings)=>Applied=settings;
        public void Start(){}
        public ImageFrame Grab(int timeoutMs)=>throw new TimeoutException();
        public void Stop(){}
        public void Close()=>CloseCount++;
        public void Dispose(){}
    }
}
