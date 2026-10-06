using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JointSlotParameterTests
{
    [Fact]
    public void SlotNames_FollowScanPattern()
    {
        Assert.Equal("DH_접합2_종류", PipeAlignmentParameters.JointSlotKind(2));
        Assert.Equal("DH_접합2_규격", PipeAlignmentParameters.JointSlotSpec(2));
        Assert.Equal("DH_접합2_수", PipeAlignmentParameters.JointSlotCount(2));
    }

    [Fact]
    public void SlotDefinitions_HaveExpectedTypesAndUniqueGuids()
    {
        Assert.Equal(PipeAlignmentParameters.Guids.Count,
            PipeAlignmentParameters.Guids.Values.Distinct().Count());
        for (var slot = 1; slot <= PipeAlignmentParameters.JointSlotDefinitionCount; slot++)
        {
            Assert.Equal(ParameterSpecType.Text, SpecOf(PipeAlignmentParameters.JointSlotKind(slot)));
            Assert.Equal(ParameterSpecType.Text, SpecOf(PipeAlignmentParameters.JointSlotSpec(slot)));
            Assert.Equal(ParameterSpecType.Integer, SpecOf(PipeAlignmentParameters.JointSlotCount(slot)));
        }
    }

    private static ParameterSpecType SpecOf(string name)
        => PipeAlignmentParameters.Definitions.Single(definition => definition.Name == name).SpecType;
}
