using WireMarkerInspection.Domain;
namespace WireMarkerInspection.Application;
public enum InspectionState { Idle, WaitingEnd1, ProcessingEnd1, WaitingEnd2, ProcessingEnd2, Completed, Faulted, Stopped }

public sealed class InspectionSession(IOcrEngine ocr, IResultStore results, ITemplateMatcher? matcher=null)
{
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private int generation;
    private Recipe? recipe;
    private readonly List<EndResult> ends = [];
    private readonly List<ImageFrame> frames = [];
    public InspectionState State { get; private set; }
    /// <summary>Time the last completed cycle spent writing its result, measured after the file was written.</summary>
    public double LastPersistMilliseconds { get; private set; }
    public Guid CycleId { get; private set; }
    public string? Error { get; private set; }
    public ProductResult? Result { get; private set; }
    public EndResult[] EndResults { get { lock (gate) return [.. ends]; } }
    public int NextEnd { get { lock(gate) return ends.Count; } }

    public void Begin(Recipe selected)
    {
        var snapshot = selected.Copy();
        if (snapshot.Validate() is { } error) throw new InvalidOperationException(error);
        lock(gate)
        {
            if (State is InspectionState.ProcessingEnd1 or InspectionState.ProcessingEnd2 or
                InspectionState.WaitingEnd1 or InspectionState.WaitingEnd2)
                throw new InvalidOperationException("Stop or finish the current cycle first.");
            cancellation?.Dispose();
            cancellation = new();
            generation++;
            recipe = snapshot; ends.Clear(); frames.Clear(); Result = null; Error = null;
            CycleId = Guid.NewGuid(); State = InspectionState.WaitingEnd1;
        }
    }
    /// <param name="frameAgeMs">
    /// How long the frame had been waiting in the application before it was accepted, measured by the
    /// caller that owns the acquisition loop.
    /// </param>
    public async Task<EndResult?> AcceptAsync(ImageFrame input, double frameAgeMs = 0)
    {
        Recipe current; int end; int version; CancellationToken token; Guid cycle;
        var started = MonotonicClock.Now;
        // Own the bytes: the camera or UI may reuse its buffer after this call.
        input.Validate();
        var frame = input with { Bgr = [.. input.Bgr] };
        lock(gate)
        {
            if (State is not (InspectionState.WaitingEnd1 or InspectionState.WaitingEnd2))
                throw new InvalidOperationException("This cycle is not waiting for an image.");
            if (frames.Any(f => f.Id == frame.Id)) throw new InvalidOperationException("This frame was already captured.");
            current = recipe!; end = ends.Count; version = generation; token = cancellation!.Token; cycle = CycleId;
            State = end == 0 ? InspectionState.ProcessingEnd1 : InspectionState.ProcessingEnd2;
        }
        try
        {
            var spec = current.Ends[end];
            if (frame.Width != spec.Width || frame.Height != spec.Height)
                throw new InvalidOperationException("Image dimensions differ from this recipe. Re-teach the ROI.");
            var ocrStarted = MonotonicClock.Now;
            var reading = await ocr.ReadAsync(frame, spec, token).ConfigureAwait(false);
            var ocrMs = MonotonicClock.MillisecondsSince(ocrStarted);
            token.ThrowIfCancellationRequested();
            var compareStarted = MonotonicClock.Now;
            var result = ExactTextComparer.Compare(frame, spec, reading);
            var compareMs = MonotonicClock.MillisecondsSince(compareStarted);
            if(spec.Terminal is {Enabled:true} terminal)
            {
                if(matcher==null)throw new InvalidOperationException("Terminal matcher is unavailable.");
                var matching=await matcher.MatchAsync(frame,terminal,token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                result=CombinedInspectionComparer.Combine(result,matching,true);
            }
            ProductResult? product = null; ImageFrame[]? captured = null;
            result = result with
            {
                Timings =
                [
                    new("frame-age", frameAgeMs), new("ocr", ocrMs), new("compare", compareMs),new("template",result.Terminal?.Milliseconds??0),
                    new("end", MonotonicClock.MillisecondsSince(started))
                ]
            };
            lock(gate)
            {
                if (version != generation) return null;
                ends.Add(result); frames.Add(frame);
                if (end == 0) State = InspectionState.WaitingEnd2;
                else
                {
                    captured = [.. frames];
                    product = new(cycle, current, [.. ends], DateTimeOffset.UtcNow,
                        captured.Select(f => new CaptureEvidence(f.Id, f.CapturedAt, f.Source, f.Width, f.Height)).ToArray(),
                        [new("cycle", ends.Sum(e => e.MillisecondsOf("end")))]);
                }
            }
            if (product != null)
            {
                var persistStarted = MonotonicClock.Now;
                await results.SaveAsync(product, captured!, token).ConfigureAwait(false);
                LastPersistMilliseconds = MonotonicClock.MillisecondsSince(persistStarted);
                lock(gate)
                {
                    if (version != generation) return null;
                    Result = product; State = InspectionState.Completed;
                }
            }
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch(Exception ex)
        {
            lock(gate)
            {
                if (version != generation) return null;
                State = InspectionState.Faulted; Error = ex.Message;
            }
            throw;
        }
    }
    public void Stop()
    {
        lock(gate)
        {
            generation++; cancellation?.Cancel(); State = InspectionState.Stopped;
        }
    }

    /// <summary>
    /// Drops the end already captured so the operator can shoot it again. Only offered while the first
    /// end is done and the second has not been captured, which is the case a bad first image creates.
    /// </summary>
    public bool RetakeLastEnd()
    {
        lock(gate)
        {
            if (State != InspectionState.WaitingEnd2 || ends.Count == 0) return false;
            generation++; cancellation?.Cancel();
            cancellation?.Dispose(); cancellation = new();
            ends.RemoveAt(ends.Count - 1); frames.RemoveAt(frames.Count - 1);
            Error = null; State = InspectionState.WaitingEnd1;
            return true;
        }
    }

    /// <summary>
    /// Abandons the cycle in progress. Losing the camera part-way through a product must never let the
    /// next frame be filed as the second end of an interrupted cycle.
    /// </summary>
    public bool Fault(string reason)
    {
        lock(gate)
        {
            if (State is not (InspectionState.WaitingEnd1 or InspectionState.WaitingEnd2 or
                InspectionState.ProcessingEnd1 or InspectionState.ProcessingEnd2)) return false;
            generation++; cancellation?.Cancel();
            ends.Clear(); frames.Clear(); Result = null;
            Error = reason; State = InspectionState.Faulted;
            return true;
        }
    }
}
