using System.IO;
using WireMarkerInspection.Application;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Infrastructure;
using Xunit;

namespace WireMarkerInspection.Tests;

public sealed class PlcTests
{
    private readonly DeltaDvpAddressMap map=new();

    [Fact]
    public void DeltaInputsAndOutputsAreNumberedInOctal()
    {
        // X10 is the ninth input on a DVP, not the tenth. Reading it as decimal would address the wrong
        // contact and still "work", which is worse than failing.
        Assert.Equal(new ModbusTarget(ModbusArea.Coil,0x0400,false),map.Translate("X0"));
        Assert.Equal(new ModbusTarget(ModbusArea.Coil,0x0407,false),map.Translate("X7"));
        Assert.Equal(new ModbusTarget(ModbusArea.Coil,0x0408,false),map.Translate("X10"));
        Assert.Equal(new ModbusTarget(ModbusArea.Coil,0x0500,true),map.Translate("Y0"));
        Assert.Equal(new ModbusTarget(ModbusArea.Coil,0x0508,true),map.Translate("Y10"));

        var octal=Assert.Throws<ArgumentException>(()=>map.Translate("X8"));
        Assert.Contains("bát phân",octal.Message);
    }

    [Fact]
    public void DeltaBitAndWordDevicesLandInTheirOwnAreas()
    {
        Assert.Equal(new ModbusTarget(ModbusArea.Coil,0x0800,true),map.Translate("M0"));
        Assert.Equal(new ModbusTarget(ModbusArea.Coil,0x0864,true),map.Translate("M100"));
        Assert.Equal(new ModbusTarget(ModbusArea.HoldingRegister,0x1000,true),map.Translate("D0"));
        Assert.Equal(new ModbusTarget(ModbusArea.HoldingRegister,0x1064,true),map.Translate("D100"));
        Assert.Equal(new ModbusTarget(ModbusArea.Coil,0x0000,true),map.Translate("S0"));
        Assert.Equal(map.Translate("m100"),map.Translate("M100"));      // case does not matter

        Assert.Throws<ArgumentException>(()=>map.Translate("Z1"));
        Assert.Throws<ArgumentException>(()=>map.Translate("M"));
        Assert.Throws<ArgumentException>(()=>map.Translate(""));
        Assert.Contains("vượt dải",Assert.Throws<ArgumentException>(()=>map.Translate("D9999")).Message);
        Assert.NotEmpty(PlcAddressMaps.Vendors);
        Assert.Throws<NotSupportedException>(()=>PlcAddressMaps.For("siemens"));
    }

    [Fact]
    public async Task OnlyARisingEdgeFiresATrigger()
    {
        var link=new FakePlcLink();
        var settings=new PlcSettings(Enabled:true,TriggerAddress:"X0");
        var source=new PlcTriggerSource(link,settings,TriggerMapping.Shared);
        var fired=new List<TriggerEvent>();
        source.Fired+=(_,e)=>fired.Add(e);

        link.Bits["X0"]=false;await source.PollOnceAsync(CancellationToken.None);
        Assert.Empty(fired);

        link.Bits["X0"]=true;await source.PollOnceAsync(CancellationToken.None);
        Assert.Single(fired);

        // A held button must not keep capturing.
        await source.PollOnceAsync(CancellationToken.None);
        await source.PollOnceAsync(CancellationToken.None);
        Assert.Single(fired);

        link.Bits["X0"]=false;await source.PollOnceAsync(CancellationToken.None);
        link.Bits["X0"]=true;await source.PollOnceAsync(CancellationToken.None);
        Assert.Equal(2,fired.Count);
        Assert.All(fired,e=>Assert.Null(e.End));
        Assert.All(fired,e=>Assert.Contains("X0",e.Source));
    }

    [Fact]
    public async Task PerEndAddressesNameTheEndTheySignal()
    {
        var link=new FakePlcLink();
        var settings=new PlcSettings(Enabled:true,End1Address:"X0",End2Address:"X1");
        var source=new PlcTriggerSource(link,settings,TriggerMapping.PerEnd);
        var fired=new List<TriggerEvent>();
        source.Fired+=(_,e)=>fired.Add(e);

        link.Bits["X0"]=false;link.Bits["X1"]=false;
        await source.PollOnceAsync(CancellationToken.None);
        link.Bits["X1"]=true;
        await source.PollOnceAsync(CancellationToken.None);

        var signal=Assert.Single(fired);
        Assert.Equal(1,signal.End);
    }

    [Fact]
    public async Task WritingBackIsOffUntilItIsConfigured()
    {
        var link=new FakePlcLink();
        var silent=new PlcReporter(link,new PlcOutputs());

        await silent.ReportStageAsync(PlcStage.WaitingEnd1,CancellationToken.None);
        await silent.ReportVerdictAsync(Verdict.Ng,CancellationToken.None);

        Assert.False(silent.Enabled);
        Assert.Empty(link.Written);        // nothing reaches the PLC until writing is enabled
        Assert.NotNull(new PlcOutputs(Enabled:true).Validate());
    }

