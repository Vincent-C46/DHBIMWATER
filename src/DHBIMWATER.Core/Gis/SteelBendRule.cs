using DHBIMWATER.Shared.Errors;
namespace DHBIMWATER.Core.Gis;

/// <summary>
/// 강관 판정 규칙(docs/211 § D, 2026-09-22 사용자 확정 3). 조인트 허용굴곡 개념이 없다 —
/// 편각이 핸드북 각도 허용차(KS D 3578 부도 1~5: 90°·45° ±2°, 22½°·11¼°·5⅝° ±1°) 안에 드는 표준 곡관을 고르고,
/// 편각 1° 이하는 곡관을 두지 않는다. 벗어나면 최근접 표준각을 배치하되 허용 초과(<see cref="BendResolutionKind.Unresolved"/>)로 표시한다.
/// </summary>
public sealed class SteelBendRule : IBendingRule
{
    private const double Epsilon = 1e-9;

    public PipeMaterial Material => PipeMaterial.Steel;

    /// <summary>KS D 3578 부도 1~5. 5⅝°는 DN1000 이상만 표에 있다 — 표에 없는 (DN, 각도)는 후보에서 자연히 빠진다.</summary>
    public static IReadOnlyList<double> StandardAngles { get; } = new[] { 5.625, 11.25, 22.5, 45d, 90d };

    IReadOnlyList<double> IBendingRule.StandardAngles => StandardAngles;

    /// <summary>이 편각 이하는 곡관 없이 통과시킨다(용접 이음으로 흡수).</summary>
    public const double NoBendThresholdDeg = 1.0;

    /// <summary>핸드북 각도 허용차 — 90°·45°는 ±2°, 그 이하는 ±1°.</summary>
    public static double AngleToleranceDeg(double angleDeg) => angleDeg >= 45d - Epsilon ? 2.0 : 1.0;

    /// <summary>진단 보고의 접합 표기. 강관은 KP·타이튼이 없으므로 접합 종류를 그대로 적는다.</summary>
    public static string JointTypeLabel(BendConnection connection) => connection == BendConnection.Flanged ? "플랜지" : "용접";

    public BendResolution Resolve(NodeClassification node, BendSettings settings)
        => Resolve(node, settings, null);

    public BendResolution Resolve(NodeClassification node, BendSettings settings, double? forcedAngleDeg)
    {
        if (node.Kind != NodeKind.Bend)
            throw new DhbimwaterException(ErrorCodes.PA_Internal, "곡관 판정은 Bend 절점에서만 정의된다.");

        var actual = node.DeflectionDeg;
        var jointType = JointTypeLabel(settings.ActiveBendConnection);

        // 편각 1° 이하 — 곡관 없음. 허용값 칸에는 문턱값을 적어 진단 표에서 왜 비었는지 보이게 한다.
        if (forcedAngleDeg is null && actual <= NoBendThresholdDeg + Epsilon)
            return Empty(node, BendResolutionKind.None, actual, 0d, NoBendThresholdDeg, actual, jointType, NoBendThresholdDeg, true);

        // 후보 = 표에 (DN, 각도, 접합, 종류) 행이 있는 표준각. 5⅝°는 DN1000 이상만 있다.
        var candidates = StandardAngles
            .Select(angle => (Angle: angle, Fitting: settings.Fittings.Find(node.MaxDiameterMm, angle, Material, settings.ActiveBendConnection, settings.ActiveSteelFittingClass)))
            .Where(x => x.Fitting is not null)
            .ToList();

        double selectedAngle;
        BendFittingEntry? fitting;
        if (forcedAngleDeg is { } forced)
        {
            selectedAngle = forced;
            fitting = candidates.FirstOrDefault(x => Math.Abs(x.Angle - forced) <= 1e-6).Fitting;
        }
        else if (candidates.Count > 0)
        {
            var pick = candidates
                .OrderBy(x => Math.Abs(actual - x.Angle))
                .ThenBy(x => x.Angle)
                .First();
            selectedAngle = pick.Angle;
            fitting = pick.Fitting;
        }
        else
        {
            // 표에 없는 DN — 최근접 표준각만 적고 치수 없이 미해결로 둔다(주철관 규칙이 fitting null일 때와 같은 형태).
            selectedAngle = StandardAngles.OrderBy(angle => Math.Abs(actual - angle)).ThenBy(angle => angle).First();
            fitting = null;
        }

        var tolerance = AngleToleranceDeg(selectedAngle);
        var residual = actual - selectedAngle;
        var isAcceptable = fitting is not null && Math.Abs(residual) <= tolerance + Epsilon;
        var kind = isAcceptable ? BendResolutionKind.Standard : BendResolutionKind.Unresolved;
        if (fitting is null)
            return Empty(node, kind, actual, selectedAngle, tolerance, residual, jointType, tolerance, isAcceptable);

        var tangent = BendResolver.TangentLength(fitting.CenterlineRadiusMm, selectedAngle);
        // 강관 곡관에는 A/B형·소켓이 없다 — 추가 다리 0, 형식은 자리만 채운다(기록은 비운다, docs/211 § 0-1 A4).
        return new BendResolution(
            node.NodeId, kind, actual, selectedAngle, tolerance, residual,
            fitting.LayingLengthMm, fitting.CenterlineRadiusMm, tangent, true,
            jointType, JointApplicationMode.SingleJoint, tolerance, isAcceptable,
            0d, fitting.WallThicknessMm, BendForm.BType, fitting.WeightKg);
    }

    private static BendResolution Empty(NodeClassification node, BendResolutionKind kind, double actual, double standardAngle,
        double allowable, double residual, string jointType, double effective, bool isAcceptable)
        => new(node.NodeId, kind, actual, standardAngle, allowable, residual, 0d, 0d, 0d, false,
            jointType, JointApplicationMode.SingleJoint, effective, isAcceptable);
}
