using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Application;

/// <summary>What the machine is doing, as the PLC sees it.</summary>
public enum PlcStage { Idle, WaitingEnd1, WaitingEnd2, Busy }

/// <summary>
/// Writes the machine state and verdict back to the PLC so the line can light a lamp, lock a button or
/// sort a part. Writing reaches outside the software, so every address is explicit, the whole feature is
/// opt-in, and a write failure is surfaced rather than swallowed — but it never aborts an inspection.
/// </summary>
public sealed class PlcReporter(IPlcLink link, PlcOutputs outputs, IDiagnosticsLog? log = null)
{
    private bool heartbeat;

    public string? LastError { get; private set; }
    public bool Enabled => outputs.Enabled;

    public async Task ReportStageAsync(PlcStage stage, CancellationToken token)
    {
        if (!outputs.Enabled) return;
        await WriteWordAsync(outputs.WaitingEndRegister, stage switch
        {
            PlcStage.WaitingEnd1 => 1, PlcStage.WaitingEnd2 => 2, PlcStage.Busy => 3, _ => 0
        }, token).ConfigureAwait(false);
        await WriteBitAsync(outputs.BusyBit, stage == PlcStage.Busy, token).ConfigureAwait(false);
    }

    public async Task ReportVerdictAsync(Verdict verdict, CancellationToken token)
    {
        if (!outputs.Enabled) return;
        await WriteBitAsync(outputs.OkBit, verdict == Verdict.Ok, token).ConfigureAwait(false);
        await WriteBitAsync(outputs.NgBit, verdict == Verdict.Ng, token).ConfigureAwait(false);
        await WriteBitAsync(outputs.ErrorBit, verdict == Verdict.Error, token).ConfigureAwait(false);
        if (outputs.ClearAfterMs <= 0) return;
        // The PLC latches the result itself when ClearAfterMs is zero; otherwise the app clears it.
        await Task.Delay(outputs.ClearAfterMs, token).ConfigureAwait(false);
        await ClearVerdictAsync(token).ConfigureAwait(false);
    }

    public async Task ClearVerdictAsync(CancellationToken token)
    {
        if (!outputs.Enabled) return;
        foreach (var address in new[] { outputs.OkBit, outputs.NgBit, outputs.ErrorBit })
            await WriteBitAsync(address, false, token).ConfigureAwait(false);
    }

    /// <summary>Toggles the watchdog bit so the PLC can tell a stopped application from an idle one.</summary>
    public async Task BeatAsync(CancellationToken token)
    {
        if (!outputs.Enabled || outputs.HeartbeatBit.Length == 0) return;
        heartbeat = !heartbeat;
        await WriteBitAsync(outputs.HeartbeatBit, heartbeat, token).ConfigureAwait(false);
    }

    private async Task WriteBitAsync(string address, bool value, CancellationToken token)
    {
        if (address.Length == 0) return;
        try { await link.WriteBitAsync(address, value, token).ConfigureAwait(false); LastError = null; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Fail(address, ex); }
    }

    private async Task WriteWordAsync(string address, short value, CancellationToken token)
    {
        if (address.Length == 0) return;
        try { await link.WriteWordAsync(address, value, token).ConfigureAwait(false); LastError = null; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Fail(address, ex); }
    }

    private void Fail(string address, Exception ex)
    {
        LastError = $"Ghi {address} thất bại: {ex.Message}";
        log?.Write("plc", "write-failed", new Dictionary<string, object?> { ["address"] = address, ["error"] = ex.Message });
    }
}
