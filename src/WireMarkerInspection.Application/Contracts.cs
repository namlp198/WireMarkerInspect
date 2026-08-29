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
public interface ICamera : IDisposable
{
    IReadOnlyList<CameraDevice> Enumerate();
    void Open(CameraDevice device);
    void SetParameter(string name, string value);
    void Start();
    ImageFrame Grab(int timeoutMs);
    void Stop();
    void Close();
}
