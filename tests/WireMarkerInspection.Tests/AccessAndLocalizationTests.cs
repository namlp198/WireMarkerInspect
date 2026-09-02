using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WireMarkerInspection.Application;
using WireMarkerInspection.Controls.Localization;
using WireMarkerInspection.Desktop.Security;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using Xunit;

namespace WireMarkerInspection.Tests;

[CollectionDefinition(Name,DisableParallelization=true)]
public sealed class LocalizationCollection
{
    public const string Name="Localization";
}

[Collection(LocalizationCollection.Name)]
public sealed class AccessAndLocalizationTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-access-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OperatorIsDefaultAndOnlyAcquisitionAndModelSelectionStayAvailable()
    {
        var camera=new PermissionCamera();var plcCreated=false;
        var vm=new MainViewModel(root,camera,autoDiscoverCameraOnLoad:false,plcFactory:_=>
        {
            plcCreated=true;throw new InvalidOperationException("PLC must not be opened by Operator.");
        });
        try
        {
            Assert.Equal(AccessLevel.Operator,vm.CurrentAccessLevel);Assert.False(vm.IsAdmin);
            Assert.True(vm.CanSearchCamera);Assert.True(vm.CanSelectModel);Assert.False(vm.CanCreateModel);
            Assert.False(vm.CanConfigurePlc);Assert.False(vm.CanConnectPlc);
            vm.NewModelCommand.Execute(new ModelIdentity("BLOCKED","Operator cannot create"));
            Assert.Equal(string.Empty,vm.ModelCode);Assert.Empty(vm.Models);
            await vm.ConnectPlcCommand.ExecuteAsync(null);Assert.False(plcCreated);

            await vm.InitializeCameraAsync();await vm.ConnectCommand.ExecuteAsync(null);
            Assert.True(vm.CameraConnected);Assert.True(vm.CanToggleAcquisition);
            Assert.False(vm.CanEditCameraParameters);
            vm.Exposure="12000";await vm.CameraParametersCommand.ExecuteAsync(null);
            Assert.Null(camera.Applied);
        }
        finally{await vm.ShutdownAsync();}
    }

    [Fact]
    public async Task AdminCredentialsAreExactAndLogoutReturnsToOperator()
    {
        var vm=new MainViewModel(root,new PermissionCamera(),autoDiscoverCameraOnLoad:false);
        try
        {
            Assert.False(vm.TryLogin("Admin","admin"));Assert.False(vm.TryLogin("admin","wrong"));Assert.False(vm.IsAdmin);
            Assert.True(vm.TryLogin("admin","admin"));Assert.True(vm.IsAdmin);Assert.True(vm.CanCreateModel);Assert.True(vm.CanConfigurePlc);
            await vm.LogoutCommand.ExecuteAsync(null);
            Assert.False(vm.IsAdmin);Assert.Equal(AccessLevel.Operator,vm.CurrentAccessLevel);Assert.False(vm.CanCreateModel);
        }
        finally{await vm.ShutdownAsync();}
    }

    [Fact]
    public void LanguagePreferenceDefaultsAndRoundTrips()
    {
        var path=Path.Combine(root,"profile","language.txt");
        Assert.Equal(AppLanguage.Vietnamese,LanguagePreferenceFile.Load(path));
        Assert.True(LanguagePreferenceFile.Save(path,AppLanguage.Korean));
        Assert.Equal(AppLanguage.Korean,LanguagePreferenceFile.Load(path));
        Assert.True(LanguagePreferenceFile.Save(path,AppLanguage.English));
        Assert.Equal(AppLanguage.English,LanguagePreferenceFile.Load(path));
    }

    [Fact]
    public void CatalogContainsEveryUsedKeyAndAllThreeLanguages()
    {
        var catalogPath=Path.Combine(AppContext.BaseDirectory,"lang.csv");
        Assert.True(File.Exists(catalogPath),$"Missing localization catalog: {catalogPath}");
        var rows=File.ReadAllLines(catalogPath,Encoding.UTF8).Where(line=>!string.IsNullOrWhiteSpace(line)).ToArray();
        Assert.Equal("key,en,vi,ko",rows[0]);
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows.Skip(1))
        {
            var cells=ParseCsv(row);Assert.Equal(4,cells.Length);Assert.All(cells,cell=>Assert.False(string.IsNullOrWhiteSpace(cell)));
            Assert.True(keys.Add(cells[0]),$"Duplicate localization key: {cells[0]}");
        }
        var sourceRoot=Path.Combine(FindRepositoryRoot(),"src");var missing=new List<string>();
        var pattern=new Regex("\\{loc:Text\\s+(?<key>[A-Za-z0-9_]+)\\s*\\}|AppLocalizer\\.(?:Text|Format)\\(\"(?<key>[A-Za-z0-9_]+)",RegexOptions.Compiled);
        foreach(var file in Directory.EnumerateFiles(sourceRoot,"*.*",SearchOption.AllDirectories).Where(file=>file.EndsWith(".cs")||file.EndsWith(".xaml")))
        {
            if(file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")||file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))continue;
            foreach(Match match in pattern.Matches(File.ReadAllText(file)))
                if(!keys.Contains(match.Groups["key"].Value))missing.Add($"{Path.GetFileName(file)}: {match.Groups["key"].Value}");
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void LanguageSwitchUpdatesCatalogWithoutRestartOrPreferenceWrite()
    {
        var original=AppLocalizer.CurrentLanguage;
        try
        {
            AppLocalizer.ChangeLanguage(AppLanguage.Vietnamese,persist:false);Assert.Equal("Đăng nhập",AppLocalizer.Text("Login"));
            AppLocalizer.ChangeLanguage(AppLanguage.English,persist:false);Assert.Equal("Login",AppLocalizer.Text("Login"));
            AppLocalizer.ChangeLanguage(AppLanguage.Korean,persist:false);Assert.Equal("로그인",AppLocalizer.Text("Login"));
            Assert.Equal("UnknownLocalizationKey",AppLocalizer.Text("UnknownLocalizationKey"));
        }
        finally{AppLocalizer.ChangeLanguage(original,persist:false);}
    }

    [Fact]
    public async Task LanguageSwitchChangesLabelsWithoutMutatingRecipeValues()
    {
        var original=AppLocalizer.CurrentLanguage;
        var vm=new MainViewModel(root,new PermissionCamera(),autoDiscoverCameraOnLoad:false).AsAdmin();
        try
        {
            vm.NewModelCommand.Execute(new ModelIdentity("LANGUAGE-STABILITY","Language stability"));
            vm.TriggerKind=TriggerKind.Plc;vm.TriggerMapping=TriggerMapping.PerEnd;
            vm.OkOutputEnabled=true;vm.OkOutputMode=PlcOutputMode.Register;vm.OkOutputDevice="D";vm.OkOutputIndex="10";vm.OkOutputValue="77";
            vm.NgOutputEnabled=true;vm.NgOutputMode=PlcOutputMode.Bit;vm.NgOutputDevice="Y";vm.NgOutputIndex="2";vm.NgOutputPulseMs="80";
            vm.End1.Orientation=TextOrientation.Degrees180;vm.End2.Orientation=TextOrientation.Auto;
            vm.Dirty=false;
            var triggerOptions=vm.TriggerKindOptions;var mappingOptions=vm.TriggerMappingOptions;
            var outputOptions=vm.PlcOutputModeOptions;var firstOrientations=vm.End1.Orientations;
            var before=vm.BuildRecipeIo();

            foreach(var language in new[]{AppLanguage.English,AppLanguage.Korean,AppLanguage.Vietnamese})
            {
                AppLocalizer.ChangeLanguage(language,persist:false);
                Assert.Same(triggerOptions,vm.TriggerKindOptions);Assert.Same(mappingOptions,vm.TriggerMappingOptions);
                Assert.Same(outputOptions,vm.PlcOutputModeOptions);Assert.Same(firstOrientations,vm.End1.Orientations);
                Assert.Equal(before,vm.BuildRecipeIo());
                Assert.Equal(TriggerMapping.PerEnd,vm.TriggerMapping);Assert.Equal(PlcOutputMode.Register,vm.OkOutputMode);
                Assert.Equal(PlcOutputMode.Bit,vm.NgOutputMode);Assert.Equal(TextOrientation.Degrees180,vm.End1.Orientation);
                Assert.Equal(TextOrientation.Auto,vm.End2.Orientation);Assert.False(vm.Dirty);
            }
        }
        finally{AppLocalizer.ChangeLanguage(original,persist:false);await vm.ShutdownAsync();}
    }

    private static string FindRepositoryRoot()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null&&!Directory.Exists(Path.Combine(directory.FullName,"src")))directory=directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
    private static string[] ParseCsv(string line)
    {
        var cells=new List<string>();var cell=new StringBuilder();var quoted=false;
        for(var index=0;index<line.Length;index++)
        {
            var value=line[index];
            if(value=='\"')
            {
                if(quoted&&index+1<line.Length&&line[index+1]=='\"'){cell.Append('\"');index++;}else quoted=!quoted;
            }
            else if(value==','&&!quoted){cells.Add(cell.ToString());cell.Clear();}else cell.Append(value);
        }
        cells.Add(cell.ToString());return [..cells];
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    private sealed class PermissionCamera : ICamera
    {
        public CameraSettings? Applied{get;private set;}
        public IReadOnlyList<CameraDevice> Enumerate()=>[new("permission","Permission camera","test",false)];
        public void Open(CameraDevice device) { }
        public CameraInfo ReadInfo()=>new("TEST","ACCESS","Mono8",64,48,30,null);
        public IReadOnlyList<CameraParameterInfo> DescribeParameters()=>[new("ExposureTime","us",10,100000,0,10000,true),new("Gain","dB",0,20,0,0,true)];
        public CameraSettings ReadSettings()=>new(10000,0);
        public void ApplySettings(CameraSettings settings)=>Applied=settings;
        public void ConfigureTrigger(CameraTrigger trigger) { }
        public void ExecuteSoftwareTrigger() { }
        public void Start() { }
        public ImageFrame Grab(int timeoutMs)=>throw new TimeoutException();
        public void Stop() { }
        public void Close() { }
        public void Dispose() { }
    }
}
