namespace WireMarkerInspection.Application;

/// <summary>The physical connection between this station and the PLC.</summary>
public enum PlcTransport { EthernetIp = 0, Com = 1 }
public enum PlcSerialProtocol { ModbusAscii = 0, ModbusRtu = 1 }
public enum PlcSerialParity { None = 0, Even = 1, Odd = 2 }
public enum PlcSerialStopBits { One = 0, Two = 1 }

/// <summary>Modbus areas a vendor address can land in.</summary>
public enum ModbusArea { Coil, DiscreteInput, HoldingRegister, InputRegister }

/// <param name="Address">Zero-based Modbus device address.</param>
public sealed record ModbusTarget(ModbusArea Area, ushort Address, bool Writable);

/// <summary>
/// Translates a vendor's own address syntax (X1, M100, D200) into Modbus. This is the only part that
/// differs between brands, so supporting another PLC means adding a map, not changing the application.
/// </summary>
public interface IPlcAddressMap
{
    string Vendor { get; }
    ModbusTarget Translate(string address);
}

/// <summary>
/// A PLC connection expressed in the vendor's own addresses. No library type crosses this boundary, so
/// the Modbus driver and a licensed vendor driver are interchangeable.
/// </summary>
public interface IPlcLink : IAsyncDisposable
{
    bool IsConnected { get; }
    string Status { get; }
    Task ConnectAsync(CancellationToken token);
    Task DisconnectAsync();
    Task<bool> ReadBitAsync(string address, CancellationToken token);
    Task WriteBitAsync(string address, bool value, CancellationToken token);
    Task WriteWordAsync(string address, short value, CancellationToken token);
}

/// <summary>
/// Addresses the application writes back to the PLC. Writing reaches outside the software and can move
/// machinery, so it is opt-in, every address is declared explicitly, and nothing is inferred.
/// </summary>
public sealed record PlcOutputs(bool Enabled = false, string WaitingEndRegister = "", string BusyBit = "",
    string OkBit = "", string NgBit = "", string ErrorBit = "", string HeartbeatBit = "", int ClearAfterMs = 0)
{
    public string? Validate()
    {
        if (!Enabled) return null;
        if (WaitingEndRegister.Length == 0 && BusyBit.Length == 0 && OkBit.Length == 0 &&
            NgBit.Length == 0 && ErrorBit.Length == 0 && HeartbeatBit.Length == 0)
            return "Bật ghi PLC thì phải khai báo ít nhất một địa chỉ ghi.";
        return ClearAfterMs < 0 ? "Thời gian xóa kết quả không được âm." : null;
    }
}

public sealed record PlcSettings(bool Enabled = false, string Vendor = "delta-dvp",
    PlcTransport Transport = PlcTransport.Com, string Host = "192.168.1.5", int Port = 502,
    string SerialPort = "COM11", int BaudRate = 9600, byte UnitId = 1, int PollMs = 20,
    string TriggerAddress = "", string End1Address = "", string End2Address = "", PlcOutputs? Outputs = null,
    PlcSerialProtocol SerialProtocol = PlcSerialProtocol.ModbusAscii, int DataBits = 7,
    PlcSerialParity Parity = PlcSerialParity.Even, PlcSerialStopBits StopBits = PlcSerialStopBits.One,
    int TimeoutMs = 1000)
{
    public PlcOutputs Writes => Outputs ?? new PlcOutputs();
    public string Describe() => Transport == PlcTransport.EthernetIp
        ? $"{Vendor} · Ethernet IP {Host}:{Port} · unit {UnitId}"
        : $"{Vendor} · COM {SerialPort} {BaudRate} · {ProtocolLabel} {DataBits}{ParityCode}{StopBitsCode} · unit {UnitId}";

    public string ProtocolLabel => SerialProtocol == PlcSerialProtocol.ModbusAscii ? "Modbus ASCII" : "Modbus RTU";
    private char ParityCode => Parity switch { PlcSerialParity.Even => 'E', PlcSerialParity.Odd => 'O', _ => 'N' };
    private int StopBitsCode => StopBits == PlcSerialStopBits.Two ? 2 : 1;

    public string? ValidateConnection()
    {
        if (Vendor.Length == 0) return "Chọn hãng PLC.";
        if (UnitId is 0 or > 247) return "Unit ID phải trong khoảng 1–247.";
        if (PollMs is < 5 or > 5000) return "Chu kỳ đọc PLC phải trong khoảng 5–5000 ms.";
        if (TimeoutMs is < 100 or > 30000) return "Timeout PLC phải trong khoảng 100–30000 ms.";
        if (Transport == PlcTransport.EthernetIp)
        {
            if (Host.Length == 0) return "Nhập địa chỉ IP của PLC.";
            if (Port is < 1 or > 65535) return "Cổng Ethernet PLC không hợp lệ.";
        }
        else
        {
            if (SerialPort.Length == 0) return "Chọn cổng COM của PLC.";
            if (BaudRate <= 0) return "Baud rate phải lớn hơn 0.";
            if (DataBits is < 5 or > 8) return "Data bits phải trong khoảng 5–8.";
        }
        return null;
    }

    public string? Validate(TriggerMapping mapping)
    {
        if (!Enabled) return null;
        if (ValidateConnection() is { } connectionError) return connectionError;
        if (mapping == TriggerMapping.Shared)
        {
            if (TriggerAddress.Length == 0) return "Nhập địa chỉ bit trigger.";
        }
        else if (End1Address.Length == 0 || End2Address.Length == 0)
            return "Chế độ tách hai đầu cần địa chỉ bit cho cả đầu 1 và đầu 2.";
        return Writes.Validate();
    }
}

