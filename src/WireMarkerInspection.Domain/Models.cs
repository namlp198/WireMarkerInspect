namespace WireMarkerInspection.Domain;

public readonly record struct PixelPoint(double X, double Y);
public enum RoiShape { Rectangle, Circle, Polygon }
public enum TextOrientation { Degrees0, Degrees180, Auto }
public enum Verdict { Ok, Ng, Error }

public sealed record SearchRoi(RoiShape Shape, PixelPoint[] Points)
{
    public SearchRoi Copy() => this with { Points = [.. Points] };
    public static SearchRoi FullImage(int width, int height) =>
        new(RoiShape.Rectangle, [new(0, 0), new(width, height)]);
    public (double X, double Y, double Width, double Height) Bounds
    {
        get
        {
            if (Points.Length == 0) return default;
            if (Shape == RoiShape.Circle && Points.Length == 2)
            {
                var r = Math.Sqrt(Math.Pow(Points[1].X - Points[0].X, 2) + Math.Pow(Points[1].Y - Points[0].Y, 2));
                return (Points[0].X - r, Points[0].Y - r, r * 2, r * 2);
            }
            return (Points.Min(p => p.X), Points.Min(p => p.Y),
                Points.Max(p => p.X) - Points.Min(p => p.X), Points.Max(p => p.Y) - Points.Min(p => p.Y));
        }
    }
    public string? Validate(int width, int height)
    {
        if (width <= 0 || height <= 0) return "Missing reference image dimensions.";
        if (Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) return "ROI has invalid coordinates.";
        if (Shape == RoiShape.Polygon ? Points.Length < 3 : Points.Length != 2) return "ROI has an invalid point count.";
        var b = Bounds;
        if (b.Width < 2 || b.Height < 2) return "ROI must be at least 2 × 2 pixels.";
        if (b.X < 0 || b.Y < 0 || b.X + b.Width > width + 0.001 || b.Y + b.Height > height + 0.001)
            return "ROI is outside the source image.";
        if (Shape == RoiShape.Polygon)
        {
            double area = 0;
            for (var i = 0; i < Points.Length; i++)
            {
                var a = Points[i]; var next = Points[(i + 1) % Points.Length];
                if (a == next) return "Polygon contains duplicate adjacent vertices.";
                area += a.X * next.Y - next.X * a.Y;
                for (var j = i + 1; j < Points.Length; j++)
                {
                    if (j == i + 1 || (i == 0 && j == Points.Length - 1)) continue;
                    if (Intersects(a, next, Points[j], Points[(j + 1) % Points.Length]))
                        return "Polygon must not intersect itself.";
                }
            }
            if (Math.Abs(area) < 4) return "Polygon area is too small.";
        }
        return null;
    }
    private static bool Intersects(PixelPoint a, PixelPoint b, PixelPoint c, PixelPoint d)
    {
        static double Cross(PixelPoint p, PixelPoint q, PixelPoint r) =>
            (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        static bool On(PixelPoint p, PixelPoint q, PixelPoint r) =>
            r.X >= Math.Min(p.X, q.X) && r.X <= Math.Max(p.X, q.X) &&
            r.Y >= Math.Min(p.Y, q.Y) && r.Y <= Math.Max(p.Y, q.Y);
        var x = Cross(a,b,c); var y = Cross(a,b,d); var z = Cross(c,d,a); var w = Cross(c,d,b);
        return (x * y < 0 && z * w < 0) || (x == 0 && On(a,b,c)) || (y == 0 && On(a,b,d)) ||
               (z == 0 && On(c,d,a)) || (w == 0 && On(c,d,b));
    }
}

public sealed record EndRecipe(string ReferenceImage, int Width, int Height, SearchRoi Roi,
    string[] ExpectedLines, TextOrientation Orientation = TextOrientation.Degrees0)
{
    public EndRecipe Copy() => this with { Roi = Roi.Copy(), ExpectedLines = [.. ExpectedLines] };
    public string? Validate() => Roi.Validate(Width, Height) ??
        (ExpectedLines.Length == 0 || ExpectedLines.Any(string.IsNullOrEmpty)
            ? "Enter expected text, one detected region per line. Empty lines are not allowed." : null);
}
/// <summary>Sensor readout window in sensor pixels. Null means the full sensor.</summary>
public sealed record SensorRoi(int OffsetX, int OffsetY, int Width, int Height)
{
    public string? Validate() =>
        Width <= 0 || Height <= 0 ? "Sensor ROI width and height must be positive." :
        OffsetX < 0 || OffsetY < 0 ? "Sensor ROI offset cannot be negative." : null;
}

/// <summary>Strobe output that fires the inspection light in step with the exposure.</summary>
public sealed record StrobeSettings(bool Enabled, int Line, double DurationUs, double DelayUs)
{
    public string? Validate() =>
        Line < 0 ? "Strobe line must not be negative." :
        DurationUs < 0 || DelayUs < 0 ? "Strobe duration and delay cannot be negative." : null;
}

/// <summary>
/// Acquisition settings a model was taught with. Repeating the exposure, gain and lighting of the
/// reference images is what makes a saved recipe reproducible, so they are stored with the recipe.
/// </summary>
public sealed record CameraSettings(double ExposureTimeUs, double Gain, double? Gamma = null,
    double? BlackLevel = null, SensorRoi? Roi = null, StrobeSettings? Strobe = null)
{
    public string? Validate()
    {
        if (!double.IsFinite(ExposureTimeUs) || ExposureTimeUs <= 0) return "Exposure time must be a positive number.";
        if (!double.IsFinite(Gain) || Gain < 0) return "Gain cannot be negative.";
        if (Gamma is { } gamma && (!double.IsFinite(gamma) || gamma <= 0)) return "Gamma must be a positive number.";
        if (BlackLevel is { } black && (!double.IsFinite(black) || black < 0)) return "Black level cannot be negative.";
        return Roi?.Validate() ?? Strobe?.Validate();
    }
}

public sealed record Recipe(Guid Id, string ModelCode, string Name, int Revision, EndRecipe[] Ends,
    DateTimeOffset SavedAt, int SchemaVersion = 1, CameraSettings? Camera = null)
{
    public Recipe Copy() => this with { Ends = Ends.Select(e => e.Copy()).ToArray() };
    public string? Validate()
    {
        if (SchemaVersion != 1) return "Unsupported recipe schema.";
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(ModelCode) || string.IsNullOrWhiteSpace(Name))
            return "Model code and name are required.";
        if (Ends.Length != 2) return "Both ends must be configured.";
        // Recipes saved before camera settings existed stay valid; they simply keep the current machine setup.
        if (Camera?.Validate() is { } cameraError) return cameraError;
        return Ends.Select((e, i) => e.Validate() is { } error ? $"End {i + 1}: {error}" : null).FirstOrDefault(e => e != null);
    }
}

