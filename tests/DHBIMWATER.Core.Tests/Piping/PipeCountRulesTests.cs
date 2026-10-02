using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>docs/266 § 2 · docs/267 § 2-2 부재별 개수 규칙(<see cref="PipeCountRules"/>)만 본다.</summary>
public sealed class PipeCountRulesTests
{
    [Fact]
    public void Valve_OnlyValveJointLayingOne()
    {
        var counts = PipeCountRules.ForValveRoomMember(new PipeMemberCountInput("제수밸브", null, "SP", IsValve: true, IsFlanged: true));

        Assert.Equal(1, counts.ValveJointLayingCount);
        Assert.Null(counts.LayingCount);
        Assert.Null(counts.CutCount);
        Assert.Empty(counts.SocketSlots);
    }

    [Theory]
    [InlineData("직관", "1S", 0, 0)]
    [InlineData("1S1F 단관", "1S1F", 1, 0)]
    [InlineData("단관", "", 1, 1)]          // 평단 단관
    [InlineData("2F 곡관", "2F", 1, 0)]
    [InlineData("4F T형", "4F", 1, 0)]
    [InlineData("DH_특수_밸브부관", "3F", 1, 0)]   // 밸브부관 = 이형관(R-7)
    public void Member_LayingAndCut(string part, string composition, int laying, int cut)
    {
        var counts = PipeCountRules.ForValveRoomMember(new PipeMemberCountInput(part, composition, "SP", IsValve: false, IsFlanged: true));

        Assert.Null(counts.ValveJointLayingCount);
        Assert.Equal(laying, counts.LayingCount);
        Assert.Equal(cut, counts.CutCount);
    }

    [Theory]
    [InlineData("SP", false, "용접합")]
    [InlineData("DCIP", false, "KP접합")]
    [InlineData("DCIP", true, "이탈방지압륜접합")]
    public void SocketSlot_KindFromMaterialCode(string code, bool gland, string kind)
    {
        var counts = PipeCountRules.ForValveRoomMember(new PipeMemberCountInput("2S1F T형", "2S1F", code, false, false, gland));

        Assert.Equal([new PipeJointSlotValue(kind, string.Empty, 2)], counts.SocketSlots);
        Assert.False(counts.MaterialAssumedSteel);
    }

    [Fact]
    public void FlangeOnly_NoSocketSlot_FlangeIsLeftToConnectionPass()
    {
        var counts = PipeCountRules.ForValveRoomMember(new PipeMemberCountInput("2F 단관", "2F", "SP", false, true));

        Assert.Empty(counts.SocketSlots);
    }

    [Fact]
    public void EmptyMaterial_AssumesSteel_AndFlags()
    {
        var counts = PipeCountRules.ForValveRoomMember(new PipeMemberCountInput("1S1F 단관", "1S1F", null, false, false));

        Assert.Equal([new PipeJointSlotValue("용접합", string.Empty, 1)], counts.SocketSlots);
        Assert.True(counts.MaterialAssumedSteel);
    }

    [Fact]
    public void EmptyComposition_FallsBackToConnection()
    {
        var socket = PipeCountRules.ForValveRoomMember(new PipeMemberCountInput("곡관", null, "SP", false, IsFlanged: false));
        var flanged = PipeCountRules.ForValveRoomMember(new PipeMemberCountInput("곡관", null, "SP", false, IsFlanged: true));

        Assert.Equal(1, socket.SocketSlots.Sum(x => x.Count));
        Assert.Empty(flanged.SocketSlots);
    }

    [Theory]
    [InlineData(6000, "직관", 0, 0)]
    [InlineData(5999.5, "직관", 0, 0)]
    [InlineData(5998, "단관", 1, 1)]
    [InlineData(4000, "단관", 1, 1)]
    public void PlannedSegment_SixMeterStandard(double lengthMm, string part, int laying, int cut)
        => Assert.Equal(new PlannedSegmentCounts(part, laying, cut), PipeCountRules.ForPlannedSegment(lengthMm));

    [Fact]
    public void PlannedBend_LayingOne_CutZero()
    {
        Assert.Equal(1, PipeCountRules.PlannedBendLayingCount);
        Assert.Equal(0, PipeCountRules.PlannedBendCutCount);
    }

    [Theory]
    [InlineData("직관", true)]
    [InlineData("2F 단관", true)]
    [InlineData("곡관", false)]
    [InlineData(null, false)]
    public void IsPipe_StraightOrShortOnly(string? part, bool expected) => Assert.Equal(expected, PipeCountRules.IsPipe(part));

    [Theory]
    [InlineData(2, 6000, "직관")]
    [InlineData(2, 5999.5, "직관")]
    [InlineData(2, 5998, "단관")]
    [InlineData(5, 1200, "곡관")]
    [InlineData(3, 6000, null)]
    public void AdaptivePartFor_ByPointCountAndLength(int points, double lengthMm, string? expected)
        => Assert.Equal(expected, PipeCountRules.AdaptivePartFor(points, lengthMm));

    [Theory]
    [InlineData("직관", "1S", 0, 0, 1)]
    [InlineData("단관", "1S", 1, 1, 1)]
    [InlineData("곡관", "2S", 1, 0, 2)]
    [InlineData("곡관", "2F", 1, 0, 0)]    // 플랜지는 연결 패스가 센다
    public void PlannedMember_CountsMatchAutoPlacement(string part, string composition, int laying, int cut, int socket)
    {
        var counts = PipeCountRules.ForPlannedMember(part, composition, "SP");

        Assert.Equal(laying, counts.LayingCount);
        Assert.Equal(cut, counts.CutCount);
        Assert.Equal(socket, counts.SocketSlots.Sum(x => x.Count));
        Assert.False(counts.MaterialUnknown);
    }

    [Fact]
    public void PlannedMember_UnknownMaterial_LeavesSocketSlotEmpty()
    {
        var counts = PipeCountRules.ForPlannedMember("직관", "1S", null);

        Assert.Empty(counts.SocketSlots);
        Assert.True(counts.MaterialUnknown);
    }

    [Theory]
    [InlineData("직관")]
    [InlineData("곡관")]
    public void DefaultPlannedComposition_IsOneSocket(string part) => Assert.Equal("1S", PipeCountRules.DefaultPlannedComposition(part));

    [Fact]
    public void FlangeJointKind_MatchesQuantityKey() => Assert.Equal("플랜지접합", PipeCountRules.FlangeJointKind);
}
