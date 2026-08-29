using System.Windows;
using System.Windows.Controls;

namespace WireMarkerInspection.Controls;

/// <summary>Shape-selectable taskbar step. The first step uses Straight; following steps use Notched.</summary>
public enum ChevronTailMode { Notched, Straight }

public sealed class ChevronButton : Button
{
    public static readonly DependencyProperty TailModeProperty = DependencyProperty.Register(
        nameof(TailMode),typeof(ChevronTailMode),typeof(ChevronButton),
        new FrameworkPropertyMetadata(ChevronTailMode.Notched));

    public ChevronTailMode TailMode
    {
        get=>(ChevronTailMode)GetValue(TailModeProperty);
        set=>SetValue(TailModeProperty,value);
    }
}
