using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Application;

/// <summary>
/// Decides which end a trigger belongs to and refuses the ones that would file an image against the
/// wrong end. A mis-routed capture is the dangerous failure here: the two ends carry different expected
/// text, so it produces a confident but wrong verdict.
/// </summary>
public sealed class TriggerRouter(TriggerSettings settings)
{
    private readonly Dictionary<int, long> lastAccepted = [];

    public TriggerSettings Settings { get; } =
        settings.Validate() is { } error ? throw new ArgumentException(error, nameof(settings)) : settings;

    /// <param name="state">The session state at the moment the signal arrived.</param>
    /// <param name="nextEnd">Zero-based end the session is waiting for.</param>
    public TriggerDecision Route(TriggerEvent signal, InspectionState state, int nextEnd)
    {
        if (state is InspectionState.ProcessingEnd1 or InspectionState.ProcessingEnd2)
            return new(TriggerOutcome.Ignored, -1, "Đang xử lý ảnh trước, bỏ qua trigger.");
        if (state is not (InspectionState.WaitingEnd1 or InspectionState.WaitingEnd2))
            return new(TriggerOutcome.Ignored, -1, "RUN không ở trạng thái chờ ảnh.");

        int end;
        if (Settings.Mapping == TriggerMapping.Shared) end = nextEnd;
        else
        {
            if (signal.End is not { } named)
                return new(TriggerOutcome.Ignored, -1, "Cấu hình tách hai đầu nhưng tín hiệu không cho biết đầu nào.");
            if (named != nextEnd)
                return new(TriggerOutcome.Ignored, named,
                    $"Tín hiệu cho đầu {named + 1} nhưng đang chờ đầu {nextEnd + 1}.");
            end = named;
        }

        // A bouncing contact or a held button must not capture the same end twice.
        if (lastAccepted.TryGetValue(end, out var previous) &&
            MonotonicClock.Milliseconds(previous, signal.Timestamp) < Settings.RepeatBlockMs)
            return new(TriggerOutcome.Ignored, end, "Trigger lặp quá nhanh, đã bỏ qua.");

        lastAccepted[end] = signal.Timestamp;
        return new(TriggerOutcome.Accepted, end, $"Nhận ảnh đầu {end + 1} từ {signal.Source}.");
    }

    /// <summary>Forgets the repeat-block history, for example when a fresh product cycle starts.</summary>
    public void Reset() => lastAccepted.Clear();
}
