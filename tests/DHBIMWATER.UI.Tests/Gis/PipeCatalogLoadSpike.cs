using DHBoost.Revit.Interop;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

public sealed class PipeCatalogLoadSpike
{
    [Fact]
    public void Loads_catalog_defaults_without_Revit()
    {
        var data = PipeCatalogInterop.GetData(PipeCatalogInterop.LoadDefaults());
        Assert.Equal(2, PipeCatalogInterop.ContractVersion);
        Assert.Equal(2, PipeQuantityRulesInterop.ContractVersion);
        Assert.NotEmpty(data.ProjectTables.StraightPipes);
        Assert.NotEmpty(data.ProjectTables.Fittings);
        Assert.NotEmpty(data.ProjectTables.JointDeflections);
    }
}
