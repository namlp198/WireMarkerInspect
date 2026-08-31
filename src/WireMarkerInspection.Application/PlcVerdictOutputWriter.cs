using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Application;

/// <summary>Executes the recipe-owned OK/NG action without changing an already computed verdict.</summary>
public sealed class PlcVerdictOutputWriter(IPlcLink link, IPlcAddressMap map, VerdictOutputProfile profile,
    IDiagnosticsLog? log = null)
{
    public string? LastError { get; private set; }
    public bool Enabled => profile.OkAction.Enabled || profile.NgAction.Enabled;

    public string? Validate()
    {
        if (profile.Validate() is { } error) return error;
        foreach (var (name, action) in Actions())
        {
            if (!action.Enabled) continue;
            try
            {
                var target = map.Translate(action.Address);
                if (!target.Writable) return $"{name} output {action.Address} is not writable.";
                if (action.Mode == PlcOutputMode.Bit && target.Area != ModbusArea.Coil)
                    return $"{name} output {action.Address} is not a writable bit.";
                if (action.Mode == PlcOutputMode.Register && target.Area != ModbusArea.HoldingRegister)
                    return $"{name} output {action.Address} is not a holding register.";
            }
            catch (Exception ex) { return $"{name} output is invalid: {ex.Message}"; }
        }
        return null;
    }

    public async Task ClearBitsAsync(CancellationToken token)
    {
        foreach (var (_, action) in Actions())
            if (action.Enabled && action.Mode == PlcOutputMode.Bit)
                await link.WriteBitAsync(action.Address, false, token).ConfigureAwait(false);
    }

    public async Task ReportAsync(Verdict verdict, CancellationToken token)
    {
        LastError = null;
        if (verdict == Verdict.Ok) await ExecuteAsync("OK", profile.OkAction, token).ConfigureAwait(false);
        else if (verdict == Verdict.Ng) await ExecuteAsync("NG", profile.NgAction, token).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(string name, PlcOutputAction action, CancellationToken token)
    {
        if (!action.Enabled) return;
        if (action.Mode == PlcOutputMode.Register)
        {
            try { await link.WriteWordAsync(action.Address, action.RegisterValue, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Fail(name, action.Address, ex); }
            return;
        }

        var set = false;
        try
        {
            await link.WriteBitAsync(action.Address, true, token).ConfigureAwait(false);
            set = true;
            await Task.Delay(action.PulseMs, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!set) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Fail(name, action.Address, ex); }
        finally
        {
            if (set)
            {
                // Never leave a machinery-facing pulse high just because the RUN cancellation token fired.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await link.WriteBitAsync(action.Address, false, cleanup.Token).ConfigureAwait(false); }
                catch (Exception ex) { Fail(name, action.Address, ex); }
            }
        }
    }

    private IEnumerable<(string Name, PlcOutputAction Action)> Actions()
    {
        yield return ("OK", profile.OkAction);
        yield return ("NG", profile.NgAction);
    }

    private void Fail(string name, string address, Exception ex)
    {
        LastError = $"{name} output {address} failed: {ex.Message}";
        log?.Write("plc", "verdict-output-failed", new Dictionary<string, object?>
        {
            ["verdict"] = name, ["address"] = address, ["error"] = ex.Message
        });
    }
}
