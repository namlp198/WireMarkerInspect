using System.IO.Ports;
using System.Net.Sockets;
using NModbus;
using NModbus.Serial;
using WireMarkerInspection.Application;

namespace WireMarkerInspection.Infrastructure;

/// <summary>
/// Modbus Ethernet or serial client addressed in the PLC's own syntax. COM supports both ASCII and RTU;
/// the Delta DVP station previously used Modbus ASCII at 9600 baud with a 7E1 frame.
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(settings.TimeoutMs);
        await gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            Release();
            var factory = new ModbusFactory();
            if (settings.Transport == PlcTransport.EthernetIp)
            {
                tcp = new TcpClient();
                await tcp.ConnectAsync(settings.Host, settings.Port, timeout.Token).ConfigureAwait(false);
                master = factory.CreateMaster(tcp);
            }
            else
            {
                serial = new SerialPort(settings.SerialPort, settings.BaudRate, ToParity(settings.Parity),
                    settings.DataBits, ToStopBits(settings.StopBits))
                {
                    ReadTimeout = settings.TimeoutMs,
                    WriteTimeout = settings.TimeoutMs
                };
                serial.Open();
                var adapter = new SerialPortAdapter(serial);
                master = settings.SerialProtocol == PlcSerialProtocol.ModbusAscii
                    ? factory.CreateAsciiMaster(adapter)
                    : factory.CreateRtuMaster(adapter);
            }
            master.Transport.ReadTimeout = settings.TimeoutMs;
            master.Transport.WriteTimeout = settings.TimeoutMs;
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

    public static IReadOnlyList<string> AvailableSerialPorts() =>
        SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    private static Parity ToParity(PlcSerialParity value) => value switch
    {
        PlcSerialParity.Even => Parity.Even,
        PlcSerialParity.Odd => Parity.Odd,
        _ => Parity.None
    };

    private static StopBits ToStopBits(PlcSerialStopBits value) =>
        value == PlcSerialStopBits.Two ? StopBits.Two : StopBits.One;

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        gate.Dispose();
    }
}
