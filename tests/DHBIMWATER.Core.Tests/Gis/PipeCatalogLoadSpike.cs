using DHBoost.Revit.Interop;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

public sealed class PipeCatalogLoadSpike
{
    [Fact]
    public void Loads_catalog_defaults_without_Revit()
    {
        var data = PipeCatalogInterop.GetData(PipeCatalogInterop.LoadDefaults());
        Assert.Equal(3, PipeCatalogInterop.ContractVersion);
        Assert.Equal(5, PipeQuantityRulesInterop.ContractVersion);
        Assert.NotEmpty(data.ProjectTables.StraightPipes);
        Assert.NotEmpty(data.ProjectTables.Fittings);
        Assert.NotEmpty(data.ProjectTables.JointDeflections);
    }
}
