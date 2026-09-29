using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>docs/208 § B — 격점번호의 Revit 뷰 이름 금지 문자·앞뒤 공백 정제.</summary>
public sealed class PipeMaterialViewNameTests
{
    [Fact]
    public void For_ReplacesForbiddenCharsAndTrims()
        => Assert.Equal("배관재료표_GV-01 _A__2", PipeMaterialViewName.For("배관재료표", " GV-01 [A]:2 "));

    [Fact]
    public void For_KeepsOrdinaryJunctionIdAsIs()
        => Assert.Equal("배관재료표_도면_J-3", PipeMaterialViewName.For("배관재료표_도면", "J-3"));
}
