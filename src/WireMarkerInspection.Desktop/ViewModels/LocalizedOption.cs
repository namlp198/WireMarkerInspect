using CommunityToolkit.Mvvm.ComponentModel;
using WireMarkerInspection.Controls.Localization;

namespace WireMarkerInspection.Desktop.ViewModels;

/// <summary>
/// Keeps a ComboBox item and its semantic value stable while only its translated label changes.
/// Replacing ItemsSource during a language switch can make WPF temporarily write the enum default
/// back through SelectedValue; refreshing this item in place avoids any recipe mutation.
/// </summary>
public sealed class LocalizedOption<T>(T value,string labelKey) : ObservableObject where T : struct,Enum
{
    public T Value{get;}=value;
    public string Label=>AppLocalizer.Text(labelKey);
    public void RefreshLabel()=>OnPropertyChanged(nameof(Label));
}
