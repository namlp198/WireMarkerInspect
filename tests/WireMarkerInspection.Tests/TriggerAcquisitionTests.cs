using System.IO;
using WireMarkerInspection.Application;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using Xunit;

namespace WireMarkerInspection.Tests;

/// <summary>
/// Switching between free-run and a triggered source is an acquisition lifecycle change, not a setting.
/// It is the highest regression risk in the trigger work, so it is exercised against a camera that really
/// stays silent until it is pulsed.
/// </summary>
[Collection(DispatcherTestHost.Collection)]
public sealed class TriggerAcquisitionTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-trigger-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public void ArmingAHardwareTriggerStopsFreeRunAndOnlyDeliversPulsedFrames()=>DispatcherTestHost.Sta(()=>
    {
        var camera=new PulsedCamera();
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,TimeSpan.FromSeconds(1));
        try
        {
            DispatcherTestHost.Wait(vm.InitializeCameraAsync());
            DispatcherTestHost.Wait(vm.ConnectCommand.ExecuteAsync(null));
            DispatcherTestHost.Wait(vm.AcquisitionCommand.ExecuteAsync(null));
            DispatcherTestHost.Pump(()=>vm.HasLiveFrame,TimeSpan.FromSeconds(10),"Free-run delivered no frame.");

            var trigger=new CameraTrigger(CameraTriggerSource.Line,2,RisingEdge:false,DelayUs:25,DebouncerUs:1000);
            DispatcherTestHost.Wait(vm.ArmTriggerAsync(new TriggerSettings(TriggerKind.CameraLine,TriggerMapping.Shared,trigger)));

            Assert.Equal(trigger,camera.Configured);
            Assert.True(camera.Restarts>=2,$"Acquisition was not restarted around the mode change ({camera.Restarts}).");
            Assert.True(vm.Acquiring);                    // live view keeps running, now triggered
            Assert.Contains("Line 2",vm.TriggerStatus);

            var before=camera.Frames;
            DispatcherTestHost.PumpFor(TimeSpan.FromMilliseconds(500));
            Assert.Equal(before,camera.Frames);           // a triggered camera stays silent without a pulse

            camera.Pulse();
            DispatcherTestHost.Pump(()=>camera.Frames>before,TimeSpan.FromSeconds(10),"The pulse produced no frame.");
            DispatcherTestHost.Pump(()=>vm.LastTrigger.Contains("RUN",StringComparison.Ordinal),TimeSpan.FromSeconds(10),
                "The pulsed frame did not reach the trigger router.");

            DispatcherTestHost.Wait(vm.DisarmTriggerAsync());

            Assert.Equal(CameraTriggerSource.FreeRun,camera.Configured!.Source);
            var resumed=camera.Frames;
            DispatcherTestHost.Pump(()=>camera.Frames>resumed,TimeSpan.FromSeconds(10),"Free-run did not resume.");
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    [Fact]
    public void AQuietTriggeredCameraIsNotTreatedAsALostConnection()=>DispatcherTestHost.Sta(()=>
    {
        var camera=new PulsedCamera();
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,TimeSpan.FromSeconds(1));
        try
        {
            DispatcherTestHost.Wait(vm.InitializeCameraAsync());
            DispatcherTestHost.Wait(vm.ConnectCommand.ExecuteAsync(null));
            DispatcherTestHost.Wait(vm.AcquisitionCommand.ExecuteAsync(null));
            DispatcherTestHost.Wait(vm.ArmTriggerAsync(new TriggerSettings(TriggerKind.CameraLine,TriggerMapping.Shared,
                new CameraTrigger(CameraTriggerSource.Line))));

            // Free-run treats four seconds of silence as a lost link; a triggered camera is silent by design.
            DispatcherTestHost.PumpFor(TimeSpan.FromSeconds(4));

            Assert.Equal(CameraUiState.Acquiring,vm.CameraState);
            Assert.Equal(0,vm.Diagnostics.Snapshot().Reconnects);
            Assert.True(vm.Acquiring);
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    private sealed class PulsedCamera:ICamera
    {
        private readonly object gate=new();
        private CameraTrigger trigger=CameraTrigger.FreeRun;
        private int pulses;
        private bool grabbing;
        public CameraTrigger? Configured{get;private set;}
        public int Restarts{get;private set;}
        public long Frames{get{lock(gate)return frames;}}
        private long frames;
        public IReadOnlyList<CameraDevice> Enumerate()=>[new("pulsed","Pulsed camera","test",false)];
        public void Open(CameraDevice device){}
        public CameraInfo ReadInfo()=>new("PULSED","SN-PULSE","Mono8",40,20,30,null);
        public IReadOnlyList<CameraParameterInfo> DescribeParameters()=>
            [new("ExposureTime","us",10,100000,0,10000,true),new("Gain","dB",0,20,0,0,true)];
        public CameraSettings ReadSettings()=>new(10000,0);
        public void ApplySettings(CameraSettings settings){}
        public void ConfigureTrigger(CameraTrigger value)
        {
            lock(gate)
            {
                if(grabbing)throw new InvalidOperationException("Stop acquisition before changing the trigger.");
                trigger=value;Configured=value;pulses=0;
            }
        }
        public void ExecuteSoftwareTrigger(){lock(gate)pulses++;}
        public void Pulse(){lock(gate)pulses++;}
        public void Start(){lock(gate){grabbing=true;Restarts++;}}
        public ImageFrame Grab(int timeoutMs)
        {
            lock(gate)
            {
                if(!grabbing)throw new InvalidOperationException("Acquisition has not started.");
                if(trigger.IsTriggered)
                {
                    if(pulses==0)throw new TimeoutException("No trigger pulse.");
                    pulses--;
                }
                frames++;
                var stride=40*3;
                return new(40,20,stride,new byte[stride*20],Guid.NewGuid(),DateTimeOffset.UtcNow,"PULSED");
            }
        }
        public void Stop(){lock(gate)grabbing=false;}
        public void Close(){}
        public void Dispose(){}
    }
}
