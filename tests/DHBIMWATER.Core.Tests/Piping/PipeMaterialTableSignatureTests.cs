using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>재료표 도면 서명(docs/291). 그려지는 값이 하나라도 바뀌면 달라야 다시 그린다.</summary>
public class PipeMaterialTableSignatureTests
{
    private static readonly PipeMaterialRow[] Rows =
        [new(1, "직관", "D160x6.00m", 2, "DCIP", 10, "a.png"), new(2, "곡관", "D160x45°", 1)];

    [Fact]
    public void SameInput_SameSignature()
        => Assert.Equal(PipeMaterialTableSignature.Of("표", Rows, true), PipeMaterialTableSignature.Of("표", Rows.ToArray(), true));

    [Fact]
    public void AnyDrawnValueChange_ChangesSignature()
    {
        var baseline = PipeMaterialTableSignature.Of("표", Rows, true);
        Assert.NotEqual(baseline, PipeMaterialTableSignature.Of("표2", Rows, true));
        Assert.NotEqual(baseline, PipeMaterialTableSignature.Of("표", Rows, false));
        Assert.NotEqual(baseline, PipeMaterialTableSignature.Of("표", [Rows[0] with { Count = 3 }, Rows[1]], true));
        Assert.NotEqual(baseline, PipeMaterialTableSignature.Of("표", [Rows[0] with { Spec = "D160x5.00m" }, Rows[1]], true));
        Assert.NotEqual(baseline, PipeMaterialTableSignature.Of("표", [Rows[0] with { Remark = "" }, Rows[1]], true));
        Assert.NotEqual(baseline, PipeMaterialTableSignature.Of("표", [Rows[0] with { TypeImagePath = "b.png" }, Rows[1]], true));
        Assert.NotEqual(baseline, PipeMaterialTableSignature.Of("표", [Rows[0]], true));
    }

    [Fact]
    public void NotDrawnValues_DoNotChangeSignature()
    {
        // SymbolTypeId는 안 그려짐. 이미지 끄면 경로도 무시
        Assert.Equal(PipeMaterialTableSignature.Of("표", Rows, true),
            PipeMaterialTableSignature.Of("표", [Rows[0] with { SymbolTypeId = 99 }, Rows[1]], true));
        Assert.Equal(PipeMaterialTableSignature.Of("표", Rows, false),
            PipeMaterialTableSignature.Of("표", [Rows[0] with { TypeImagePath = "b.png" }, Rows[1]], false));
    }
}
