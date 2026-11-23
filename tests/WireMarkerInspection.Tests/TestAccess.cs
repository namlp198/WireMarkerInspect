using WireMarkerInspection.Desktop.ViewModels;
using Xunit;

namespace WireMarkerInspection.Tests;

internal static class TestAccess
{
    public static MainViewModel AsAdmin(this MainViewModel viewModel)
    {
        Assert.True(viewModel.TryLogin("admin","admin"));
        return viewModel;
    }
}
