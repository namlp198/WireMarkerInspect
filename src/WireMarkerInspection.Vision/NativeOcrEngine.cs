using System.Runtime.InteropServices;
using System.Text.Json;
using WireMarkerInspection.Application;
using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Vision;
public sealed class NativeOcrEngine(string modelDirectory) : IOcrEngine, IDisposable
{
    private readonly SemaphoreSlim gate = new(1,1);
    private IntPtr handle;
    private bool disposed;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public string? AvailabilityError
    {
        get
        {
            foreach(var name in new[] { "detector.onnx", "recognizer.onnx", "dictionary.txt" })
                if(!File.Exists(Path.Combine(modelDirectory,name))) return $"Missing OCR asset: {Path.Combine(modelDirectory,name)}";
            try { return Native.wmi_abi_version() == 1 ? null : "Unsupported native OCR ABI."; }
            catch(Exception ex) { return $"Native OCR unavailable: {ex.Message}"; }
        }
    }
    public async Task<OcrReading> ReadAsync(ImageFrame frame, EndRecipe recipe, CancellationToken cancellationToken)
    {
        frame.Validate();
        if(recipe.Roi.Validate(frame.Width,frame.Height) is { } error) throw new ArgumentException(error);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(handle==IntPtr.Zero)
                {
                    if(AvailabilityError is { } issue) throw new InvalidOperationException(issue);
                    handle=Native.wmi_create(Path.Combine(modelDirectory,"detector.onnx"),Path.Combine(modelDirectory,"recognizer.onnx"),
                        Path.Combine(modelDirectory,"dictionary.txt"),out var message);
                    var detail=Take(message);
                    if(handle==IntPtr.Zero) throw new InvalidOperationException(detail ?? "OCR engine initialization failed.");
                }
                var xy=recipe.Roi.Points.SelectMany(p=>new[]{p.X,p.Y}).ToArray();
                var payload=Take(Native.wmi_inspect(handle,frame.Bgr,frame.Width,frame.Height,frame.Stride,
                    (int)recipe.Roi.Shape,xy,recipe.Roi.Points.Length,(int)recipe.Orientation)) ?? throw new InvalidDataException("Empty native response.");
                cancellationToken.ThrowIfCancellationRequested();
                using var doc=JsonDocument.Parse(payload);
                if(doc.RootElement.TryGetProperty("error",out var failure)) throw new InvalidOperationException(failure.GetString());
                return JsonSerializer.Deserialize<OcrReading>(payload,Json) ?? throw new InvalidDataException("Invalid OCR response.");
            },cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private static string? Take(IntPtr p) { if(p==IntPtr.Zero)return null; try{return Marshal.PtrToStringUTF8(p);}finally{Native.wmi_free(p);} }
    public void Dispose()
    {
        gate.Wait();
        try { if(disposed)return; disposed=true; if(handle!=IntPtr.Zero)Native.wmi_destroy(handle); handle=IntPtr.Zero; }
        finally {gate.Release();}
    }
    private static class Native
    {
        private const string Dll="WireMarkerVision";
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int wmi_abi_version();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
        internal static extern IntPtr wmi_create(string det,string rec,string dict,out IntPtr error);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void wmi_destroy(IntPtr handle);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void wmi_free(IntPtr pointer);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]
        internal static extern IntPtr wmi_inspect(IntPtr handle,byte[] bgr,int width,int height,int stride,int shape,
            double[] xy,int points,int orientation);
    }
}
