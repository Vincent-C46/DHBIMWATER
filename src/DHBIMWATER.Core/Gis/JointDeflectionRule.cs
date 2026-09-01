namespace DHBIMWATER.Core.Gis;

/// <summary>
/// 덕타일주철관 판정 규칙. 조인트 허용굴곡 안이면 곡관을 생략하고,
/// 넘으면 잔여각이 허용굴곡 안에 드는 표준 곡관을 고른다.
/// (기존 <c>BendResolver.Resolve</c> 본문을 관종별 규칙으로 옮긴 것으로, 판정 결과는 동일하다.)
/// </summary>
public sealed class JointDeflectionRule : IBendingRule
{
    private const double Epsilon = 1e-9;

    public PipeMaterial Material => PipeMaterial.DuctileIron;

    /// <summary>덕타일 표준 곡관 각도. 다른 관종은 이 목록을 쓰지 않는다.</summary>
    public static IReadOnlyList<double> StandardAngles { get; } = new[] { 11.25, 22.5, 45d, 90d };

    /// <summary>표에서 유효 허용굴곡을 조회한다. 판정 계산은 하지 않는다.</summary>
    public static double? GetAllowableDeflection(JointDeflectionTable table, string jointType, double dn, JointApplicationMode mode)
        => table.EffectiveAllowableFor(jointType, dn, mode);

    /// <summary>선정 곡관의 잔여각이 유효 허용굴곡 안인지 계산한다.</summary>
    public static bool EvaluateFitting(double actualAngleDeg, double fittingAngleDeg, double effectiveAllowableDeg)
        => Math.Abs(actualAngleDeg - fittingAngleDeg) <= effectiveAllowableDeg + Epsilon;

    public BendResolution Resolve(NodeClassification node, BendSettings settings)
        => Resolve(node, settings, null);

    public BendResolution Resolve(NodeClassification node, BendSettings settings, double? forcedAngleDeg)
    {
        if (node.Kind != NodeKind.Bend)
            throw new ArgumentException("곡관 판정은 Bend 절점에서만 정의된다.", nameof(node));

        var actual = node.DeflectionDeg;
        var jointType = settings.ActiveJointType;
        var singleAllowable = settings.JointDeflections.AllowableFor(jointType, node.MaxDiameterMm) ?? 0d;
        var effectiveValue = GetAllowableDeflection(settings.JointDeflections, jointType, node.MaxDiameterMm, settings.ApplicationMode);
        var hasDeflectionSetting = effectiveValue.HasValue;
        var effective = effectiveValue ?? 0d;

        if (forcedAngleDeg is null && hasDeflectionSetting && actual <= effective + Epsilon)
            return Empty(node, BendResolutionKind.None, actual, 0d, singleAllowable, actual, jointType, settings.ApplicationMode, effective, true);

        var availableAngles = BendFittingCatalog.AnglesFor(settings.ActiveBendConnection);
        var acceptable = forcedAngleDeg is null ? availableAngles
            .Select(angle => new { Angle = angle, Residual = Math.Abs(actual - angle) })
            .Where(x => hasDeflectionSetting && EvaluateFitting(actual, x.Angle, effective))
            .OrderBy(x => x.Residual)
            .ThenBy(x => x.Angle)
            .FirstOrDefault() : null;

        var selectedAngle = forcedAngleDeg ?? acceptable?.Angle ?? availableAngles
            .OrderBy(angle => Math.Abs(actual - angle))
            .ThenBy(angle => angle)
            .First();
        // 엑셀 지정각도 자동판정과 같은 기준으로 합부를 본다. 지정각이라는 이유만으로 허용 초과(적색) 처리하면
        // 잔여각이 허용굴곡 안인 곡관까지 전부 빨갛게 뜬다.
        var isAcceptable = forcedAngleDeg is { } forced
            ? hasDeflectionSetting && EvaluateFitting(actual, forced, effective)
            : acceptable is not null;
        var kind = isAcceptable ? BendResolutionKind.Standard : BendResolutionKind.Unresolved;
        var residual = actual - selectedAngle;
        var fitting = settings.Fittings.Find(node.MaxDiameterMm, selectedAngle, Material, settings.ActiveBendConnection);
        if (fitting is null)
            return Empty(node, kind, actual, selectedAngle, singleAllowable, residual, jointType, settings.ApplicationMode, effective, isAcceptable);

        var tangent = BendResolver.TangentLength(fitting.CenterlineRadiusMm, selectedAngle);
        var weightKg = jointType == JointTypeCatalog.Tyton ? fitting.WeightTytonKg : fitting.WeightKpMechanicalKg;
        return new BendResolution(
            node.NodeId, kind, actual, selectedAngle, singleAllowable, residual,
            fitting.LayingLengthMm, fitting.CenterlineRadiusMm, tangent, true,
            jointType, settings.ApplicationMode, effective, isAcceptable,
            fitting.ExtraLegLengthMm, fitting.WallThicknessMm, fitting.Form, weightKg);
    }

    private static BendResolution Empty(NodeClassification node, BendResolutionKind kind, double actual, double standardAngle,
        double allowable, double residual, string jointType, JointApplicationMode mode, double effective, bool isAcceptable)
        => new(node.NodeId, kind, actual, standardAngle, allowable, residual, 0d, 0d, 0d, false,
            jointType, mode, effective, isAcceptable);
}
