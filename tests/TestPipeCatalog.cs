using System.Runtime.CompilerServices;
using DHBoost.Revit.Interop;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Revit.Interop;

internal static class TestPipeCatalog
{
    [ModuleInitializer]
    internal static void EnsureInstalled()
    {
        var data = PipeCatalogInterop.GetData(PipeCatalogInterop.LoadDefaults());
        var warnings = PipeCatalog.Install(PipeCatalogDataMapper.Map(data));
        if (warnings.Count != 0) throw new InvalidOperationException(string.Join("; ", warnings));
    }
}
