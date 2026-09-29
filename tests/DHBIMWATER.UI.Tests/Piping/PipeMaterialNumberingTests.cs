using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.UI.Tests.Piping;

/// <summary>격점 안 부재번호 채번(docs/180). 신규 배치와 재기록이 이 함수 하나를 공유하므로
/// 두 경로의 회귀가 여기서 한꺼번에 잡힌다.</summary>
public class PipeMaterialNumberingTests
{
    private static PipeMaterialNumbering.PartKey Key(string name, string spec) => new(name, spec);

    private static readonly PipeMaterialNumbering.PartKey Straight = Key("2F 직관", "D160x6.00m");
    private static readonly PipeMaterialNumbering.PartKey Bend45 = Key("2F 곡관", "D160x45°");
    private static readonly PipeMaterialNumbering.PartKey Bend90 = Key("2F 곡관", "D200 90°");

    private static PipeMaterialPart Part(PipeMaterialNumbering.PartKey key, int number, string junctionId = "V-3")
        => new(junctionId, number, key.Name, key.Spec);

    /// <summary>기존 부재가 없으면 등장 순서로 1부터.</summary>
    [Fact]
    public void Assign_NumbersFromOneWhenNothingExists()
    {
        var numbers = PipeMaterialNumbering.Assign([], [Straight, Bend45, Bend90]);

        Assert.Equal(1, numbers[Straight]);
        Assert.Equal(2, numbers[Bend45]);
        Assert.Equal(3, numbers[Bend90]);
    }

    /// <summary>같은 그룹이 여러 번 나와도 번호는 1개다(사용자 요구의 핵심).</summary>
    [Fact]
    public void Assign_GivesOneNumberPerGroup()
    {
        var numbers = PipeMaterialNumbering.Assign([], [Straight, Straight, Bend45, Straight]);

        Assert.Equal(2, numbers.Count);
        Assert.Equal(1, numbers[Straight]);
        Assert.Equal(2, numbers[Bend45]);
    }

    /// <summary>이미 있는 그룹은 그 번호를 그대로 받는다 — 재채번하지 않는다.</summary>
    [Fact]
    public void Assign_KeepsExistingGroupNumber()
    {
        var numbers = PipeMaterialNumbering.Assign(
            [Part(Straight, 1), Part(Bend45, 2)], [Straight, Bend45]);

        Assert.Equal(1, numbers[Straight]);
        Assert.Equal(2, numbers[Bend45]);
    }

    /// <summary>새 그룹만 최댓값+1부터 받는다. 빈 번호(3, 4)는 메우지 않는다 —
    /// 사용자가 고친 번호·이미 붙인 태그를 건드리지 않기 위해서다.</summary>
    [Fact]
    public void Assign_ContinuesFromMaxAndDoesNotFillGaps()
    {
        var numbers = PipeMaterialNumbering.Assign(
            [Part(Straight, 1), Part(Bend45, 2), Part(Key("2F 단관", "D160 L=2000"), 5)],
            [Straight, Bend90]);

        Assert.Equal(1, numbers[Straight]);
        Assert.Equal(6, numbers[Bend90]);
    }

    /// <summary>번호가 0인 기존 부재는 "아직 안 매겨진 것"으로 보고 무시한다.
    /// 파라미터가 없는 인스턴스도 0으로 읽히므로(Collector의 `?? 0`) 0번 행이 생기면 안 된다.</summary>
    [Fact]
    public void Assign_IgnoresUnnumberedExistingParts()
    {
        var numbers = PipeMaterialNumbering.Assign([Part(Straight, 0)], [Straight]);

        Assert.Equal(1, numbers[Straight]);
    }

    /// <summary>한 그룹에 번호가 섞여 있으면(사용자가 인스턴스 하나만 고친 경우) 최솟값을 택한다.
    /// 수집 순서에 결과가 흔들리면 안 되므로 순서를 뒤집은 케이스도 같이 본다.</summary>
    [Theory]
    [InlineData(3, 7)]
    [InlineData(7, 3)]
    public void Assign_TakesSmallestNumberWhenGroupIsInconsistent(int first, int second)
    {
        var numbers = PipeMaterialNumbering.Assign(
            [Part(Straight, first), Part(Straight, second)], [Straight, Bend45]);

        Assert.Equal(3, numbers[Straight]);
        // 새 그룹은 어디까지나 최댓값 기준으로 이어 붙는다.
        Assert.Equal(8, numbers[Bend45]);
    }

    [Theory]
    [InlineData("V-3", "V-3", true)]
    [InlineData(null, "", true)]
    [InlineData("  ", null, true)]
    [InlineData("", "V-3", false)]
    [InlineData("V-3", "v-3", false)]
    public void SameJunction_TreatsBlankAsUnassigned(string? left, string? right, bool expected)
        => Assert.Equal(expected, PipeMaterialNumbering.SameJunction(left, right));
}
