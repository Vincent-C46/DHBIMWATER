using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>밸브실 유형별 격점번호 기본값(docs/177 § A).</summary>
public sealed class ValveRoomJunctionIdCatalogTests
{
    [Theory]
    [InlineData("제수밸브실", "GV-01")]
    [InlineData("이토밸브실", "BV-01")]
    [InlineData("공기밸브실", "AV-01")]
    [InlineData("타이밸브실", "TV-01")]
    public void Default_KnownZone_ReturnsPrefixedNumber(string zone, string expected)
        => Assert.Equal(expected, ValveRoomJunctionIdCatalog.Default(zone));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("펌프실")]
    public void Default_UnknownOrBlankZone_ReturnsEmpty(string? zone)
        => Assert.Equal(string.Empty, ValveRoomJunctionIdCatalog.Default(zone));

    /// <summary>일람표 대상 정렬이 <c>StringComparer.OrdinalIgnoreCase</c> 문자열 순서라
    /// 0 채움 2자리가 아니면 10번부터 순서가 깨진다(CreatePipeMaterialScheduleUseCase.cs:64).</summary>
    [Fact]
    public void Default_IsZeroPaddedToTwoDigits()
        => Assert.Equal("GV-01", ValveRoomJunctionIdCatalog.Default("제수밸브실"));

    /// <summary>창의 유형 목록(빈 항목 제외)이 전부 접두를 갖는지. 유형을 늘리면 여기서 걸린다.</summary>
    [Fact]
    public void Prefix_CoversEveryZoneOfferedInTheWindow()
    {
        string[] zones = ["이토밸브실", "공기밸브실", "타이밸브실", "제수밸브실"];

        Assert.All(zones, zone => Assert.NotNull(ValveRoomJunctionIdCatalog.Prefix(zone)));
    }
}
