using WireMarkerInspection.Domain;
namespace WireMarkerInspection.Application;

public interface IOcrEngine
{
    Task<OcrReading> ReadAsync(ImageFrame frame, EndRecipe recipe, CancellationToken cancellationToken);
}
public interface IRecipeStore
{
    IReadOnlyList<Recipe> LoadAll();
    Recipe Save(Recipe recipe, byte[][] referencePngs);
    byte[] LoadReference(Recipe recipe, int end);
    void Delete(Guid id);
}
public interface IResultStore
{
    Task SaveAsync(ProductResult result, ImageFrame[] frames, CancellationToken cancellationToken);
}
public sealed record CameraDevice(string Id, string Name, string Backend, bool IsSimulation);

/// <summary>
/// Writable range of one acquisition parameter, read from the device itself so the UI can show the
/// real limits instead of hard-coded numbers.
/// </summary>
public sealed record CameraParameterInfo(string Name, string Unit, double Minimum, double Maximum,
    double Increment, double Value, bool Writable)
{
    public string? Validate(double value) =>
        !Writable ? $"{Name} is read-only on this camera." :
        !double.IsFinite(value) ? $"{Name} must be a number." :
        value < Minimum || value > Maximum ? $"{Name} must be between {Minimum:0.###} and {Maximum:0.###} {Unit}".TrimEnd() + "." :
        null;
}

/// <summary>Read-only identity and state of the open camera.</summary>
public sealed record CameraInfo(string Model, string Serial, string PixelFormat,
    int SensorWidth, int SensorHeight, double? FrameRate, double? TemperatureCelsius);

public interface ICamera : IDisposable
{
    IReadOnlyList<CameraDevice> Enumerate();
    void Open(CameraDevice device);
    CameraInfo ReadInfo();
    IReadOnlyList<CameraParameterInfo> DescribeParameters();
    CameraSettings ReadSettings();
    void ApplySettings(CameraSettings settings);
    /// <summary>
    /// Switches between continuous acquisition and a triggered source. Triggered acquisition only
    /// delivers a frame per pulse, so callers must stop treating a quiet camera as a fault.
    /// </summary>
    void ConfigureTrigger(CameraTrigger trigger);
    /// <summary>Fires one software trigger. Only valid while the software trigger source is configured.</summary>
    void ExecuteSoftwareTrigger();
    void Start();
    ImageFrame Grab(int timeoutMs);
    void Stop();
    void Close();
}
