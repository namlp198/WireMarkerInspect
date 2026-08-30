using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Application;

/// <summary>Where a capture request comes from.</summary>
public enum TriggerKind { Manual, CameraLine, Plc }

/// <summary>
/// How one trigger maps onto the two ends of a product. <see cref="Shared"/> uses one signal and the
/// session state to know which end is expected; <see cref="PerEnd"/> carries the end in the signal itself.
/// </summary>
public enum TriggerMapping { Shared, PerEnd }

public enum CameraTriggerSource { FreeRun, Line, Software }

/// <summary>
/// Camera-side trigger wiring. <see cref="CameraTriggerSource.FreeRun"/> restores continuous acquisition,
/// which SETTING needs for framing; RUN uses a triggered source.
/// </summary>
public sealed record CameraTrigger(CameraTriggerSource Source, int Line = 0, bool RisingEdge = true,
    double DelayUs = 0, double DebouncerUs = 0)
{
    public static readonly CameraTrigger FreeRun = new(CameraTriggerSource.FreeRun);
    public bool IsTriggered => Source != CameraTriggerSource.FreeRun;
    public string? Validate() =>
        Line < 0 ? "Trigger line must not be negative." :
        DelayUs < 0 || DebouncerUs < 0 ? "Trigger delay and debouncer cannot be negative." : null;
}

public sealed record TriggerSettings(TriggerKind Kind = TriggerKind.Manual,
    TriggerMapping Mapping = TriggerMapping.Shared, CameraTrigger? Camera = null, int RepeatBlockMs = 250)
{
    public CameraTrigger CameraTrigger => Camera ?? CameraTrigger.FreeRun;
    public string? Validate()
    {
        if (RepeatBlockMs < 0) return "Trigger repeat block cannot be negative.";
        if (CameraTrigger.Validate() is { } error) return error;
        // One camera exposes a single TriggerSource node, so two physical lines cannot drive the two ends.
        if (Kind == TriggerKind.CameraLine && Mapping == TriggerMapping.PerEnd)
            return "Một camera chỉ có một nguồn trigger. Dùng tín hiệu chung, hoặc chuyển sang trigger PLC để tách hai đầu.";
        if (Kind == TriggerKind.CameraLine && CameraTrigger.Source != CameraTriggerSource.Line)
            return "Trigger phần cứng cần chọn chân Line của camera.";
        return null;
    }
}

/// <param name="End">Zero-based end the signal names, or null when one shared signal is used.</param>
public sealed record TriggerEvent(int? End, long Timestamp, string Source);

public enum TriggerOutcome { Accepted, Ignored }

/// <param name="End">Zero-based end the capture was routed to, when accepted.</param>
public sealed record TriggerDecision(TriggerOutcome Outcome, int End, string Reason)
{
    public bool Accepted => Outcome == TriggerOutcome.Accepted;
}

public interface ITriggerSource : IAsyncDisposable
{
    event EventHandler<TriggerEvent>? Fired;
    string Status { get; }
    Task StartAsync(CancellationToken token);
    Task StopAsync();
}

/// <summary>Base for trigger sources that are pushed by something else (a click, a frame, a PLC poll).</summary>
public abstract class PushTriggerSource : ITriggerSource
{
    public event EventHandler<TriggerEvent>? Fired;
    public abstract string Status { get; }
    public virtual Task StartAsync(CancellationToken token) => Task.CompletedTask;
    public virtual Task StopAsync() => Task.CompletedTask;
    public void Fire(int? end, string source) => Fired?.Invoke(this, new(end, MonotonicClock.Now, source));
    public virtual ValueTask DisposeAsync() { GC.SuppressFinalize(this); return ValueTask.CompletedTask; }
}

/// <summary>The operator pressing the capture button. Always available, including offline.</summary>
public sealed class ManualTriggerSource : PushTriggerSource
{
    public override string Status => "Trigger thủ công";
}

/// <summary>
/// A pulse on the camera's I/O line. The camera only delivers a frame when it is triggered, so an
/// arriving frame is the trigger event; this source owns the camera-side configuration.
/// </summary>
public sealed class CameraLineTriggerSource(ICamera camera, CameraTrigger trigger) : PushTriggerSource
{
    public override string Status => $"Trigger phần cứng · Line {trigger.Line} · {(trigger.RisingEdge ? "sườn lên" : "sườn xuống")}";
    public override Task StartAsync(CancellationToken token)
    {
        if (trigger.Validate() is { } error) throw new InvalidOperationException(error);
        camera.ConfigureTrigger(trigger);
        return Task.CompletedTask;
    }
    public override Task StopAsync()
    {
        camera.ConfigureTrigger(CameraTrigger.FreeRun);
        return Task.CompletedTask;
    }
}
