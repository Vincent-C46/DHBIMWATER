using Autodesk.Revit.DB;
using System.IO;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Infrastructure.Helpers;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Gis;

internal sealed class RevitBendDirectShapeRepo : IBendDirectShapeRepo
{
    private readonly Func<Document?> _doc;
    private readonly IProjectLocationCoordinateTransformRepo _coordinateTransform;
    private readonly IPipeQuantityRules _rules;
    private readonly IPipeCatalogSource _catalogSource;
    public RevitBendDirectShapeRepo(Func<Document?> doc, IProjectLocationCoordinateTransformRepo coordinateTransform,
        IPipeQuantityRules rules, IPipeCatalogSource catalogSource)
    { _doc = doc; _coordinateTransform = coordinateTransform; _rules = rules; _catalogSource = catalogSource; }

    public AdaptiveBendPlacementResult Place(IReadOnlyList<AdaptiveBendPlacementPlan> plans, AlignmentPlacementOrigin origin,
        PipeInfoParameterContext? info = null, IProgress<PipeAlignmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (plans.Count == 0) return new AdaptiveBendPlacementResult(0, Array.Empty<string>());
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var materialIds = new Dictionary<PipeMaterial, ElementId>();   // 관종별 재질(docs/212 § B)
        var writer = new PipeParameterWriter(_rules);
        var catalog = info is { Enabled: true } ? _catalogSource.OpenCatalog() : null;
        var warnings = new List<string>();
        var overrideWarnings = new List<string>();
        var overrideView = BendOverrideGraphics.ResolveOverrideView(doc);
        var solidFillPatternId = BendOverrideGraphics.FindSolidFillPatternId(doc);
        var outerDiameters = new Dictionary<(string PipeKind, double DiameterMm), double>();
        var skippedNodes = new List<string>();
        var count = 0;
        var skipped = 0;
        var failed = 0;
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingBends, 0, plans.Count));

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();   // docs/209 § A — 사용자 취소
            try
            {
                var sourcePoints = new[] { plan.Points.Start, plan.Points.ArcStart, plan.Points.ArcMid, plan.Points.ArcEnd, plan.Points.End };
                var points = ProjectLocationCoordinateConverter.Convert(_coordinateTransform, sourcePoints,
                    Enumerable.Repeat(plan.ZOffsetM, sourcePoints.Length).ToArray());
                if (!TryBuildSolid(doc.Application.ShortCurveTolerance, points, plan, PipeSolidFactory.MaterialIdFor(doc, materialIds, plan.PipeKind), out var solid, out var failureReason))
                {
                    skipped++;
                    skippedNodes.Add($"{plan.NodeId}({failureReason})");
                    continue;
                }
                var shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                shape.SetShape(new GeometryObject[] { solid });
                // 강관 곡관은 이름·등급에 STWW 대신 곡관 종류(F12/F15/F20)를 쓴다(2026-09-22 사용자 확정).
                var gradeLabel = info is not null && PipeKindCatalog.MaterialOf(plan.PipeKind) == PipeMaterial.Steel ? info.SteelFittingClass.ToString() : plan.PipeKind;
                shape.Name = $"곡관_{gradeLabel}_{plan.DiameterMm:0}_{BendKindParser.Label(plan.StandardAngleDeg)}도";
                if (info is { Enabled: true }) WriteParameters(writer, shape, plan, info, catalog!);
                if (plan.OuterDiameterMm is { } outerDiameterMm) outerDiameters[(plan.PipeKind, plan.DiameterMm)] = outerDiameterMm;
                if (!plan.IsAcceptable || plan.HasLegOverlap) BendOverrideGraphics.ApplyExceededOverride(overrideView, shape.Id, solidFillPatternId, plan.NodeId, overrideWarnings);
                count++;
            }
            catch (Exception ex)
            {
                failed++;
                warnings.Add($"절점 {plan.NodeId} 곡관 DirectShape 생성 실패: {ex.GetType().Name} — {ex.Message}");
            }
            finally { progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingBends, count, plans.Count)); }
        }
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingBends, plans.Count, plans.Count));
        if (skipped > 0) warnings.Add($"곡관 DirectShape 경로가 퇴화한 절점 {skipped}곳을 건너뛰었습니다: {string.Join(", ", skippedNodes)}.");
        if (failed > 0) warnings.Add($"곡관 DirectShape 생성 예외가 난 절점 {failed}곳을 건너뛰었습니다.");
        if (writer.InvalidTypes.Count > 0) warnings.Add($"프로젝트 매개변수 {string.Join(", ", writer.InvalidTypes.OrderBy(x => x))}의 타입이 배치 태그 계약과 달라 값을 건너뛰었습니다.");
        var missing = writer.Missing.Except(writer.InvalidTypes).OrderBy(x => x).ToList();
        if (missing.Count > 0) warnings.Add($"프로젝트 매개변수 {string.Join(", ", missing)}를 곡관 DirectShape에 기록하지 못했습니다(바인딩 실패 또는 읽기전용).");
        if (info is { Enabled: true } && writer.UnmappedJointWarning is { } jointCountWarning)
            warnings.Add(jointCountWarning);
        warnings.AddRange(overrideWarnings);
        return new AdaptiveBendPlacementResult(count, warnings, outerDiameters);
    }

    private static bool TryBuildSolid(double tolerance, IReadOnlyList<XYZ> points, AdaptiveBendPlacementPlan plan, ElementId materialId, out Solid solid, out string failureReason)
    {
        var path = new CurveLoop();
        var axis = points[1] - points[0];
        if (axis.GetLength() >= tolerance) path.Append(Line.CreateBound(points[0], points[1]));
        var arc = Arc.Create(points[1], points[3], points[2]);
        if (arc.Length < tolerance) { solid = null!; failureReason = "호 퇴화"; return false; }
        path.Append(arc);
        var endLine = points[4] - points[3];
        if (endLine.GetLength() >= tolerance) path.Append(Line.CreateBound(points[3], points[4]));
        if (path.IsOpen() == false || path.Count() == 0) { solid = null!; failureReason = "경로 퇴화"; return false; }
        if (axis.GetLength() < tolerance) axis = points[2] - points[1];
        if (axis.GetLength() < tolerance) { solid = null!; failureReason = "축 퇴화"; return false; }
        axis = axis.Normalize();
        var seed = Math.Abs(axis.Z) > 0.9 ? XYZ.BasisX : XYZ.BasisZ;
        var xAxis = axis.CrossProduct(seed).Normalize();
        var yAxis = axis.CrossProduct(xAxis).Normalize();
        var outerRadiusFt = UC.MmToFt((plan.OuterDiameterMm ?? plan.DiameterMm) / 2d);
        var loops = new List<CurveLoop> { PipeSolidFactory.CircleLoop(points[0], outerRadiusFt, xAxis, yAxis) };
        var innerRadiusMm = (plan.OuterDiameterMm ?? plan.DiameterMm) / 2d - plan.WallThicknessEMm;
        if (plan.WallThicknessEMm > 0d && innerRadiusMm > 0.01d)
            loops.Add(PipeSolidFactory.CircleLoop(points[0], UC.MmToFt(innerRadiusMm), xAxis, yAxis));
        solid = GeometryCreationUtilities.CreateSweptGeometry(path, 0, 0d, loops, new SolidOptions(materialId, ElementId.InvalidElementId));
        failureReason = string.Empty;
        return true;
    }

    private void WriteParameters(PipeParameterWriter writer, DirectShape shape, AdaptiveBendPlacementPlan plan,
        PipeInfoParameterContext info, IPipeCatalog catalog)
    {
        writer.Text(shape, PipeAlignmentParameters.Addin, PipeAlignmentParameters.AddinValue);
        writer.Text(shape, PipeAlignmentParameters.Part, PipeAlignmentParameters.BendPartValue);
        writer.Text(shape, PipeAlignmentParameters.NominalDiameter, PipeAlignmentParameters.DiameterText(plan.DiameterMm));
        writer.Text(shape, PipeAlignmentParameters.Material, PipeKindCatalog.MaterialOf(plan.PipeKind) is { } material ? PipeAlignmentParameters.Code(material) : string.Empty);
        var isSteel = PipeKindCatalog.MaterialOf(plan.PipeKind) == PipeMaterial.Steel;
        writer.Text(shape, PipeAlignmentParameters.Grade, isSteel ? info.SteelFittingClass.ToString() : plan.PipeKind);
        writer.YesNo(shape, PipeAlignmentParameters.NightWork, info.IsNightWork);   // docs/141 § C-4
        writer.Text(shape, PipeAlignmentParameters.ElevationDatum, PipeAlignmentParameters.Label(info.ZDatum));
        writer.Integer(shape, PipeAlignmentParameters.NodeId, plan.NodeId);
        writer.AngleDeg(shape, PipeAlignmentParameters.ActualDeflection, plan.DeflectionDeg);
        writer.AngleDeg(shape, PipeAlignmentParameters.StandardAngle, plan.StandardAngleDeg);
        writer.AngleDeg(shape, PipeAlignmentParameters.AllowableDeflection, plan.EffectiveAllowableDeg);
        writer.AngleDeg(shape, PipeAlignmentParameters.ResidualDeflection, plan.ResidualDeg);
        writer.YesNo(shape, PipeAlignmentParameters.IsAcceptable, plan.IsAcceptable);
        // 강관 곡관에는 A/B형이 없어 형식은 비운다(docs/211 § 0-1 A4). 접합 개소·구성은 강관 전용 접합 적용(편측 1 · 양측 2)을 따른다 —
        // 곡관 형식(A/B)은 주철관 형상도 바꾸므로 강관은 따로 둔 설정을 본다(docs/243 § B, docs/211 § 0-1 A3 개정).
        var applicationMode = isSteel ? info.SteelApplicationMode : info.ApplicationMode;
        var socketEnds = applicationMode == JointApplicationMode.BothJoints ? 2 : 1;
        // 가변 패밀리 경로(RevitAdaptiveBendPlacementRepo)와 같은 규칙이다 — 두 경로가 다른 값을 찍으면
        // 출력 모드만 바꿔도 산출서가 달라진다(docs/166 § 6).
        writer.JointSlots(shape, PipeKindCatalog.MaterialOf(plan.PipeKind), info.BendConnection,
            new[] { (Spec: string.Empty, Count: socketEnds) },
            info.UseRestraintGland);
        writer.Integer(shape, PipeAlignmentParameters.LayingCount, _rules.PlannedBendLayingCount);
        // 곡관은 자르지 않는다 — 0과 미기록을 구분하려고 명시한다(docs/266 § 2-4).
        writer.Integer(shape, PipeAlignmentParameters.CutCount, _rules.PlannedBendCutCount);
        writer.JointComposition(shape, info.BendConnection, endCount: 2, socketEnds: socketEnds);   // A형 `2S` · B형 `1S`(docs/199 § 2)
        var weight = isSteel
            // 강관 곡관 + 플랜지 단부 수 × 개당 무게(None→F12, 형상 등급과 같음).
            ? _rules.SteelBendWeightKg(catalog, info.BendConnection, ValveBypassSpecTable.NormalizeSteelClass(info.SteelFittingClass),
                plan.DiameterMm, plan.StandardAngleDeg, PipeEndFlagParameters.SteelFlangeCount(PipeMaterial.Steel, info.BendConnection))
            : _rules.DuctileBendWeightKg(catalog, info.BendConnection, plan.Form, info.JointType, plan.DiameterMm, plan.StandardAngleDeg);
        if (weight is { } weightKg) writer.MassKg(shape, PipeAlignmentParameters.Weight, weightKg);
        // DirectShape는 측점편집 대상이 아니다(docs/214, 2026-09-22 사용자 확정) — 갱신용 태그는 적지 않는다.
        // 수량산출이 읽는 셋(구간·측점시작·측점끝)만 남긴다.
        writer.Text(shape, PlacementStationParameters.Alignment, plan.RecordNumber);
        writer.StationM(shape, PlacementStationParameters.StaStart, plan.StaStartM);
        writer.StationM(shape, PlacementStationParameters.StaEnd, plan.StaEndM);
    }
}
