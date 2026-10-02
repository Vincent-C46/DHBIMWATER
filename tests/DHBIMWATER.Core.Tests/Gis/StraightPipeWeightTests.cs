using DHBIMWATER.Core.Gis;
using Xunit;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Core.Tests.Gis;

/// <summary>docs/178 § A-2 — 직관 무게 = M무게 × 연장 + 소켓 무게. 기본표 값은 주철관 핸드북(2020년판) 직관 표 원문이다.</summary>
public sealed class StraightPipeWeightTests
{
    [Fact]
    public void 기본표는_핸드북_M무게와_소켓_무게를_갖는다()
    {
        var spec = StraightPipeSpecTable.Default.Find(PipeKindCatalog.Water1, 300)!;
        Assert.Equal(61.8, spec.UnitWeightKgPerM);
        Assert.Equal(16.7, spec.SocketWeightKpKg);
        Assert.Equal(26.5, spec.SocketWeightTytonKg);
        // 상수 4종관은 DN600부터. 소켓 무게는 관종과 무관한 DN 단일값이다.
        var class4 = StraightPipeSpecTable.Default.Find(PipeKindCatalog.Water4, 600)!;
        Assert.Equal(122.0, class4.UnitWeightKgPerM);
        Assert.Equal(StraightPipeSpecTable.Default.Find(PipeKindCatalog.Water1, 600)!.SocketWeightKpKg, class4.SocketWeightKpKg);
        // 모든 기본 행에 무게가 있다 — 빠진 행이 있으면 그 관종·DN의 직관만 무게가 비어 산출서에서 조용히 빠진다.
        // 소켓 무게는 주철관에만 있다 — 강관(STWW)은 용접·플랜지라 0이다(docs/211 § B-1).
        Assert.All(StraightPipeSpecTable.Default.Entries, x =>
        {
            Assert.True(x.UnitWeightKgPerM > 0, $"{x.PipeKind} DN{x.DiameterMm}");
            if (PipeKindCatalog.MaterialOf(x.PipeKind) == PipeMaterial.Steel)
                Assert.True(x.SocketWeightKpKg == 0 && x.SocketWeightTytonKg == 0, $"{x.PipeKind} DN{x.DiameterMm}");
            else
                Assert.True(x.SocketWeightKpKg > 0 && x.SocketWeightTytonKg > 0, $"{x.PipeKind} DN{x.DiameterMm}");
        });
    }

    [Fact]
    public void 직관_무게는_M무게_곱_연장_더하기_적용_조인트의_소켓_무게다()
    {
        var spec = StraightPipeSpecTable.Default.Find(PipeKindCatalog.Water1, 300)!;
        // 핸드북 "KP 메커니컬 조인트관 총무게 6M" = 388(약). 61.8 × 6 + 16.7 = 387.5.
        Assert.Equal(387.5, StraightPipeWeight.TotalKg(spec, 6d, JointTypeCatalog.KpMechanical)!.Value, 6);
        // 타이튼 6M = 397(약). 61.8 × 6 + 26.5 = 397.3.
        Assert.Equal(397.3, StraightPipeWeight.TotalKg(spec, 6d, JointTypeCatalog.Tyton)!.Value, 6);
        // 접합 종류를 모르면 KP 소켓으로 본다 — 기본 Joint Type이 KP다.
        Assert.Equal(387.5, StraightPipeWeight.TotalKg(spec, 6d, null)!.Value, 6);
    }

    [Fact]
    public void 단위중량이_없으면_무게도_없다()
    {
        var noWeight = new StraightPipeSpec(PipeKindCatalog.Water1, 300, 326, 8.8);
        Assert.Null(StraightPipeWeight.TotalKg(noWeight, 6d, JointTypeCatalog.KpMechanical));
        Assert.Null(StraightPipeWeight.TotalKg(null, 6d, JointTypeCatalog.KpMechanical));
        Assert.Null(StraightPipeWeight.TotalKg(StraightPipeSpecTable.Default.Find(PipeKindCatalog.Water1, 300), 0d, JointTypeCatalog.KpMechanical));
    }
}
