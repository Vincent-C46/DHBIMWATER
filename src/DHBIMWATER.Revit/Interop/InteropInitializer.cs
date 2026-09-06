using System.Reflection;
using System.Runtime.CompilerServices;

namespace DHBIMWATER.Revit.Interop;

internal static class InteropInitializer
{
    private static readonly Lazy<Assembly> Assembly = new(() =>
    {
        var assembly = System.Reflection.Assembly.Load("DHBoost.Revit");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        return assembly;
    });

    public static void EnsureInitialized() => _ = Assembly.Value;
}
