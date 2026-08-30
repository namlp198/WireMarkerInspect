using System.IO;
using WireMarkerInspection.Application;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using Xunit;

namespace WireMarkerInspection.Tests;

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
        var camera=new FakeCamera([device],TimeSpan.FromMilliseconds(120));
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,TimeSpan.FromMilliseconds(20));
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

            await Task.Delay(150);
            await vm.InitializeCameraAsync();
            Assert.Equal(CameraUiState.Found,vm.CameraState);
            Assert.Equal(device,vm.SelectedCamera);
            Assert.True(vm.CanConnectCamera);
        }
        finally{await vm.ShutdownAsync();}
    });

    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    private sealed class FakeCamera(IReadOnlyList<CameraDevice> devices,TimeSpan? delay=null):ICamera
    {
        public int OpenCount{get;private set;}
        public int CloseCount{get;private set;}
        public IReadOnlyList<CameraDevice> Enumerate()
        {
            if(delay is { } wait)Thread.Sleep(wait);
            return devices;
        }
        public void Open(CameraDevice device)=>OpenCount++;
        public void SetParameter(string name,string value){}
        public void Start(){}
        public ImageFrame Grab(int timeoutMs)=>throw new TimeoutException();
        public void Stop(){}
        public void Close()=>CloseCount++;
        public void Dispose(){}
    }
}
