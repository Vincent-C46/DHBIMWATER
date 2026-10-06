using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class PipeMaterialSpecTextTests
{
    [Theory]
    [InlineData(PipeMaterialSpecText.StraightName, BendConnection.Flanged, "2F 직관")]
    [InlineData(PipeMaterialSpecText.ShortName, BendConnection.Socket, "2S 단관")]
    [InlineData(PipeMaterialSpecText.TeeName, BendConnection.Flanged, "3F T형")]
    public void WithJointMark_UsesEndCount(string part, BendConnection connection, string expected)
        => Assert.Equal(expected, PipeMaterialSpecText.WithJointMark(part, connection));

    [Fact]
    public void WithJointMark_UsesNormalizedComposition()
        => Assert.Equal("2S1F T형", PipeMaterialSpecText.WithJointMark("3F T형", "2S1F", BendConnection.Flanged, 3));

    [Fact]
    public void WithJointMark_DoesNotStack()
        => Assert.Equal("2S 단관", PipeMaterialSpecText.WithJointMark("2F 단관", BendConnection.Socket));

    [Theory]
    [InlineData("2F 직관", "DCIP")]
    [InlineData("2S 단관", "DCIP")]
    [InlineData("3F T형", "")]
    public void Remark_UsesBaseName(string part, string expected)
        => Assert.Equal(expected, PipeMaterialSpecText.Remark(part, PipeMaterial.DuctileIron));

    [Fact]
    public void ValveRoomAndAlignment_ShareParameterNamesAndGuids()
    {
        Assert.Equal(PipeAlignmentParameters.JointTotalCount, ValvePipingParameters.JointTotalCount);
        Assert.Equal(PipeAlignmentParameters.Guids[PipeAlignmentParameters.LayingCount],
            ValvePipingParameters.Guids[ValvePipingParameters.LayingCount]);
        for (var slot = 1; slot <= PipeAlignmentParameters.JointSlotDefinitionCount; slot++)
            Assert.Equal(PipeAlignmentParameters.Guids[PipeAlignmentParameters.JointSlotKind(slot)],
                ValvePipingParameters.Guids[ValvePipingParameters.JointSlotKind(slot)]);
    }
}
