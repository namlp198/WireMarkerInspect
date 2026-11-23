using System.IO;

namespace WireMarkerInspection.Controls.Localization;

public static class LanguagePreferenceFile
{
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WireMarkerInspection", "language.txt");

    public static AppLanguage Load(string path)
    {
        try
        {
            if(File.Exists(path))
                return File.ReadAllText(path).Trim() switch
                {
                    "en" => AppLanguage.English,
                    "ko" => AppLanguage.Korean,
                    _ => AppLanguage.Vietnamese
                };
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException) { }
        return AppLanguage.Vietnamese;
    }

    public static bool Save(string path,AppLanguage language)
    {
        var code=language switch
        {
            AppLanguage.Vietnamese => "vi",
            AppLanguage.English => "en",
            AppLanguage.Korean => "ko",
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path,code);
            return true;
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException) { return false; }
    }
}
