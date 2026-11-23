using System.IO;
using System.Text.Json;
using WireMarkerInspection.Application;
using WireMarkerInspection.Infrastructure;

namespace WireMarkerInspection.Desktop;

/// <summary>
/// Hardware acceptance for the PLC link, mirroring the camera probe. Reading is always safe; writing is
/// only attempted when an address is passed explicitly, because a write can move machinery.
/// </summary>
internal static class PlcProbe
{
    public static async void Run(string output,string readAddress,string? writeAddress)
    {
        var reportPath=Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report=new Dictionary<string,object?>
        {
            ["timestamp"]=DateTimeOffset.Now,["readAddress"]=readAddress,
            ["writeAddress"]=writeAddress,["status"]="starting"
        };
        try
        {
            var settings=new FileSettingsStore(JsonFiles.DataRoot).Load().Plc;
            report["plc"]=settings.Describe();
            if(settings.ValidateConnection() is{}invalid)throw new InvalidOperationException(invalid);
            var map=PlcAddressMaps.For(settings.Vendor);
            report["resolvedRead"]=map.Translate(readAddress);
            await using var link=new ModbusPlcLink(settings,map);
            await link.ConnectAsync(CancellationToken.None);
            report["connected"]=true;
            report["readValue"]=await link.ReadBitAsync(readAddress,CancellationToken.None);
            if(!string.IsNullOrWhiteSpace(writeAddress))
            {
                report["resolvedWrite"]=map.Translate(writeAddress);
                await link.WriteBitAsync(writeAddress,true,CancellationToken.None);
                await Task.Delay(200);
                await link.WriteBitAsync(writeAddress,false,CancellationToken.None);
                report["writePulsed"]=true;
            }
            await link.DisconnectAsync();
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
