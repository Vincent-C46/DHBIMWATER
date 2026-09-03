using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Parameters;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

public sealed class PlacementStationParametersTests
{
    [Fact]
    public void DHBoost_계약_이름_GUID_타입이_고정된다()
    {
        Assert.Equal(12, PlacementStationParameters.Definitions.Count);
        Assert.Equal(12, PlacementStationParameters.Guids.Values.Distinct().Count());
        Assert.Equal(new Guid("53dfa8cd-0583-4a4b-90a9-31762c06eb2c"), PlacementStationParameters.Guids[PlacementStationParameters.SourceFile]);
        Assert.Equal(new Guid("cb041b23-f945-4e3d-a3fe-0f74ff49ae94"), PlacementStationParameters.Guids[PlacementStationParameters.StaStart]);

        AssertDefinition(PlacementStationParameters.SourceFile, ParameterSpecType.Text);
        AssertDefinition(PlacementStationParameters.Alignment, ParameterSpecType.Text);
        AssertDefinition(PlacementStationParameters.Profile, ParameterSpecType.Text);
        AssertDefinition(PlacementStationParameters.StaStart, ParameterSpecType.Number);
        AssertDefinition(PlacementStationParameters.StaEnd, ParameterSpecType.Number);
        AssertDefinition(PlacementStationParameters.ZOffset, ParameterSpecType.Number);
        AssertDefinition(PlacementStationParameters.Flip, ParameterSpecType.YesNo);
        AssertDefinition(PlacementStationParameters.VerticalBasis, ParameterSpecType.Text);
        AssertDefinition(PlacementStationParameters.ShapeKind, ParameterSpecType.Text);
        AssertDefinition(PlacementStationParameters.AlignmentId, ParameterSpecType.Text);
        AssertDefinition(PlacementStationParameters.SourceKind, ParameterSpecType.Text);
        AssertDefinition(PlacementStationParameters.SourceOptions, ParameterSpecType.Text);
    }

    private static void AssertDefinition(string name, ParameterSpecType spec)
    {
        var definition = Assert.Single(PlacementStationParameters.Definitions, x => x.Name == name);
        Assert.Equal(PlacementStationParameters.Guids[name], definition.Guid);
        Assert.Equal(spec, definition.SpecType);
        Assert.Equal(ParameterBindingType.Instance, definition.BindingType);
        Assert.Equal([ParameterCategory.GenericModel], definition.Categories);
    }
}
