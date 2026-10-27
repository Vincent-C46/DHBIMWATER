using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>패밀리 Yes/No 플래그 → 접합 구성(`2S1F` 표기)·형식. 실제 밸브실 배관 패밀리의 제어 방식을 기준으로 삼는다
/// (`DH_플랜지_직관`·`DH_소켓_직관`·`DH_소켓_곡관`·`DH_플랜지_플랜지소켓관`·`DH_소켓_플랜지T형관`, 2026-09-15 사용자 확인).
/// 소켓의 세부 종류(`KP접합`·`이탈방지압륜`)는 구성에 싣지 않는다(2026-09-15 사용자 결정).</summary>
public sealed class PipeJointFlagReaderTests
{
    private static Dictionary<string, bool> Flags(params (string Name, bool Value)[] flags)
        => flags.ToDictionary(flag => flag.Name, flag => flag.Value, StringComparer.Ordinal);

    [Fact]
    public void ComposeByEnd_CountsSocketAndFlangeEnds()
    {
        var leftSocket = PipeJointFlagReader.ComposeByEnd(
            Flags((PipeEndFlagParameters.LeftSocket, true)), PipeMaterial.DuctileIron, 2)!;
        Assert.Equal((1, 0), (leftSocket.Socket, leftSocket.Flange));
        var bothFlanges = PipeJointFlagReader.ComposeByEnd(Flags(
            (PipeEndFlagParameters.LeftFlange, true), (PipeEndFlagParameters.RightFlange, true)), PipeMaterial.DuctileIron, 2)!;
        Assert.Equal((0, 2), (bothFlanges.Socket, bothFlanges.Flange));
        var twoSockets = PipeJointFlagReader.ComposeByEnd(Flags(
            (PipeEndFlagParameters.LeftSocket, true), (PipeEndFlagParameters.RightSocket, true)), PipeMaterial.DuctileIron, 2)!;
        Assert.Equal((2, 0), (twoSockets.Socket, twoSockets.Flange));
    }

    [Fact]
    public void ComposeByEnd_CountsOneFlangeAndSteelRemainingEndAsSocket()
    {
        var castIron = PipeJointFlagReader.ComposeByEnd(Flags((PipeEndFlagParameters.LeftFlange, true)), PipeMaterial.DuctileIron, 2)!;
        Assert.Equal((0, 1), (castIron.Socket, castIron.Flange));
        var steel = PipeJointFlagReader.ComposeByEnd(Flags((PipeEndFlagParameters.RightFlange, true)), PipeMaterial.Steel, 2)!;
        Assert.Equal((1, 1), (steel.Socket, steel.Flange));
    }

    [Fact]
    public void ComposeByEnd_FlangedConflictPrefersFlangeAndReportsSide()
    {
        var oneSide = PipeJointFlagReader.ComposeByEnd(Flags(
            (PipeEndFlagParameters.LeftSocket, true), (PipeEndFlagParameters.LeftFlange, true)), PipeMaterial.DuctileIron, 2)!;
        Assert.Equal((0, 1), (oneSide.Socket, oneSide.Flange));
        Assert.Equal(new[] { "좌측" }, oneSide.ConflictSides);

        var bothSides = PipeJointFlagReader.ComposeByEnd(Flags(
            (PipeEndFlagParameters.LeftSocket, true), (PipeEndFlagParameters.LeftFlange, true),
            (PipeEndFlagParameters.RightSocket, true), (PipeEndFlagParameters.RightFlange, true)), PipeMaterial.DuctileIron, 2)!;
        Assert.Equal((0, 2), (bothSides.Socket, bothSides.Flange));
        Assert.Equal(new[] { "좌측", "우측" }, bothSides.ConflictSides);
    }

    [Fact]
    public void ComposeByEnd_AllOffIsReportedForEveryMaterial()
    {
        var flags = Flags((PipeEndFlagParameters.LeftSocket, false), (PipeEndFlagParameters.RightFlange, false));
        var castIron = PipeJointFlagReader.ComposeByEnd(flags, PipeMaterial.DuctileIron, 2)!;
        Assert.Equal((0, 0, true), (castIron.Socket, castIron.Flange, castIron.AllOff));
        Assert.True(castIron.HasEndFlags);
        var steel = PipeJointFlagReader.ComposeByEnd(flags, PipeMaterial.Steel, 2)!;
        Assert.True(steel.AllOff);
        Assert.Equal((0, 0), (steel.Socket, steel.Flange));
        var unknown = PipeJointFlagReader.ComposeByEnd(flags, null, 2)!;
        Assert.Equal((0, 0), (unknown.Socket, unknown.Flange));
        Assert.True(unknown.AllOff);
    }

    [Fact]
    public void ComposeByEnd_ReturnsNullWithoutRecognizedSideFlags()
    {
        Assert.Null(PipeJointFlagReader.ComposeByEnd(Flags(("소켓곡관(A형)", true)), PipeMaterial.DuctileIron, 2));
        Assert.Null(PipeJointFlagReader.ComposeByEnd(Flags(("분기_플랜지", true)), PipeMaterial.DuctileIron, 2));
        Assert.Null(PipeJointFlagReader.ComposeByEnd(Flags(("기타", true)), PipeMaterial.DuctileIron, 2));
    }

    [Fact]
    public void Compose_CountsBothFlangeEnds()
        => Assert.Equal((0, 2), PipeJointFlagReader.Compose(
            Flags(("좌측_플랜지", true), ("우측_플랜지", true)), "DH_플랜지_직관", 2));

