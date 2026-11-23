using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Application;

/// <summary>Append-only operational log. Soak evidence must outlive the running process.</summary>
public interface IDiagnosticsLog
{
    void Write(string category, string message, IReadOnlyDictionary<string, object?>? data = null);
}

public sealed record AcquisitionSnapshot(TimeSpan Uptime, long Frames, long Timeouts, long Reconnects,
    long ReconnectFailures, double? FramesPerSecond, string? LastError, DateTimeOffset? LastErrorAt);

/// <summary>
/// Counters for one acquisition run. Rates use a monotonic clock so a time-service correction cannot
/// distort a long soak measurement.
/// </summary>
public sealed class AcquisitionDiagnostics
{
    private readonly object gate = new();
    private long started;
    private long frames, timeouts, reconnects, reconnectFailures;
    private string? lastError;
    private DateTimeOffset? lastErrorAt;

    public void BeginRun()
    {
        lock (gate)
        {
            started = MonotonicClock.Now;
            frames = timeouts = reconnects = reconnectFailures = 0;
            lastError = null; lastErrorAt = null;
        }
    }
    public void Frame() { lock (gate) frames++; }
    public void Timeout() { lock (gate) timeouts++; }
    public void Reconnected() { lock (gate) reconnects++; }
    public void ReconnectFailed(string error)
    {
        lock (gate) { reconnectFailures++; lastError = error; lastErrorAt = DateTimeOffset.Now; }
    }
    public void Failed(string error)
    {
        lock (gate) { lastError = error; lastErrorAt = DateTimeOffset.Now; }
    }

    public AcquisitionSnapshot Snapshot()
    {
        lock (gate)
        {
            var elapsed = started == 0 ? 0 : MonotonicClock.MillisecondsSince(started);
            return new(TimeSpan.FromMilliseconds(elapsed), frames, timeouts, reconnects, reconnectFailures,
                elapsed > 0 && frames > 0 ? frames * 1000.0 / elapsed : null, lastError, lastErrorAt);
        }
    }
}

/// <summary>
/// Rolling cycle durations. A single number hides the outliers that matter on a production line, so
/// the average is reported next to p95 and max over a bounded window.
/// </summary>
public sealed class CycleStatistics(int capacity = 50)
{
    private readonly object gate = new();
    private readonly Queue<double> window = new();

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public double Last { get; private set; }

    public void Add(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "A measured duration cannot be negative.");
        lock (gate)
        {
            Last = milliseconds;
            window.Enqueue(milliseconds);
            while (window.Count > Capacity) window.Dequeue();
        }
    }
    public void Clear() { lock (gate) { window.Clear(); Last = 0; } }

    public (int Count, double Average, double P95, double Max) Summary()
    {
        lock (gate)
        {
            if (window.Count == 0) return (0, 0, 0, 0);
            var sorted = window.OrderBy(v => v).ToArray();
            // Nearest-rank p95: with a short window this is the honest reading of "95th percentile".
            var rank = (int)Math.Ceiling(sorted.Length * 0.95) - 1;
            return (sorted.Length, sorted.Average(), sorted[Math.Clamp(rank, 0, sorted.Length - 1)], sorted[^1]);
        }
    }
}
