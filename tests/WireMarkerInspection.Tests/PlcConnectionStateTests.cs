using System.IO;
using WireMarkerInspection.Application;
using WireMarkerInspection.Desktop.ViewModels;
using Xunit;

namespace WireMarkerInspection.Tests;

public sealed class PlcConnectionStateTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-plc-ui-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConnectAndDisconnectOwnTheVisiblePlcState()
    {
        var link=new FakePlcLink();
        PlcSettings? openedWith=null;
        var vm=new MainViewModel(root,autoDiscoverCameraOnLoad:false,plcFactory:settings=>
        {
            openedWith=settings;
            return link;
        }).AsAdmin();

        Assert.Equal(PlcTransport.Com,vm.PlcTransport);
        Assert.True(vm.PlcUsesSerial);
        Assert.False(vm.PlcUsesNetwork);
        Assert.True(vm.CanConnectPlc);
        Assert.False(vm.CanDisconnectPlc);

        await vm.ConnectPlcCommand.ExecuteAsync(null);

        Assert.Equal(PlcConnectionState.Connected,vm.PlcConnectionState);
        Assert.True(vm.PlcConnected);
        Assert.False(vm.CanConfigurePlc);
        Assert.False(vm.CanConnectPlc);
        Assert.True(vm.CanDisconnectPlc);
        Assert.NotNull(openedWith);
        Assert.Equal(PlcSerialProtocol.ModbusAscii,openedWith!.SerialProtocol);
        Assert.Equal("COM11",openedWith.SerialPort);
        Assert.Equal(7,openedWith.DataBits);
        Assert.Equal(PlcSerialParity.Even,openedWith.Parity);
        Assert.Equal("X0",link.LastRead);

        await vm.DisconnectPlcCommand.ExecuteAsync(null);

        Assert.Equal(PlcConnectionState.Disconnected,vm.PlcConnectionState);
        Assert.False(vm.PlcConnected);
        Assert.True(vm.CanConfigurePlc);
        Assert.True(vm.CanConnectPlc);
        Assert.False(vm.CanDisconnectPlc);
    }

    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    private sealed class FakePlcLink:IPlcLink
    {
        public bool IsConnected{get;private set;}
        public string Status=>IsConnected?"connected":"disconnected";
        public string? LastRead{get;private set;}
        public Task ConnectAsync(CancellationToken token){IsConnected=true;return Task.CompletedTask;}
        public Task DisconnectAsync(){IsConnected=false;return Task.CompletedTask;}
        public Task<bool> ReadBitAsync(string address,CancellationToken token)
        {
            LastRead=address;return Task.FromResult(false);
        }
        public Task WriteBitAsync(string address,bool value,CancellationToken token)=>Task.CompletedTask;
        public Task WriteWordAsync(string address,short value,CancellationToken token)=>Task.CompletedTask;
        public ValueTask DisposeAsync(){IsConnected=false;return ValueTask.CompletedTask;}
    }
}