    [Fact]
    public async Task AnEnabledReporterPublishesStageAndVerdict()
    {
        var link=new FakePlcLink();
        var outputs=new PlcOutputs(Enabled:true,WaitingEndRegister:"D100",BusyBit:"M10",
            OkBit:"M11",NgBit:"M12",ErrorBit:"M13",HeartbeatBit:"M14");
        var reporter=new PlcReporter(link,outputs);

        await reporter.ReportStageAsync(PlcStage.WaitingEnd2,CancellationToken.None);
        Assert.Equal((short)2,link.Words["D100"]);
        Assert.False(link.Bits["M10"]);

        await reporter.ReportStageAsync(PlcStage.Busy,CancellationToken.None);
        Assert.True(link.Bits["M10"]);

        await reporter.ReportVerdictAsync(Verdict.Ng,CancellationToken.None);
        Assert.False(link.Bits["M11"]);
        Assert.True(link.Bits["M12"]);
        Assert.False(link.Bits["M13"]);

        await reporter.BeatAsync(CancellationToken.None);
        Assert.True(link.Bits["M14"]);
        await reporter.BeatAsync(CancellationToken.None);
        Assert.False(link.Bits["M14"]);    // the watchdog has to keep changing

        await reporter.ClearVerdictAsync(CancellationToken.None);
        Assert.False(link.Bits["M12"]);
    }

    [Fact]
    public async Task AFailedWriteIsReportedWithoutThrowingAtTheInspection()
    {
        var link=new FakePlcLink{FailWrites=true};
        var reporter=new PlcReporter(link,new PlcOutputs(Enabled:true,OkBit:"M11"));

        await reporter.ReportVerdictAsync(Verdict.Ok,CancellationToken.None);

        Assert.NotNull(reporter.LastError);
        Assert.Contains("M11",reporter.LastError!);
    }

    [Fact]
    public void PlcConfigurationIsCheckedBeforeAnythingIsArmed()
    {
        Assert.Null(new PlcSettings().Validate(TriggerMapping.Shared));            // disabled needs nothing
        Assert.Contains("địa chỉ bit trigger",new PlcSettings(Enabled:true).Validate(TriggerMapping.Shared)!);
        Assert.Contains("đầu 1",new PlcSettings(Enabled:true,TriggerAddress:"X0").Validate(TriggerMapping.PerEnd)!);
        Assert.Contains("Chu kỳ đọc",new PlcSettings(Enabled:true,TriggerAddress:"X0",PollMs:0).Validate(TriggerMapping.Shared)!);
        Assert.Contains("IP",new PlcSettings(Enabled:true,TriggerAddress:"X0",Host:"").Validate(TriggerMapping.Shared)!);
        Assert.Null(new PlcSettings(Enabled:true,TriggerAddress:"X0").Validate(TriggerMapping.Shared));
    }

    [Fact]
    public void MachineSettingsSurviveARoundTripAndABrokenFile()
    {
        var root=Path.Combine(Path.GetTempPath(),"wmi-settings-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new FileSettingsStore(root);
            Assert.Equal(TriggerKind.Manual,store.Load().Trigger.Kind);   // a missing file is not an error

            var machine=new MachineSettings(
                new TriggerSettings(TriggerKind.Plc,TriggerMapping.PerEnd,new CameraTrigger(CameraTriggerSource.Software),300),
                new PlcSettings(Enabled:true,TriggerAddress:"X0",End1Address:"X0",End2Address:"X1",
                    Outputs:new PlcOutputs(Enabled:true,OkBit:"M11",NgBit:"M12")));
            store.Save(machine);

            var loaded=new FileSettingsStore(root).Load();
            Assert.Equal(TriggerKind.Plc,loaded.Trigger.Kind);
            Assert.Equal(TriggerMapping.PerEnd,loaded.Trigger.Mapping);
            Assert.Equal(300,loaded.Trigger.RepeatBlockMs);
            Assert.Equal("X1",loaded.Plc.End2Address);
            Assert.True(loaded.Plc.Writes.Enabled);
            Assert.Equal("M11",loaded.Plc.Writes.OkBit);

            // A corrupt settings file must not stop the station, but it must say so.
            File.WriteAllText(Path.Combine(root,"settings.json"),"{ not json");
            var broken=new FileSettingsStore(root);
            Assert.Equal(TriggerKind.Manual,broken.Load().Trigger.Kind);
            Assert.NotNull(broken.LoadError);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    private sealed class FakePlcLink:IPlcLink
    {
        public Dictionary<string,bool> Bits{get;}=[];
        public Dictionary<string,short> Words{get;}=[];
        public List<string> Written{get;}=[];
        public bool FailWrites{get;init;}
        public bool IsConnected{get;private set;}
        public string Status=>IsConnected?"fake connected":"fake idle";
        public Task ConnectAsync(CancellationToken token){IsConnected=true;return Task.CompletedTask;}
        public Task DisconnectAsync(){IsConnected=false;return Task.CompletedTask;}
        public Task<bool> ReadBitAsync(string address,CancellationToken token)=>
            Task.FromResult(Bits.TryGetValue(address,out var value)&&value);
        public Task WriteBitAsync(string address,bool value,CancellationToken token)
        {
            if(FailWrites)throw new IOException("PLC offline");
            Bits[address]=value;Written.Add(address);return Task.CompletedTask;
        }
        public Task WriteWordAsync(string address,short value,CancellationToken token)
        {
            if(FailWrites)throw new IOException("PLC offline");
            Words[address]=value;Written.Add(address);return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
