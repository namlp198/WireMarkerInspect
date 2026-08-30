using System.IO;
using System.Text.Json;
using WireMarkerInspection.Desktop.Services;
using WireMarkerInspection.Infrastructure;

namespace WireMarkerInspection.Desktop;

internal static class CameraProbe
{
    public static async void Run(string output,bool grab,bool softwareTrigger=false)
    {
        var reportPath=Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report=new Dictionary<string,object?>
        {
            ["timestamp"]=DateTimeOffset.Now,
            ["requestedGrab"]=grab,
            ["requestedSoftwareTrigger"]=softwareTrigger,
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
            if(softwareTrigger)
            {
                if(devices.Count==0)throw new InvalidOperationException("MVS không tìm thấy camera nào.");
                await Task.Run(()=>ProbeSoftwareTrigger(camera,devices[0],report));
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
    /// <summary>
    /// Proves the triggered acquisition path against real hardware without any wiring: the camera must
    /// stay silent until a software trigger is issued, and deliver exactly on that trigger.
    /// </summary>
    private static void ProbeSoftwareTrigger(HikrobotMvsCamera camera,WireMarkerInspection.Application.CameraDevice device,Dictionary<string,object?> report)
    {
        var trigger=new Dictionary<string,object?>();
        camera.Open(device);
        try
        {
            camera.ConfigureTrigger(new(WireMarkerInspection.Application.CameraTriggerSource.Software));
            camera.Start();
            try
            {
                var silent=false;
                try{_=camera.Grab(700);}
                catch(TimeoutException){silent=true;}
                trigger["silentWithoutTrigger"]=silent;
                if(!silent)throw new InvalidOperationException("Camera vẫn trả frame khi chưa có trigger.");

                camera.ExecuteSoftwareTrigger();
                var frame=camera.Grab(3000);
                trigger["triggeredFrame"]=new{frame.Width,frame.Height,frame.Stride,frame.Source};
            }
            finally{camera.Stop();}
            camera.ConfigureTrigger(WireMarkerInspection.Application.CameraTrigger.FreeRun);
            camera.Start();
            try{var free=camera.Grab(3000);trigger["freeRunRestored"]=new{free.Width,free.Height};}
            finally{camera.Stop();}
        }
        finally{camera.Close();}
        report["softwareTrigger"]=trigger;
    }

    private static void Write(string path,Dictionary<string,object?> report)=>
        File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
}
