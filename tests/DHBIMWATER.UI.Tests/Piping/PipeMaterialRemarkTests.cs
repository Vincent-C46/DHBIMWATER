using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.UI.Tests.Piping;

/// <summary>비고 자동값과 관종 어휘(docs/141 § B).
/// 배치 경로와 재기록 경로가 같은 문자열을 만들어야 하므로, 이 형식이 곧 회귀선이다.</summary>
public class PipeMaterialRemarkTests
{
    [Theory]
    [InlineData("직관", PipeMaterial.DuctileIron, "DCIP")]
    [InlineData("단관", PipeMaterial.Steel, "SP")]
    [InlineData("직관", PipeMaterial.Pe, "PE")]
    [InlineData("단관", PipeMaterial.Pvc, "PVC")]
    public void Remark_WritesMaterialCodeForSegments(string partName, PipeMaterial material, string expected)
        => Assert.Equal(expected, PipeMaterialSpecText.Remark(partName, material));

    [Theory]
    [InlineData("곡관")]
    [InlineData("T형")]
    [InlineData("제수밸브")]
    [InlineData("신축관")]
    public void Remark_IsEmptyForEverythingElse(string partName)
        => Assert.Equal(string.Empty, PipeMaterialSpecText.Remark(partName, PipeMaterial.DuctileIron));

    /// <summary>관종을 모르는 경로(재기록)는 값을 지어내지 않는다.</summary>
    [Fact]
    public void Remark_IsEmptyWhenMaterialUnknown()
        => Assert.Equal(string.Empty, PipeMaterialSpecText.Remark("직관", null));

    /// <summary>라벨을 바꾸면 재기록 경로의 관종 복원이 조용히 깨진다. 왕복으로 묶어 둔다.</summary>
    [Fact]
    public void MaterialFromLabel_RoundTripsEveryMaterial()
    {
        foreach (PipeMaterial material in Enum.GetValues(typeof(PipeMaterial)))
            Assert.Equal(material, PipeAlignmentParameters.MaterialFromLabel(PipeAlignmentParameters.Label(material)));
    }

    [Theory]
    [InlineData("없는관")]
    [InlineData("")]
    [InlineData(null)]
    public void MaterialFromLabel_ReturnsNullWhenUnknown(string? label)
        => Assert.Null(PipeAlignmentParameters.MaterialFromLabel(label));

    [Fact]
    public void Code_MatchesQuantitySheetVocabulary()
    {
        Assert.Equal("DCIP", PipeAlignmentParameters.Code(PipeMaterial.DuctileIron));
        Assert.Equal("SP", PipeAlignmentParameters.Code(PipeMaterial.Steel));
        Assert.Equal("PE", PipeAlignmentParameters.Code(PipeMaterial.Pe));
        Assert.Equal("PVC", PipeAlignmentParameters.Code(PipeMaterial.Pvc));
    }

    /// <summary>`DH_관종`에 기록하는 값은 코드다(docs/139 § F-1). 코드를 바꾸면 DHBoost 수량산출의
    /// 관종 필터가 조용히 어긋나므로 왕복으로 묶어 둔다.</summary>
    [Fact]
    public void MaterialFromCode_RoundTripsEveryMaterial()
    {
        foreach (PipeMaterial material in Enum.GetValues(typeof(PipeMaterial)))
            Assert.Equal(material, PipeAlignmentParameters.MaterialFromCode(PipeAlignmentParameters.Code(material)));
    }

    /// <summary>재기록 경로는 코드로 바꾸기 전에 배치된 모델(한글 라벨)도 읽어야 한다.</summary>
    [Theory]
    [InlineData("DCIP", PipeMaterial.DuctileIron)]
    [InlineData("SP", PipeMaterial.Steel)]
    [InlineData("덕타일주철관", PipeMaterial.DuctileIron)]
    [InlineData("강관", PipeMaterial.Steel)]
    public void MaterialFromModelValue_AcceptsCodeAndLegacyLabel(string value, PipeMaterial expected)
        => Assert.Equal(expected, PipeAlignmentParameters.MaterialFromModelValue(value));

    [Theory]
    [InlineData("없는관")]
    [InlineData("")]
    [InlineData(null)]
    public void MaterialFromModelValue_ReturnsNullWhenUnknown(string? value)
        => Assert.Null(PipeAlignmentParameters.MaterialFromModelValue(value));
}
