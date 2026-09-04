using System.Runtime.InteropServices;
using WireMarkerInspection.Vision;
using Xunit;

namespace WireMarkerInspection.Tests;
public sealed class NativeMatchingContractTests
{
    [Fact]public void DeployedNativeDllProvidesCompleteMatchingContract()=>Assert.Null(new NativeTemplateMatcher().AvailabilityError);
    [Fact]public void MissingExportsReturnActionableErrorWithoutCallingThem()
    {
        // A real native module without any WMI exports exercises the legacy/missing-export branch.
        var module=NativeLibrary.Load("kernel32.dll");
        try
        {
            var error=NativeTemplateMatcher.CheckLibrary(module);
            Assert.Contains("missing wmi_matching_abi_version",error);
            Assert.Contains("Rebuild",error);Assert.Contains("restart",error);
        }
        finally{NativeLibrary.Free(module);}
    }
}
