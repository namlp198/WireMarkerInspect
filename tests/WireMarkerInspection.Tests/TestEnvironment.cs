using System.Runtime.CompilerServices;
using WireMarkerInspection.Controls.Localization;

namespace WireMarkerInspection.Tests;

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()=>AppLocalizer.ChangeLanguage(AppLanguage.Vietnamese,persist:false);
}
