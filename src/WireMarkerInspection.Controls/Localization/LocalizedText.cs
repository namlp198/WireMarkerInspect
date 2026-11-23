using System.ComponentModel;

namespace WireMarkerInspection.Controls.Localization;

public sealed class LocalizedText : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizedText> Singleton=new(()=>new LocalizedText());
    private LocalizedText()=>AppLocalizer.LanguageChanged+=(_,_)=>
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs("Item[]"));
    public static LocalizedText Instance=>Singleton.Value;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key]=>AppLocalizer.Text(key??string.Empty);
}
