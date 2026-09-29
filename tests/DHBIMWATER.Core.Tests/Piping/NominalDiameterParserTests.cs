using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class NominalDiameterParserTests
{
    [Theory]
    [InlineData("버터플라이밸브 : DN300", 300)]
    [InlineData("게이트밸브 : 300A", 300)]
    [InlineData("밸브 : Ø150", 150)]
    [InlineData("밸브 : DN300 - 2단", 300)]
    [InlineData("게이트밸브 300A : 표준", 300)]
    [InlineData("게이트밸브 300A", 300)]
    [InlineData("밸브 : DN300", 300)]
    [InlineData("밸브 500A : 300형", 300)]
    public void Parse_ReturnsNominalDiameterFromTypeName(string familyTypeName, double expected)
        => Assert.Equal(expected, NominalDiameterParser.Parse(familyTypeName));

    [Theory]
    [InlineData("밸브 : 표준형")]
    public void Parse_ReturnsNull_WhenNoValidNominalDiameterExists(string familyTypeName)
        => Assert.Null(NominalDiameterParser.Parse(familyTypeName));

    [Fact]
    public void Parse_ReturnsDiameterWithinConfiguredRange()
        => Assert.Equal(2026, NominalDiameterParser.Parse("밸브 : DN2026 기본"));

    // ── NameComparer — 호칭지름 숫자 정렬(docs/192) ──────────────────────────────

    /// <summary>문자열 정렬이면 "65A"가 "200A" 아래로 간다. 숫자로 읽어 올바른 순서가 나와야 한다.</summary>
    [Fact]
    public void NameComparer_OrdersByNominalDiameter_NotByText()
    {
        string[] names = ["밸브 : 200A", "밸브 : 65A", "밸브 : 300A", "밸브 : 80A", "밸브 : 100A"];

        var sorted = names.OrderBy(x => x, NominalDiameterParser.NameComparer).ToList();

        Assert.Equal(["밸브 : 65A", "밸브 : 80A", "밸브 : 100A", "밸브 : 200A", "밸브 : 300A"], sorted);
    }

    /// <summary>표기(DN·A·Ø)가 섞여 있어도 읽어 낸 숫자로 한 줄에 세운다.</summary>
    [Fact]
    public void NameComparer_MixesNotations()
    {
        string[] names = ["밸브 : DN200", "밸브 : 65A", "밸브 : Ø100"];

        var sorted = names.OrderBy(x => x, NominalDiameterParser.NameComparer).ToList();

        Assert.Equal(["밸브 : 65A", "밸브 : Ø100", "밸브 : DN200"], sorted);
    }

    /// <summary>호칭지름을 못 읽은 이름은 숫자 목록 뒤로 보낸다. 그들끼리는 이름순이다.</summary>
    [Fact]
    public void NameComparer_PutsUnparsableNamesLast()
    {
        string[] names = ["밸브 : 표준형", "밸브 : 200A", "밸브 : 기본형", "밸브 : 65A"];

        var sorted = names.OrderBy(x => x, NominalDiameterParser.NameComparer).ToList();

        Assert.Equal(["밸브 : 65A", "밸브 : 200A", "밸브 : 기본형", "밸브 : 표준형"], sorted);
    }

    /// <summary>같은 지름끼리는 이름(Ordinal)으로 갈라 순서가 결정적이어야 한다 —
    /// 목록을 다시 만들 때마다 흔들리면 안 된다.</summary>
    [Fact]
    public void NameComparer_BreaksTiesByName()
    {
        string[] names = ["밸브 : 100A 무겁게", "밸브 : 100A 가볍게"];

        var sorted = names.OrderBy(x => x, NominalDiameterParser.NameComparer).ToList();

        Assert.Equal(["밸브 : 100A 가볍게", "밸브 : 100A 무겁게"], sorted);
    }
}
