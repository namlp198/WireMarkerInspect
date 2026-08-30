using System.IO.Ports;
using System.Net.Sockets;
using NModbus;
using NModbus.Serial;
using WireMarkerInspection.Application;

namespace WireMarkerInspection.Infrastructure;

/// <summary>
/// Modbus TCP or RTU client addressed in the PLC's own syntax. Delta DVP speaks Modbus natively, and the
/// same driver serves any other Modbus PLC once its address map exists.
/// </summary>
public sealed class ModbusPlcLink(PlcSettings settings, IPlcAddressMap map) : IPlcLink
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private TcpClient? tcp;
    private SerialPort? serial;
    private IModbusMaster? master;

    public bool IsConnected { get; private set; }
    public string Status { get; private set; } = "Chưa kết nối PLC";

    public async Task ConnectAsync(CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Release();
            var factory = new ModbusFactory();
            if (settings.Transport == PlcTransport.Tcp)
            {
                tcp = new TcpClient();
                await tcp.ConnectAsync(settings.Host, settings.Port, token).ConfigureAwait(false);
                master = factory.CreateMaster(tcp);
            }
            else
            {
                serial = new SerialPort(settings.SerialPort, settings.BaudRate, Parity.Even, 7, StopBits.One);
                serial.Open();
                master = factory.CreateRtuMaster(new SerialPortAdapter(serial));
            }
            master.Transport.ReadTimeout = 1000;
            master.Transport.WriteTimeout = 1000;
            IsConnected = true;
            Status = $"Đã kết nối {settings.Describe()}";
        }
        catch
        {
            Release();
            Status = $"Không kết nối được {settings.Describe()}";
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { Release(); Status = "Đã ngắt PLC"; }
        finally { gate.Release(); }
    }

    public async Task<bool> ReadBitAsync(string address, CancellationToken token)
    {
        var target = map.Translate(address);
        return await RunAsync(client => target.Area == ModbusArea.DiscreteInput
            ? client.ReadInputs(settings.UnitId, target.Address, 1)[0]
            : client.ReadCoils(settings.UnitId, target.Address, 1)[0], token).ConfigureAwait(false);
    }

    public async Task WriteBitAsync(string address, bool value, CancellationToken token)
    {
        var target = map.Translate(address);
        if (!target.Writable) throw new InvalidOperationException($"{address} là ngõ vào của PLC, không ghi được.");
        if (target.Area != ModbusArea.Coil) throw new InvalidOperationException($"{address} không phải vùng bit.");
        await RunAsync<object?>(client => { client.WriteSingleCoil(settings.UnitId, target.Address, value); return null; }, token)
            .ConfigureAwait(false);
    }

    public async Task WriteWordAsync(string address, short value, CancellationToken token)
    {
        var target = map.Translate(address);
        if (!target.Writable) throw new InvalidOperationException($"{address} không ghi được.");
        if (target.Area != ModbusArea.HoldingRegister) throw new InvalidOperationException($"{address} không phải thanh ghi word.");
        await RunAsync<object?>(client =>
        {
            client.WriteSingleRegister(settings.UnitId, target.Address, unchecked((ushort)value)); return null;
        }, token).ConfigureAwait(false);
    }

    private async Task<T> RunAsync<T>(Func<IModbusMaster, T> action, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var client = master ?? throw new InvalidOperationException("PLC chưa được kết nối.");
            // NModbus is synchronous on the wire; keep it off the UI thread.
            return await Task.Run(() => action(client), token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            IsConnected = false;
            Status = $"Lỗi PLC: {ex.Message}";
            throw;
        }
        finally { gate.Release(); }
    }

    private void Release()
    {
        master?.Dispose(); master = null;
        try { serial?.Close(); } catch (IOException) { }
        serial?.Dispose(); serial = null;
        tcp?.Dispose(); tcp = null;
        IsConnected = false;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        gate.Dispose();
    }
}
