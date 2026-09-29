using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.UI.Tests.Piping;

/// <summary>배관재료표 명칭·규격 문자열(docs/136 § 3-10).
/// 신규 배치 경로와 재기록 경로가 같은 문자열을 만들어야 하므로, 이 형식이 곧 회귀선이다.</summary>
public class PipeMaterialSpecTextTests
{
    [Fact]
    public void Segment_UsesDiameterAndMeterLengthWithTwoDecimals()
    {
        Assert.Equal("D160x6.00m", PipeMaterialSpecText.Segment(160, 6000));
        Assert.Equal("D160x4.25m", PipeMaterialSpecText.Segment(160, 4250));
        // 소수 두 자리 고정이라 표기 분해능은 10mm다 — 커넥터 실측 오차로 몇 mm 어긋난 관도
        // 신규 배치분과 같은 행에 묶인다(docs/203 § A, § 4-①).
        Assert.Equal("D160x6.00m", PipeMaterialSpecText.Segment(160, 5999.6));
    }

    [Fact]
    public void Bend_OmitsAngleWhenDeflectionUnknown()
    {
        Assert.Equal("D160x45°", PipeMaterialSpecText.Bend(160, 45));
        // 정수면 정수로 적는다(docs/203 § B).
        Assert.Equal("D160x90°", PipeMaterialSpecText.Bend(160, 90));
        Assert.Equal("D160x22.5°", PipeMaterialSpecText.Bend(160, 22.5));
        Assert.Equal("D160", PipeMaterialSpecText.Bend(160, null));
    }

    /// <summary>T형은 분기경에도 `D`를 붙인다(docs/203 § C). 십자·Y형은 따라오지 않는다.</summary>
    [Fact]
    public void Tee_JoinsMainAndBranchDiameters()
    {
        Assert.Equal("D160xD90", PipeMaterialSpecText.Tee(160, 90));
        Assert.Equal("D1650xD600", PipeMaterialSpecText.Tee(1650, 600));
    }

    [Theory]
    [InlineData(6000, "직관")]
    [InlineData(5999.5, "직관")]   // 허용오차 1mm 안
    [InlineData(5990, "단관")]
    [InlineData(1200, "단관")]
    public void SegmentName_SplitsStraightAndShortByNominalLength(double lengthMm, string expected)
        => Assert.Equal(expected, PipeMaterialSpecText.SegmentName(lengthMm));

    [Theory]
    [InlineData(-1, 0)]    // 커넥터가 서로 반대 방향 = 일직선
    [InlineData(0, 90)]    // 직각
    [InlineData(1, 180)]
    public void DeflectionDeg_MatchesPipeNodeAngleConvention(double dot, double expected)
        => Assert.Equal(expected, PipeMaterialSpecText.DeflectionDeg(dot), 6);
}
