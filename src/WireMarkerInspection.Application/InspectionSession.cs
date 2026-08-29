using WireMarkerInspection.Domain;
namespace WireMarkerInspection.Application;
public enum InspectionState { Idle, WaitingEnd1, ProcessingEnd1, WaitingEnd2, ProcessingEnd2, Completed, Faulted, Stopped }

public sealed class InspectionSession(IOcrEngine ocr, IResultStore results)
{
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private int generation;
    private Recipe? recipe;
    private readonly List<EndResult> ends = [];
    private readonly List<ImageFrame> frames = [];
    public InspectionState State { get; private set; }
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
    public async Task<EndResult?> AcceptAsync(ImageFrame input)
    {
        Recipe current; int end; int version; CancellationToken token; Guid cycle;
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
            var reading = await ocr.ReadAsync(frame, spec, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var result = ExactTextComparer.Compare(frame, spec, reading);
            ProductResult? product = null; ImageFrame[]? captured = null;
            lock(gate)
            {
                if (version != generation) return null;
                ends.Add(result); frames.Add(frame);
                if (end == 0) State = InspectionState.WaitingEnd2;
                else
                {
                    captured = [.. frames];
                    product = new(cycle, current, [.. ends], DateTimeOffset.UtcNow,
                        captured.Select(f => new CaptureEvidence(f.Id, f.CapturedAt, f.Source, f.Width, f.Height)).ToArray());
                }
            }
            if (product != null)
            {
                await results.SaveAsync(product, captured!, token).ConfigureAwait(false);
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
}
