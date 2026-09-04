using System.Runtime.InteropServices;
using System.Text.Json;
using WireMarkerInspection.Application;
using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Vision;

public sealed class NativeTemplateMatcher : ITemplateMatcher
{
    private readonly SemaphoreSlim gate=new(1,1);
    private static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
    public string? AvailabilityError
    {
        get
        {
            IntPtr library=IntPtr.Zero;
            try
            {
                // Resolve exactly as this assembly's P/Invoke does, and inspect before calling.
                // Older OCR-only DLLs must produce an actionable error, not EntryPointNotFoundException.
                library=NativeLibrary.Load("WireMarkerVision",typeof(NativeTemplateMatcher).Assembly,null);
                return CheckLibrary(library);
            }
            catch(Exception ex){return $"Native matching unavailable in {AppContext.BaseDirectory}: {ex.Message}";}
            finally{if(library!=IntPtr.Zero)NativeLibrary.Free(library);}
        }
    }
    internal static string? CheckLibrary(IntPtr library)
    {
        foreach(var name in new[]{"wmi_matching_abi_version","wmi_match","wmi_free"})
            if(!NativeLibrary.TryGetExport(library,name,out _))
                return $"WireMarkerVision.dll is outdated (missing {name}). Rebuild the active Debug/Release configuration with scripts/build.ps1, then restart the app. Output: {AppContext.BaseDirectory}";
        var version=Marshal.GetDelegateForFunctionPointer<AbiVersion>(NativeLibrary.GetExport(library,"wmi_matching_abi_version"))();
        return version==1?null:$"Unsupported matching ABI {version}; rebuild native vision and restart the app.";
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int AbiVersion();
    public async Task<TemplateMatchResult> MatchAsync(ImageFrame frame,TerminalTemplate template,CancellationToken cancellationToken)
    {
        frame.Validate();
        var snapshot=template.Copy();
        if(!snapshot.Enabled)throw new ArgumentException("Template matching is disabled.");
        if(snapshot.Validate(frame.Width,frame.Height) is {} error)throw new ArgumentException(error);
        if(AvailabilityError is{}availabilityError)throw new InvalidOperationException(availabilityError);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(()=>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var learn=snapshot.LearnRoi!;var search=snapshot.SearchRoi!;var p=snapshot.ActiveParameters();
                var pointer=Native.wmi_match(frame.Bgr,frame.Width,frame.Height,frame.Stride,snapshot.TemplatePng,snapshot.TemplatePng.Length,
                    (int)learn.Shape,learn.Points.SelectMany(v=>new[]{v.X,v.Y}).ToArray(),learn.Points.Length,
                    (int)search.Shape,search.Points.SelectMany(v=>new[]{v.X,v.Y}).ToArray(),search.Points.Length,
                    (int)snapshot.Algorithm,MatchingParameters.Definitions.Select(d=>p[d.Key]).ToArray(),MatchingParameters.Definitions.Length);
                string payload;
                try{payload=Marshal.PtrToStringUTF8(pointer)??throw new InvalidDataException("Empty matching response.");}
                finally{if(pointer!=IntPtr.Zero)Native.wmi_free(pointer);}
                cancellationToken.ThrowIfCancellationRequested();
                using var doc=JsonDocument.Parse(payload);
                if(doc.RootElement.TryGetProperty("error",out var failure))throw new InvalidOperationException(failure.GetString());
                return JsonSerializer.Deserialize<TemplateMatchResult>(payload,Json)??throw new InvalidDataException("Invalid matching response.");
            },cancellationToken).ConfigureAwait(false);
        }
        finally{gate.Release();}
    }
    private static class Native
    {
        private const string Dll="WireMarkerVision";
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void wmi_free(IntPtr pointer);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr wmi_match(byte[] bgr,int width,int height,int stride,
            byte[] png,int length,int learnShape,double[] learnXY,int learnCount,int searchShape,double[] searchXY,int searchCount,
            int algorithm,double[] parameters,int parameterCount);
    }
}