    [Fact]
    public void Compose_CountsOnlyEnabledEnds()
        => Assert.Equal((0, 1), PipeJointFlagReader.Compose(
            Flags(("좌측_플랜지", true), ("우측_플랜지", false)), "DH_플랜지_직관", 2));

    /// <summary>소켓 종류 플래그는 구성에 영향을 주지 않는다 — 켜져 있든 꺼져 있든 `1S`다.</summary>
    [Fact]
    public void Compose_IgnoresSocketKindFlags()
        => Assert.Equal((1, 0), PipeJointFlagReader.Compose(
            Flags(("좌측_소켓", true), ("우측_소켓", false), ("KP접합", true), ("이탈방지압륜", false)),
            "DH_소켓_직관", 2));

    /// <summary>한쪽 소켓은 지금 패밀리의 사정일 뿐이다. 양쪽 소켓 직관이 나와도 2로 세어져야 한다.</summary>
    [Fact]
    public void Compose_CountsTwoSockets_WhenBothEndsModelled()
        => Assert.Equal((2, 0), PipeJointFlagReader.Compose(
            Flags(("좌측_소켓", true), ("우측_소켓", true), ("KP접합", false), ("이탈방지압륜", true)),
            "DH_소켓_직관", 2));

    /// <summary>형식 플래그는 이름에 `소켓`이 들어 있어도 단부가 아니다 — 괄호로 갈린다.</summary>
    [Fact]
    public void Compose_IgnoresFormFlagsWhenCountingEnds()
        => Assert.Equal((2, 0), PipeJointFlagReader.Compose(
            Flags(("소켓곡관(A형)", false), ("소켓곡관(B형)", true), ("KP접합", true), ("이탈방지압륜", false)),
            "DH_소켓_곡관", 2));

    /// <summary>단부 플래그가 없는 패밀리는 이름으로 유도한다 — 1S1F 고정 부재.</summary>
    [Fact]
    public void Compose_FallsBackToFamilyName_WhenNoEndFlags()
        => Assert.Equal((1, 1), PipeJointFlagReader.Compose(
            Flags(("KP접합", true), ("이탈방지압륜", false)), "DH_플랜지_플랜지소켓관", 2));

    /// <summary>종류 플래그가 없어도 형상만 알면 된다 — 종전에는 종류를 몰라 null이었다.</summary>
    [Fact]
    public void Compose_DoesNotNeedSocketKind()
        => Assert.Equal((1, 0), PipeJointFlagReader.Compose(Flags(("좌측_소켓", true)), "DH_소켓_직관", 2));

    /// <summary>`DH_소켓_플랜지T형관`(2026-09-15 확인): 소켓 단부 2개만 플래그이고 분기 플랜지는 고정이라 플래그가 없다.
    /// 플래그가 없는 종류는 이름으로 유도해 남은 단부를 채운다 — 종전에는 분기가 빠져 `KP:2`였다.</summary>
    [Fact]
    public void Compose_FillsRemainingEndsFromFamilyName_WhenOnlyOneKindHasFlags()
        => Assert.Equal((2, 1), PipeJointFlagReader.Compose(
            Flags(("좌측_소켓", true), ("우측_소켓", true), ("KP접합", true), ("이탈방지압륜", false)),
            "DH_소켓_플랜지T형관", 3));

    /// <summary>플랜지 플래그가 있는데 꺼진 것은 "분기 없음"이다. 이름만 보고 지어내지 않는다.</summary>
    [Fact]
    public void Compose_DoesNotFill_WhenOtherKindHasFlagsButOff()
        => Assert.Equal((2, 0), PipeJointFlagReader.Compose(
            Flags(("좌측_소켓", true), ("우측_소켓", true), ("분기_플랜지", false), ("KP접합", true)),
            "DH_소켓_플랜지T형관", 3));

    /// <summary>반대 방향도 같다 — 플랜지 단부만 플래그이고 소켓이 고정이면 소켓을 채운다.</summary>
    [Fact]
    public void Compose_FillsRemainingSocketEnds_WhenOnlyFlangeHasFlags()
        => Assert.Equal((1, 2), PipeJointFlagReader.Compose(
            Flags(("좌측_플랜지", true), ("우측_플랜지", true)), "DH_소켓_플랜지T형관", 3));

    /// <summary>이름에 다른 낱말이 없으면 종전과 같다 — 한쪽만 켠 소켓 직관은 여전히 1이다.</summary>
    [Fact]
    public void Compose_DoesNotFill_WhenFamilyNameLacksOtherWord()
        => Assert.Equal((1, 0), PipeJointFlagReader.Compose(
            Flags(("좌측_소켓", true), ("우측_소켓", false), ("KP접합", true)), "DH_소켓_직관", 2));

    [Fact]
    public void Compose_ReturnsNull_WhenNothingIsKnown()
        => Assert.Null(PipeJointFlagReader.Compose(Flags(("가시성", true)), "DH_이음관", 2));

    [Fact]
    public void Form_ReadsParenLabel()
        => Assert.Equal("B형", PipeJointFlagReader.Form(
            Flags(("소켓곡관(A형)", false), ("소켓곡관(B형)", true))));

    [Fact]
    public void Form_ReturnsNull_WhenBothOn()
        => Assert.Null(PipeJointFlagReader.Form(
            Flags(("소켓곡관(A형)", true), ("소켓곡관(B형)", true))));

}
