using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace WireMarkerInspection.Controls.Localization;

public static class AppLocalizer
{
    private const string CatalogFileName="lang.csv";
    private static readonly object Gate=new();
    private static readonly Dictionary<AppLanguage,Dictionary<string,string>> Texts=new()
    {
        [AppLanguage.Vietnamese]=new(StringComparer.OrdinalIgnoreCase),
        [AppLanguage.English]=new(StringComparer.OrdinalIgnoreCase),
        [AppLanguage.Korean]=new(StringComparer.OrdinalIgnoreCase)
    };
    private static AppLanguage currentLanguage=LanguagePreferenceFile.Load(LanguagePreferenceFile.DefaultFilePath);
    private static DateTime lastWriteUtc=DateTime.MinValue;
    private static FileSystemWatcher? watcher;
    private static bool loaded;

    public static event EventHandler? LanguageChanged;

    public static AppLanguage CurrentLanguage
    {
        get=>currentLanguage;
        set=>ChangeLanguage(value,persist:true);
    }

    public static void ChangeLanguage(AppLanguage language,bool persist)
    {
        if(!Enum.IsDefined(language))throw new ArgumentOutOfRangeException(nameof(language));
        EnsureLoaded();
        if(currentLanguage==language)return;
        currentLanguage=language;
        if(persist)LanguagePreferenceFile.Save(LanguagePreferenceFile.DefaultFilePath,language);
        RaiseChanged();
    }

    public static string Text(string key)
    {
        EnsureLoaded();
        if(Texts[currentLanguage].TryGetValue(key,out var value)&&!string.IsNullOrWhiteSpace(value))return value;
        return Texts[AppLanguage.English].TryGetValue(key,out value)&&!string.IsNullOrWhiteSpace(value)?value:key;
    }

    public static string Format(string key,params object[] args)=>
        string.Format(CultureInfo.InvariantCulture,Text(key),args);

    public static void Reload()
    {
        lock(Gate)Load(force:true);
        RaiseChanged();
    }

    private static void EnsureLoaded(){lock(Gate)Load(force:false);}
    private static void Load(bool force)
    {
        var path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,CatalogFileName);
        if(!File.Exists(path))
        {
            loaded=true;EnsureWatcher(path);return;
        }
        var write=File.GetLastWriteTimeUtc(path);
        if(!force&&loaded&&write==lastWriteUtc)return;
        string[] lines;
        try{lines=File.ReadAllLines(path,Encoding.UTF8);}
        catch(IOException){return;}
        var en=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var vi=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var ko=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        for(var index=0;index<lines.Length;index++)
        {
            if(string.IsNullOrWhiteSpace(lines[index]))continue;
            var cells=ParseCsv(lines[index]);
            if(cells.Length<4||(index==0&&cells[0].Equals("key",StringComparison.OrdinalIgnoreCase)))continue;
            var key=cells[0].Trim();if(key.Length==0)continue;
            en[key]=cells[1];vi[key]=cells[2];ko[key]=cells[3];
        }
        Texts[AppLanguage.English]=en;Texts[AppLanguage.Vietnamese]=vi;Texts[AppLanguage.Korean]=ko;
        lastWriteUtc=write;loaded=true;EnsureWatcher(path);
    }

    private static string[] ParseCsv(string line)
    {
        var cells=new List<string>();var cell=new StringBuilder();var quoted=false;
        for(var index=0;index<line.Length;index++)
        {
            var character=line[index];
            if(character=='\"')
            {
                if(quoted&&index+1<line.Length&&line[index+1]=='\"'){cell.Append('\"');index++;}
                else quoted=!quoted;
            }
            else if(character==','&&!quoted){cells.Add(cell.ToString());cell.Clear();}
            else cell.Append(character);
        }
        cells.Add(cell.ToString());return [..cells];
    }

    private static void EnsureWatcher(string path)
    {
        if(watcher!=null)return;
        var directory=Path.GetDirectoryName(path);
        if(string.IsNullOrWhiteSpace(directory)||!Directory.Exists(directory))return;
        watcher=new FileSystemWatcher(directory,CatalogFileName)
        {
            NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size,
            EnableRaisingEvents=true
        };
        watcher.Changed+=OnCatalogChanged;watcher.Created+=OnCatalogChanged;watcher.Renamed+=OnCatalogChanged;
    }
    private static void OnCatalogChanged(object sender,FileSystemEventArgs args)=>Reload();
    private static void RaiseChanged()
    {
        var dispatcher=Application.Current?.Dispatcher;
        if(dispatcher==null||dispatcher.CheckAccess())LanguageChanged?.Invoke(null,EventArgs.Empty);
        else dispatcher.BeginInvoke(new Action(()=>LanguageChanged?.Invoke(null,EventArgs.Empty)));
    }
}
