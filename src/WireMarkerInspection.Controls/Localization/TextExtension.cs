using System.Windows.Data;
using System.Windows.Markup;

namespace WireMarkerInspection.Controls.Localization;

public sealed class TextExtension : MarkupExtension
{
    public TextExtension() { }
    public TextExtension(string key)=>Key=key;
    [ConstructorArgument("key")]public string Key{get;set;}=string.Empty;
    public override object ProvideValue(IServiceProvider serviceProvider)=>
        new Binding($"[{Key}]"){Source=LocalizedText.Instance,Mode=BindingMode.OneWay}.ProvideValue(serviceProvider);
}
