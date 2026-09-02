using System.IO;
using System.Text.Json;
using WireMarkerInspection.Application;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Infrastructure;
using Xunit;

namespace WireMarkerInspection.Tests;

[Collection(DispatcherTestHost.Collection)]
public sealed class CameraSettingsTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-camera-settings-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public void RecipesSavedBeforeCameraSettingsExistedStillLoad()
    {
        // A recipe.json written by the previous build has no "Camera" property at all.
        var legacy="""
        {
          "Id": "8f14e45f-ea2b-4c3a-9a1b-0c8d6d0d1a11",
          "ModelCode": "LEGACY-1",
          "Name": "Legacy model",
          "Revision": 3,
          "Ends": [
            {"ReferenceImage":"end1.png","Width":100,"Height":100,
             "Roi":{"Shape":0,"Points":[{"X":1,"Y":1},{"X":60,"Y":60}]},
             "ExpectedLines":["QK1.11"],"Orientation":0},
            {"ReferenceImage":"end2.png","Width":100,"Height":100,
             "Roi":{"Shape":0,"Points":[{"X":1,"Y":1},{"X":60,"Y":60}]},
             "ExpectedLines":["FT3.F"],"Orientation":0}
          ],
          "SavedAt": "2026-08-01T00:00:00+00:00",
          "SchemaVersion": 1
        }
        """;
        var recipe=JsonSerializer.Deserialize<Recipe>(legacy,JsonFiles.Options);

        Assert.NotNull(recipe);
        Assert.Null(recipe!.Camera);      // no acquisition setup recorded
        Assert.Null(recipe.Validate());   // and that is still a valid recipe
    }

    [Fact]
    public void SavedRecipeKeepsTheTaughtAcquisitionSetup()=>DispatcherTestHost.Sta(()=>
    {
        var camera=new SettingsCamera();
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false){Confirm=_=>true}.AsAdmin();
        try
        {
            vm.NewModelCommand.Execute(new ModelIdentity("CAM-1","Camera settings"));
            ConfigureBothEnds(vm);
            vm.Exposure="12500";vm.Gain="2.5";
            vm.GammaEnabled=true;vm.Gamma="0.7";
            vm.SensorRoiEnabled=true;vm.SensorOffsetX="8";vm.SensorOffsetY="16";vm.SensorWidth="640";vm.SensorHeight="480";
            Assert.True(vm.Dirty);        // camera values are part of the recipe
            vm.SaveRecipeCommand.Execute(null);

            var stored=Assert.Single(new FileRecipeStore(root).LoadAll());
            var settings=Assert.IsType<CameraSettings>(stored.Camera);
            Assert.Equal(12500,settings.ExposureTimeUs);
            Assert.Equal(2.5,settings.Gain);
            Assert.Equal(0.7,settings.Gamma);
            Assert.Equal(new SensorRoi(8,16,640,480),settings.Roi);
            Assert.Null(settings.BlackLevel);
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    [Fact]
    public void SavedRecipeKeepsItsOwnTriggerAndVerdictOutputs()=>DispatcherTestHost.Sta(()=>
    {
        var vm=new MainViewModel(root,new SettingsCamera(),autoDiscoverCameraOnLoad:false){Confirm=_=>true}.AsAdmin();
        try
        {
            vm.NewModelCommand.Execute(new ModelIdentity("IO-1","Recipe IO"));ConfigureBothEnds(vm);
            vm.TriggerKind=TriggerKind.Plc;vm.TriggerMapping=TriggerMapping.PerEnd;
            vm.PlcEnd1Address="X2";vm.PlcEnd2Address="X3";vm.PlcPollMs="35";
            vm.OkOutputEnabled=true;vm.OkOutputMode=PlcOutputMode.Bit;vm.OkOutputDevice="Y";
            vm.OkOutputIndex="10";vm.OkOutputPulseMs="80";
            vm.NgOutputEnabled=true;vm.NgOutputMode=PlcOutputMode.Register;vm.NgOutputDevice="D";
            vm.NgOutputIndex="120";vm.NgOutputValue="-7";
            vm.SaveRecipeCommand.Execute(null);

            var stored=Assert.Single(new FileRecipeStore(root).LoadAll());
            Assert.Equal(2,stored.SchemaVersion);
            var io=Assert.IsType<CameraInspectionIo>(stored.Io);
            Assert.Equal(RecipeTriggerKind.Plc,io.TriggerProfile.Kind);
            Assert.Equal(RecipeTriggerMapping.PerEnd,io.TriggerProfile.Mapping);
            Assert.Equal("X2",io.TriggerProfile.End1Address);Assert.Equal("X3",io.TriggerProfile.End2Address);
            Assert.Equal(35,io.TriggerProfile.PollMs);
            Assert.Equal("Y10",io.VerdictOutputs.OkAction.Address);Assert.Equal(80,io.VerdictOutputs.OkAction.PulseMs);
            Assert.Equal("D120",io.VerdictOutputs.NgAction.Address);Assert.Equal((short)-7,io.VerdictOutputs.NgAction.RegisterValue);
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    [Fact]
    public void OpeningAModelRestoresItsSetupAndPushesItToAConnectedCamera()=>DispatcherTestHost.Sta(()=>
    {
        var camera=new SettingsCamera();
        var author=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false){Confirm=_=>true}.AsAdmin();
        try
        {
            author.NewModelCommand.Execute(new ModelIdentity("CAM-2","Restore setup"));
            ConfigureBothEnds(author);
            author.Exposure="9000";author.Gain="1.5";
            author.SaveRecipeCommand.Execute(null);
        }
        finally{DispatcherTestHost.Wait(author.ShutdownAsync());}

        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false){Confirm=_=>true}.AsAdmin();
        try
        {
            DispatcherTestHost.Wait(vm.InitializeCameraAsync());
            DispatcherTestHost.Wait(vm.ConnectCommand.ExecuteAsync(null));
            Assert.True(vm.CameraConnected);
            Assert.Contains("FAKE-MODEL",vm.CameraInfo);
            Assert.Contains("1000000",vm.ExposureRange);   // limits come from the device

            vm.SelectedModel=Assert.Single(vm.Models);

            Assert.Equal("9000",vm.Exposure);
            Assert.Equal("1.5",vm.Gain);
            Assert.False(vm.Dirty);                        // restoring a saved setup is not an edit
            var applied=Assert.IsType<CameraSettings>(camera.Applied);
            Assert.Equal(9000,applied.ExposureTimeUs);
            Assert.Equal(1.5,applied.Gain);
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    [Fact]
    public void ValuesOutsideTheDeviceRangeAreRejectedBeforeReachingTheCamera()=>DispatcherTestHost.Sta(()=>
    {
        var camera=new SettingsCamera();
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false){Confirm=_=>true}.AsAdmin();
        try
        {
            DispatcherTestHost.Wait(vm.InitializeCameraAsync());
            DispatcherTestHost.Wait(vm.ConnectCommand.ExecuteAsync(null));

            vm.Exposure="9999999";        // above the reported maximum
            DispatcherTestHost.Wait(vm.CameraParametersCommand.ExecuteAsync(null));
            Assert.Contains("ExposureTime",vm.Message);
            Assert.Null(camera.Applied);

            vm.Exposure="abc";
            DispatcherTestHost.Wait(vm.CameraParametersCommand.ExecuteAsync(null));
            Assert.Contains("Exposure",vm.Message);
            Assert.Null(camera.Applied);

            vm.Exposure="15000";vm.Gain="3";
            DispatcherTestHost.Wait(vm.CameraParametersCommand.ExecuteAsync(null));
            var applied=Assert.IsType<CameraSettings>(camera.Applied);
            Assert.Equal(15000,applied.ExposureTimeUs);
            Assert.Equal(3,applied.Gain);
        }
        finally{DispatcherTestHost.Wait(vm.ShutdownAsync());}
    });

    [Fact]
    public void CameraSettingsRejectImpossibleValues()
    {
        Assert.NotNull(new CameraSettings(0,0).Validate());
        Assert.NotNull(new CameraSettings(1000,-1).Validate());
        Assert.NotNull(new CameraSettings(1000,0,Gamma:0).Validate());
        Assert.NotNull(new CameraSettings(1000,0,Roi:new SensorRoi(0,0,0,10)).Validate());
        Assert.NotNull(new CameraSettings(1000,0,Roi:new SensorRoi(-1,0,10,10)).Validate());
        Assert.Null(new CameraSettings(1000,0,0.7,2,new SensorRoi(0,0,64,48),new StrobeSettings(true,0,100,0)).Validate());
    }

    private static void ConfigureBothEnds(MainViewModel vm)
    {
        foreach(var editor in new[]{vm.End1,vm.End2})
        {
            editor.SetFrame(InspectionTests.Frame());
            editor.Roi=SearchRoi.FullImage(100,100);
            editor.ExpectedText="QK1.11/FT3.f";
            editor.Orientation=TextOrientation.Auto;
            editor.Apply();
        }
    }

    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    private sealed class SettingsCamera:ICamera
    {
        public CameraSettings? Applied{get;private set;}
        public IReadOnlyList<CameraDevice> Enumerate()=>[new("settings-fake","Fake settings camera","test",false)];
        public void Open(CameraDevice device){}
        public CameraInfo ReadInfo()=>new("FAKE-MODEL","SN-1234","Mono8",1280,1024,25,38.5);
        public IReadOnlyList<CameraParameterInfo> DescribeParameters()=>
        [
            new("ExposureTime","us",10,1000000,0,10000,true),
            new("Gain","dB",0,20,0,0,true),
            new("Gamma",string.Empty,0.1,4,0,1,true),
            new("Width","px",8,1280,8,1280,true),
            new("Height","px",8,1024,8,1024,true),
            new("OffsetX","px",0,1272,8,0,true),
            new("OffsetY","px",0,1016,8,0,true)
        ];
        public CameraSettings ReadSettings()=>Applied??new(10000,0);
        public void ApplySettings(CameraSettings settings)=>Applied=settings;
        public void ConfigureTrigger(CameraTrigger trigger)
        {
            if(trigger.IsTriggered)throw new NotSupportedException("This fake only runs free-run.");
        }
        public void ExecuteSoftwareTrigger()=>throw new NotSupportedException("This fake has no trigger.");
        public void Start(){}
        public ImageFrame Grab(int timeoutMs)=>throw new TimeoutException();
        public void Stop(){}
        public void Close(){}
        public void Dispose(){}
    }
}
