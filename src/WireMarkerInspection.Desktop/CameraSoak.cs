using System.IO;
using System.Text.Json;
using WireMarkerInspection.Application;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Infrastructure;

namespace WireMarkerInspection.Desktop;

/// <summary>
/// Long-run acquisition evidence: frame rate, frame-interval spread, timeouts and recovery over a real
/// period. It reports what happened rather than deciding whether the machine is production ready.
/// </summary>
internal static class CameraSoak
{
    public static async void Run(string output,double minutes)
    {
        var reportPath=Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report=new Dictionary<string,object?>
        {
            ["timestamp"]=DateTimeOffset.Now,["requestedMinutes"]=minutes,["status"]="starting"
        };
        try
        {
            if(!double.IsFinite(minutes)||minutes<=0)throw new ArgumentOutOfRangeException(nameof(minutes),"Soak duration must be positive.");
            using var camera=new HikrobotMvsCamera();
            var devices=await Task.Run(camera.Enumerate);
            report["devices"]=devices;
            if(devices.Count==0)throw new InvalidOperationException("MVS không tìm thấy camera nào.");
            await Task.Run(()=>Measure(camera,devices[0],TimeSpan.FromMinutes(minutes),report));
            report["status"]="pass";
            Write(reportPath,report);System.Windows.Application.Current.Shutdown(0);
        }
        catch(Exception ex)
        {
            report["status"]="fail";report["error"]=ex.ToString();
            Write(reportPath,report);System.Windows.Application.Current.Shutdown(1);
        }
    }

    private static void Measure(HikrobotMvsCamera camera,CameraDevice device,TimeSpan duration,Dictionary<string,object?> report)
    {
        camera.Open(device);
        report["info"]=camera.ReadInfo();
        camera.Start();
        var intervals=new List<double>();
        var temperatures=new List<double>();
        long frames=0,timeouts=0,errors=0;
        string? lastError=null;
        var started=MonotonicClock.Now;
        var previous=0L;
        var nextSample=0.0;
        try
        {
            while(MonotonicClock.MillisecondsSince(started)<duration.TotalMilliseconds)
            {
                try
                {
                    _=camera.Grab(2000);
                    var now=MonotonicClock.Now;
                    if(previous!=0)intervals.Add(MonotonicClock.Milliseconds(previous,now));
                    previous=now;frames++;
                }
                catch(TimeoutException){timeouts++;continue;}
                catch(Exception ex){errors++;lastError=ex.Message;break;}
                var elapsed=MonotonicClock.MillisecondsSince(started);
                if(elapsed>=nextSample)
                {
                    nextSample=elapsed+60000;
                    if(camera.ReadInfo().TemperatureCelsius is{}temperature)temperatures.Add(temperature);
                }
            }
        }
        finally{try{camera.Stop();}catch{/* The report still stands. */}try{camera.Close();}catch{}}

        var elapsedMs=MonotonicClock.MillisecondsSince(started);
        var sorted=intervals.OrderBy(v=>v).ToArray();
        report["elapsedSeconds"]=Math.Round(elapsedMs/1000.0,1);
        report["frames"]=frames;
        report["timeouts"]=timeouts;
        report["errors"]=errors;
        report["lastError"]=lastError;
        report["framesPerSecond"]=elapsedMs>0?Math.Round(frames*1000.0/elapsedMs,3):0;
        report["frameIntervalMs"]=sorted.Length==0?null:new
        {
            min=Math.Round(sorted[0],2),
            average=Math.Round(sorted.Average(),2),
            p95=Math.Round(sorted[Math.Clamp((int)Math.Ceiling(sorted.Length*0.95)-1,0,sorted.Length-1)],2),
            max=Math.Round(sorted[^1],2)
        };
        report["temperatureCelsius"]=temperatures.Count==0?null:new{first=temperatures[0],last=temperatures[^1]};
    }

    private static void Write(string path,Dictionary<string,object?> report)=>
        File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
}
