using System.IO;
using System.Text.Json;
using WireMarkerInspection.Desktop.Services;
using WireMarkerInspection.Infrastructure;

namespace WireMarkerInspection.Desktop;

internal static class CameraProbe
{
    public static async void Run(string output,bool grab)
    {
        var reportPath=Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report=new Dictionary<string,object?>
        {
            ["timestamp"]=DateTimeOffset.Now,
            ["requestedGrab"]=grab,
            ["status"]="starting"
        };
        try
        {
            using var camera=new HikrobotMvsCamera();
            var devices=await Task.Run(camera.Enumerate);
            report["devices"]=devices;
            report["deviceCount"]=devices.Count;
            if(grab)
            {
                if(devices.Count==0)throw new InvalidOperationException("MVS không tìm thấy camera nào.");
                await Task.Run(()=>
                {
                    camera.Open(devices[0]);
                    camera.ApplySettings(new WireMarkerInspection.Domain.CameraSettings(10000,0));
                    report["info"]=camera.ReadInfo();
                    report["parameters"]=camera.DescribeParameters();
                    camera.Start();
                    try
                    {
                        var frames=Enumerable.Range(0,3).Select(_=>camera.Grab(3000)).ToArray();
                        var frame=frames[^1];
                        var imagePath=Path.ChangeExtension(reportPath,".png");
                        File.WriteAllBytes(imagePath,ImageFiles.Png(ImageFiles.Bitmap(frame)));
                        report["frames"]=frames.Select(value=>new{value.Id,value.Width,value.Height,value.Stride,value.Source,value.CapturedAt}).ToArray();
                        report["image"]=imagePath;
                    }
                    finally{camera.Stop();camera.Close();}
                });
            }
            report["status"]="pass";
            Write(reportPath,report);System.Windows.Application.Current.Shutdown(0);
        }
        catch(Exception ex)
        {
            report["status"]="fail";report["error"]=ex.ToString();
            Write(reportPath,report);System.Windows.Application.Current.Shutdown(1);
        }
    }
    private static void Write(string path,Dictionary<string,object?> report)=>
        File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
}
