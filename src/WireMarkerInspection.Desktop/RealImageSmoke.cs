using System.IO;
using System.Text.Json;
using WireMarkerInspection.Desktop.Services;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Vision;

namespace WireMarkerInspection.Desktop;

internal static class RealImageSmoke
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async void Run(string imageDirectory,string manifestPath,string outputDirectory)
    {
        outputDirectory=Path.GetFullPath(outputDirectory);Directory.CreateDirectory(outputDirectory);
        var resultPath=Path.Combine(outputDirectory,"managed-load-result.txt");
        try
        {
            var manifest=JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(manifestPath),Json)
                ?? throw new InvalidDataException("Real-image manifest is empty.");
            if(manifest.SchemaVersion!=1||manifest.NormalizedRoi.Length!=4)
                throw new InvalidDataException("Unsupported real-image manifest.");
            using var ocr=new NativeOcrEngine(Path.Combine(AppContext.BaseDirectory,"assets","ocr"));
            if(ocr.AvailabilityError is { } issue)throw new InvalidOperationException(issue);
            var rows=new List<string>{"File,Expected,Actual,ExpectedRotation,ActualRotation,Result"};
            var passed=0;
            foreach(var test in manifest.Cases)
            {
                var frame=ImageFiles.Load(Path.Combine(imageDirectory,test.File));
                var r=manifest.NormalizedRoi;
                var roi=new SearchRoi(RoiShape.Rectangle,
                [
                    new(r[0]*frame.Width,r[1]*frame.Height),
                    new(r[2]*frame.Width,r[3]*frame.Height)
                ]);
                var recipe=new EndRecipe(test.File,frame.Width,frame.Height,roi,test.Regions,
                    (TextOrientation)manifest.Orientation);
                var reading=await ocr.ReadAsync(frame,recipe,CancellationToken.None);
                var comparison=ExactTextComparer.Compare(frame,recipe,reading);
                var ok=comparison.Verdict==Verdict.Ok&&reading.Rotation==test.Rotation;
                if(ok)passed++;
                rows.Add(string.Join(',',Csv(test.File),Csv(string.Join(" | ",test.Regions)),
                    Csv(string.Join(" | ",reading.Regions.Select(x=>x.Text))),test.Rotation,reading.Rotation,ok?"PASS":"FAIL"));
            }
            await File.WriteAllLinesAsync(Path.Combine(outputDirectory,"managed-load.csv"),rows);
            var summary=$"Managed Load Image OCR: {passed}/{manifest.Cases.Length} passed.";
            await File.WriteAllTextAsync(resultPath,summary);
            System.Windows.Application.Current.Shutdown(passed==manifest.Cases.Length?0:1);
        }
        catch(Exception error)
        {
            await File.WriteAllTextAsync(resultPath,error.ToString());
            System.Windows.Application.Current.Shutdown(1);
        }
    }

    private static string Csv(object? value)=>$"\"{value?.ToString()?.Replace("\"","\"\"")}\"";
    private sealed record Manifest(int SchemaVersion,int Orientation,double[] NormalizedRoi,Case[] Cases);
    private sealed record Case(string File,int Rotation,string[] Regions);
}