/// <summary>
/// Polls PLC bits and turns a rising edge into a trigger. Only the transition fires, so a held button or
/// a latched bit cannot capture repeatedly.
/// </summary>
public sealed class PlcTriggerSource(IPlcLink link, PlcSettings settings, TriggerMapping mapping,
    IDiagnosticsLog? log = null, bool manageLinkLifecycle = true) : PushTriggerSource
{
    private readonly Dictionary<string, bool> previous = [];
    private CancellationTokenSource? polling;
    private Task? loop;
    private string status = "PLC chưa kết nối";

    public override string Status => status;

    public override async Task StartAsync(CancellationToken token)
    {
        if (manageLinkLifecycle) await link.ConnectAsync(token).ConfigureAwait(false);
        else if (!link.IsConnected) throw new InvalidOperationException("PLC chưa được kết nối từ phần PLC CONNECTION.");
        status = $"Trigger PLC · {settings.Describe()}";
        previous.Clear();
        polling = CancellationTokenSource.CreateLinkedTokenSource(token);
        loop = Task.Run(() => PollAsync(polling.Token), CancellationToken.None);
    }

    public override async Task StopAsync()
    {
        polling?.Cancel();
        if (loop != null) { try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        polling?.Dispose(); polling = null; loop = null;
        if (manageLinkLifecycle) await link.DisconnectAsync().ConfigureAwait(false);
        status = "PLC đã ngắt";
    }

    /// <summary>Reads one poll cycle. Exposed so a test can step the edge detector deterministically.</summary>
    public async Task PollOnceAsync(CancellationToken token)
    {
        foreach (var (address, end) in Addresses())
        {
            var value = await link.ReadBitAsync(address, token).ConfigureAwait(false);
            var had = previous.TryGetValue(address, out var last) && last;
            previous[address] = value;
            if (value && !had) Fire(end, $"PLC {address}");
        }
    }

    private IEnumerable<(string Address, int? End)> Addresses()
    {
        if (mapping == TriggerMapping.Shared) { yield return (settings.TriggerAddress, null); yield break; }
        yield return (settings.End1Address, 0);
        yield return (settings.End2Address, 1);
    }

    private async Task PollAsync(CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(token).ConfigureAwait(false);
                failures = 0;
                status = $"Trigger PLC · {settings.Describe()}";
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // A PLC read failure must be visible, not a silently dead trigger.
                failures++;
                status = $"Lỗi đọc PLC ({failures}): {ex.Message}";
                log?.Write("plc", "read-failed", new Dictionary<string, object?> { ["error"] = ex.Message, ["failures"] = failures });
                try { await Task.Delay(500, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                continue;
            }
            try { await Task.Delay(settings.PollMs, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (manageLinkLifecycle) await link.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