public sealed record ImageFrame(int Width, int Height, int Stride, byte[] Bgr,
    Guid Id, DateTimeOffset CapturedAt, string Source)
{
    public void Validate()
    {
        if (Width <= 0 || Height <= 0 || Stride < checked(Width * 3) || Bgr.LongLength != (long)Stride * Height)
            throw new ArgumentException("Invalid BGR24 image buffer.");
    }
}
public sealed record OcrRegion(string Text, double Confidence, PixelPoint[] Box, byte[] CropPng);
public sealed record OcrReading(OcrRegion[] Regions, int Rotation);
public sealed record TextDifference(int Region, string Expected, string Actual, int FirstMismatch);

/// <summary>
/// One measured step of a cycle. Durations come from a monotonic clock, never from wall-clock
/// subtraction, so a time-service correction cannot produce a negative or jumped measurement.
/// </summary>
public sealed record StageTiming(string Stage, double Milliseconds);

/// <summary>Monotonic stopwatch time. Use this for every duration; use UtcNow only to stamp evidence.</summary>
public static class MonotonicClock
{
    public static long Now => System.Diagnostics.Stopwatch.GetTimestamp();
    public static double MillisecondsSince(long start) => Milliseconds(start, Now);
    public static double Milliseconds(long from, long to) =>
        Math.Max(0, to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
}

public sealed record EndResult(Guid FrameId, Verdict Verdict, OcrReading Reading, TextDifference[] Differences, string Reason,
    StageTiming[]? Timings = null)
{
    public double MillisecondsOf(string stage) => Timings?.FirstOrDefault(t => t.Stage == stage)?.Milliseconds ?? 0;
}
public sealed record CaptureEvidence(Guid FrameId, DateTimeOffset CapturedAt, string Source, int Width, int Height);
public sealed record ProductResult(Guid CycleId, Recipe Recipe, EndResult[] Ends, DateTimeOffset CompletedAt,
    CaptureEvidence[]? Captures = null, StageTiming[]? Timings = null)
{
    public Verdict Verdict => Ends.Length != 2 ? Verdict.Error :
        Ends.Any(e => e.Verdict == Verdict.Error) ? Verdict.Error :
        Ends.All(e => e.Verdict == Verdict.Ok) ? Verdict.Ok : Verdict.Ng;
}

public static class ExactTextComparer
{
    public static EndResult Compare(ImageFrame frame, EndRecipe recipe, OcrReading reading)
    {
        var differences = new List<TextDifference>();
        for (var i = 0; i < Math.Max(recipe.ExpectedLines.Length, reading.Regions.Length); i++)
        {
            var expected = i < recipe.ExpectedLines.Length ? recipe.ExpectedLines[i] : "";
            var actual = i < reading.Regions.Length ? reading.Regions[i].Text : "";
            if (i < recipe.ExpectedLines.Length && i < reading.Regions.Length &&
                string.Equals(expected, actual, StringComparison.Ordinal)) continue;
            var at = 0;
            while (at < Math.Min(expected.Length, actual.Length) && expected[at] == actual[at]) at++;
            differences.Add(new(i + 1, expected, actual, at));
        }
        var requiredRotation = recipe.Orientation switch
        {
            TextOrientation.Degrees0 => 0,
            TextOrientation.Degrees180 => 180,
            TextOrientation.Auto => (int?)null,
            _ => throw new ArgumentOutOfRangeException(nameof(recipe), "Unsupported text orientation.")
        };
        var validRotation = reading.Rotation is 0 or 180;
        var rotationMatches = validRotation && (requiredRotation==null||reading.Rotation==requiredRotation);
        var textMatches = differences.Count==0&&reading.Regions.Length>0;
        var ok = textMatches&&rotationMatches;
        var orientationReason = requiredRotation==null
            ? $"Detected rotation must be 0° or 180°; actual {reading.Rotation}°"
            : $"Text orientation does not match: expected {requiredRotation}°, actual {reading.Rotation}°";
        var reason = ok ? "Exact text and orientation match" :
            reading.Regions.Length==0 ? "No text detected" :
            !textMatches&&!rotationMatches ? $"Text/region count and orientation do not match; {orientationReason}" :
            !textMatches ? "Text or region count does not match" :
            orientationReason;
        return new(frame.Id, ok ? Verdict.Ok : Verdict.Ng, reading, [.. differences],
            reason);
    }
}
