using System.Text.Json;
using WireMarkerInspection.Application;
using WireMarkerInspection.Domain;
namespace WireMarkerInspection.Infrastructure;

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WireMarkerInspection");
    public static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class FileRecipeStore(string root) : IRecipeStore
{
    private readonly string directory = Path.Combine(root, "recipes");
    public List<string> LoadErrors { get; } = [];
    public IReadOnlyList<Recipe> LoadAll()
    {
        Directory.CreateDirectory(directory); LoadErrors.Clear();
        var values = new List<Recipe>();
        foreach(var path in Directory.EnumerateFiles(directory, "recipe.json", SearchOption.AllDirectories))
        {
            try
            {
                var recipe = JsonSerializer.Deserialize<Recipe>(File.ReadAllBytes(path), JsonFiles.Options)
                    ?? throw new InvalidDataException("Empty recipe.");
                if (recipe.Validate() is { } error) throw new InvalidDataException(error);
                for(var end=0; end<2; end++) _ = ReferencePath(recipe, end);
                if (values.Any(r => r.Id == recipe.Id || string.Equals(r.ModelCode, recipe.ModelCode, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Duplicate model identity.");
                values.Add(recipe);
            }
            catch(Exception ex) { LoadErrors.Add($"{path}: {ex.Message}"); }
        }
        return values.OrderBy(r => r.ModelCode, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public Recipe Save(Recipe recipe, byte[][] referencePngs)
    {
        if (recipe.Validate() is { } error) throw new InvalidDataException(error);
        if (referencePngs.Length != 2 || referencePngs.Any(b => b.Length == 0))
            throw new InvalidDataException("Both reference images are required.");
        var all = LoadAll();
        if (all.Any(r => r.Id != recipe.Id && string.Equals(r.ModelCode, recipe.ModelCode, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Model code already exists.");
        var folder = Path.Combine(directory, recipe.Id.ToString("N"));
        Directory.CreateDirectory(folder);
        var previous = all.FirstOrDefault(r => r.Id == recipe.Id);
        if (previous != null && previous.Revision != recipe.Revision)
            throw new InvalidDataException("Recipe changed on disk. Reload before saving.");
        var revision = (previous?.Revision ?? 0) + 1;
        var generation = Guid.NewGuid().ToString("N");
        var updated = recipe.Copy() with { Revision = revision, SavedAt = DateTimeOffset.UtcNow };
        for(var i=0; i<2; i++)
        {
            var name = $"end{i + 1}-{generation}.png";
            JsonFiles.AtomicWrite(Path.Combine(folder, name), referencePngs[i]);
            updated.Ends[i] = updated.Ends[i] with { ReferenceImage = name };
        }
        // Publish only after both immutable image files exist. Old versions remain recoverable.
        JsonFiles.AtomicWrite(Path.Combine(folder, "recipe.json"), JsonSerializer.SerializeToUtf8Bytes(updated, JsonFiles.Options));
        return updated;
    }
    private string ReferencePath(Recipe recipe, int end)
    {
        var name = recipe.Ends[end].ReferenceImage;
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || Path.IsPathRooted(name))
            throw new InvalidDataException("Invalid reference image path.");
        var path = Path.Combine(directory, recipe.Id.ToString("N"), name);
        if (!File.Exists(path)) throw new FileNotFoundException("Reference image missing.", path);
        return path;
    }
    public byte[] LoadReference(Recipe recipe, int end) => File.ReadAllBytes(ReferencePath(recipe, end));
    public void Delete(Guid id)
    {
        var file = Path.Combine(directory, id.ToString("N"), "recipe.json");
        if (File.Exists(file)) File.Move(file, Path.Combine(Path.GetDirectoryName(file)!, $"deleted-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json"));
    }
}

public sealed class FileResultStore(string root) : IResultStore
{
    public async Task SaveAsync(ProductResult result, ImageFrame[] frames, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(root, "results", result.CompletedAt.ToString("yyyy-MM-dd"), result.CycleId.ToString("N"));
        Directory.CreateDirectory(folder);
        for(var i=0; i<frames.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // PPM keeps the original pixels without a managed image-processing dependency.
            var f = frames[i];
            await using var output = File.Create(Path.Combine(folder, $"end{i+1}.ppm"));
            await output.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"P6\n{f.Width} {f.Height}\n255\n"), cancellationToken);
            var row = new byte[f.Width * 3];
            for(var y=0; y<f.Height; y++)
            {
                for(var x=0; x<f.Width; x++)
                {
                    row[x*3] = f.Bgr[y*f.Stride+x*3+2]; row[x*3+1] = f.Bgr[y*f.Stride+x*3+1]; row[x*3+2] = f.Bgr[y*f.Stride+x*3];
                }
                await output.WriteAsync(row, cancellationToken);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        JsonFiles.AtomicWrite(Path.Combine(folder,"result.json"), JsonSerializer.SerializeToUtf8Bytes(result, JsonFiles.Options));
    }
}

/// <summary>
/// Append-only JSON Lines diagnostics, one file per day. Soak evidence has to survive the process that
/// produced it, and one line per event keeps a partially written file readable.
/// </summary>
public sealed class FileDiagnosticsLog(string root) : IDiagnosticsLog
{
    private readonly object gate = new();
    private readonly string directory = Path.Combine(root, "diagnostics");

    public void Write(string category, string message, IReadOnlyDictionary<string, object?>? data = null)
    {
        var entry = new Dictionary<string, object?>
        {
            ["at"] = DateTimeOffset.Now, ["category"] = category, ["message"] = message
        };
        if (data != null) foreach (var pair in data) entry[pair.Key] = pair.Value;
        var line = JsonSerializer.Serialize(entry, JsonFiles.Options).ReplaceLineEndings(" ") + Environment.NewLine;
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, $"{DateTimeOffset.Now:yyyy-MM-dd}.jsonl"), line);
            }
            catch (IOException) { /* Diagnostics must never take down an inspection cycle. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// Machine-level configuration: which PLC this station talks to and how RUN is triggered. It belongs to
/// the machine, not to a model, so it lives beside the recipes rather than inside them.
/// </summary>
public sealed record MachineSettings(TriggerSettings Trigger, PlcSettings Plc)
{
    public static MachineSettings Default => new(new TriggerSettings(), new PlcSettings());
}

public sealed class FileSettingsStore(string root)
{
    private readonly string path = Path.Combine(root, "settings.json");
    public string? LoadError { get; private set; }

    public MachineSettings Load()
    {
        LoadError = null;
        if (!File.Exists(path)) return MachineSettings.Default;
        try
        {
            return JsonSerializer.Deserialize<MachineSettings>(File.ReadAllBytes(path), JsonFiles.Options)
                ?? MachineSettings.Default;
        }
        catch (Exception ex)
        {
            // A broken settings file must not stop the station; it falls back and says so.
            LoadError = $"{path}: {ex.Message}";
            return MachineSettings.Default;
        }
    }

    public void Save(MachineSettings settings) =>
        JsonFiles.AtomicWrite(path, JsonSerializer.SerializeToUtf8Bytes(settings, JsonFiles.Options));
}
