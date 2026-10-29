namespace DHBIMWATER.Core.Piping;

/// <summary>공기밸브실 기초 본관 관통 Void 지름 규칙(2026-10-29). Void = 관 외경 + 여유. 외경은 직관 규격표(관종·DN).
/// 밸브실 모델링(Void 생성)과 PipeLayout(Void → DN 역산)이 같은 규칙을 쓴다.</summary>
public static class MainPipeVoidSizing
{
    /// <summary>외경에 더하는 지름 여유(mm). 사용자 지정 전까지 0(2026-10-29).</summary>
    public const double ClearanceMm = 0;

    /// <summary>역산 허용차(mm). Void 지름이 어느 DN·외경과도 이 안에 안 들면 DN을 못 정한 것으로 본다.</summary>
    private const double MatchToleranceMm = 5;

    private static StraightPipeSpecTable Table => StraightPipeSpecTable.Default.WithSteelDefaults();

    /// <summary>관종·DN의 외경. 규격표에 없으면 null.</summary>
    public static double? OuterDiameterMm(double nominalMm, PipeMaterial material)
        => Table.FindOuterDiameterByDiameter(nominalMm, material)?.OuterDiameterMm;

    /// <summary>Void 지름 = 외경 + 여유. 외경 모르면 DN + 여유(종전 동작).</summary>
    public static double VoidDiameterMm(double nominalMm, PipeMaterial material)
        => (OuterDiameterMm(nominalMm, material) ?? nominalMm) + ClearanceMm;

    /// <summary>Void 지름 → DN. 여유를 뺀 값이 어떤 관종의 외경 또는 DN 자체(종전 Void = DN)와 가장 가까운 DN.
    /// 허용차 밖이면 null.</summary>
    public static double? NominalFromVoidDiameter(double voidDiameterMm)
    {
        var target = voidDiameterMm - ClearanceMm;
        var best = Table.Entries
            .SelectMany(x => new[] { (x.DiameterMm, Gap: Math.Abs(x.OuterDiameterMm - target)), (x.DiameterMm, Gap: Math.Abs(x.DiameterMm - target)) })
            .OrderBy(x => x.Gap)
            .FirstOrDefault();
        return best.Gap <= MatchToleranceMm && best.DiameterMm > 0 ? best.DiameterMm : null;
    }
}
