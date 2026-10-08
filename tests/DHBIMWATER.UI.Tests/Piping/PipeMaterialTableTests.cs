using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.UI.Tests.Piping;

/// <summary>드래프팅뷰 표의 행 묶음(docs/141 § B-7).
/// 일람표는 DH_비고가 필드라 값이 다르면 이미 행이 갈린다 — 여기서도 같은 규칙이어야 두 표의 행 수가 맞는다.</summary>
public class PipeMaterialTableTests
{
    private static PipeMaterialPart Part(string remark)
        => new("V-3", 1, "직관", "D160x6.00m", remark);

    [Fact]
    public void Build_SplitsRowsWhenRemarkDiffers()
    {
        var rows = PipeMaterialTable.Build([Part("DCIP"), Part("현장확인")], "V-3");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, x => Assert.Equal(1, x.Count));
        Assert.Equal(["DCIP", "현장확인"], rows.Select(x => x.Remark).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Build_GroupsAndKeepsRemarkWhenEqual()
    {
        var rows = PipeMaterialTable.Build([Part("DCIP"), Part("DCIP")], "V-3");

        var row = Assert.Single(rows);
        Assert.Equal(2, row.Count);
        Assert.Equal("DCIP", row.Remark);
    }

    /// <summary>비고가 없는 기존 문서는 종전과 같은 표가 나와야 한다(회귀).</summary>
    [Fact]
    public void Build_IsUnchangedWhenNoRemarks()
    {
        var rows = PipeMaterialTable.Build(
            [new("V-3", 1, "직관", "D160x6.00m"), new("V-3", 1, "직관", "D160x6.00m"), new("V-3", 2, "곡관", "D160x45°")],
            "V-3");

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Count);
        Assert.Equal(1, rows[1].Count);
        Assert.All(rows, x => Assert.Equal(string.Empty, x.Remark));
    }

    [Fact]
    public void Build_KeepsFirstSymbolTypeWithoutSplittingRows()
    {
        var rows = PipeMaterialTable.Build([
            new("V-3", 1, "직관", "D160x6.00m", "DCIP", 101),
            new("V-3", 1, "직관", "D160x6.00m", "DCIP", 202)], "V-3");

        var row = Assert.Single(rows);
        Assert.Equal(2, row.Count);
        Assert.Equal(101, row.SymbolTypeId);
    }
}
