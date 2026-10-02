using Autodesk.Revit.DB;
using System.IO;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Infrastructure.Helpers;
using DHBIMWATER.Shared.Diagnostics;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;
using DHBIMWATER.Core.Piping;
using PipeCountRules = DHBIMWATER.Core.Piping.PipeCountRules;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Gis;

/// <summary>
/// 5점 가변(Adaptive Component) 곡관 패밀리를 배치한다. Beam 모드 전용이라 Connector 연결은 하지 않는다.
/// </summary>
internal sealed class RevitAdaptiveBendPlacementRepo : IAdaptiveBendPlacementRepo
{
    private const string RotationXyParameterPrefix = "rot_XY_";
    private const string RotationXzParameterPrefix = "rot_XZ_";
    /// <summary>곡관마다 문서 전체를 재생성하지 않고, 이 개수마다 형상 변경을 한 번씩 반영한다.</summary>
    private const int RegenerateBatchSize = 50;

    private readonly Func<Document?> _doc;
    private readonly IProjectLocationCoordinateTransformRepo _coordinateTransform;
    public RevitAdaptiveBendPlacementRepo(Func<Document?> doc, IProjectLocationCoordinateTransformRepo coordinateTransform)
    { _doc = doc; _coordinateTransform = coordinateTransform; }

    public AdaptiveBendPlacementResult Place(IReadOnlyList<AdaptiveBendPlacementPlan> plans, AlignmentPlacementOrigin origin, PipeInfoParameterContext? info = null, IProgress<PipeAlignmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (plans.Count == 0) return new AdaptiveBendPlacementResult(0, Array.Empty<string>());
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingBends, 0, plans.Count));
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var symbols = new Dictionary<(string Family, string Type), FamilySymbol>();
        // 비활성 Symbol 활성화 시 필요한 Regenerate가 곡관 생성 배치 중간에 끼지 않도록 유형별로 먼저 확정한다.
        using (PlacementProfiler.Step("07a 곡관 Symbol 확정"))
            foreach (var key in plans.Select(x => (x.FamilyName, x.TypeName)).Distinct())
                ResolveSymbol(doc, symbols, key.FamilyName, key.TypeName);
        // 패밀리별로 한 번만 경고하면 충분하다 — 절점마다 같은 파라미터 누락 메시지가 반복되면 오히려 안 읽힌다.
        var missingParameters = new HashSet<(string Family, string Type, string Parameter)>();
        var writer = new PipeParameterWriter();
        var overrideWarnings = new List<string>();
        var exceededOverrides = new List<(ElementId ElementId, int NodeId)>();
        var shapeParameterSuccesses = 0;
        // OD를 못 구한 (관종, DN) 조합. 절점마다 반복 경고하지 않도록 조합 단위로 묶는다.
        var missingOuterDiameter = new HashSet<(string PipeKind, double DiameterMm)>();
        var overrideView = BendOverrideGraphics.ResolveOverrideView(doc);
        var solidFillPatternId = BendOverrideGraphics.FindSolidFillPatternId(doc);
        var count = 0;
        var pendingRegeneration = 0;

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();   // docs/209 § A — 사용자 취소
            var symbol = symbols[(plan.FamilyName, plan.TypeName)];
            FamilyInstance instance;
            IList<ElementId> pointIds;
            // 07b를 Create와 GetPointIds로 분리한다. 후자가 암묵적 문서 재생성을 강제하는지 확인하기 위한 것.
            using (PlacementProfiler.Step("07b1 곡관 Create"))
            using (PlacementProfiler.StepBucketed("07b1 곡관 Create", count))
                instance = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(doc, symbol);
            using (PlacementProfiler.Step("07b2 곡관 GetPointIds"))
            using (PlacementProfiler.StepBucketed("07b2 곡관 GetPointIds", count))
                pointIds = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(instance);
            if (pointIds.Count != 5)
                throw new DhbimwaterException(ErrorCodes.PA_AdaptivePointCount, $"곡관 패밀리 '{plan.FamilyName}:{plan.TypeName}'의 Adaptive Point가 5개가 아닙니다({pointIds.Count}개).");
            if (plan.RotXYDeg.Count != 5 || plan.RotXZDeg.Count != 5)
                throw new DhbimwaterException(ErrorCodes.PA_RotationCountMismatch, $"절점 {plan.NodeId}의 회전값은 XY/XZ 각각 5개여야 합니다.");
            (Parameter Xy, Parameter Xz)[] rotationParameters;
            using (PlacementProfiler.Step("07c 곡관 회전 파라미터 조회"))
                rotationParameters = ResolveRotationParameters(instance, plan);

            var points = new[] { plan.Points.Start, plan.Points.ArcStart, plan.Points.ArcMid, plan.Points.ArcEnd, plan.Points.End };
            var converted = ProjectLocationCoordinateConverter.Convert(_coordinateTransform, points,
                Enumerable.Repeat(plan.ZOffsetM, points.Length).ToArray());
            using (PlacementProfiler.Step("07d 곡관 P1~P5 좌표 설정"))
            using (PlacementProfiler.StepBucketed("07d 곡관 P1~P5 좌표 설정", count))
                for (var i = 0; i < 5; i++)
                {
                    var refPoint = (ReferencePoint)doc.GetElement(pointIds[i]);
                    try
                    {
                        refPoint.Position = converted[i];
                    }
                    catch (ArgumentException outOfRange) when (
                        outOfRange.ParamName == "point" ||
                        outOfRange.Message.Contains("design limits", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new DhbimwaterException(ErrorCodes.PA_TooFarFromOrigin,
                            $"곡관 배치 실패({plan.FamilyName}:{plan.TypeName}, 절점 {plan.NodeId}): " +
                            "내부원점으로부터 너무 멀리 떨어진 곳에 모델링을 시도했습니다.", outOfRange);
                    }
                }
            if (info is { Enabled: true })
            {
                using var infoScope = PlacementProfiler.Step("07e 곡관 DH_ 파라미터 기록");
                writer.Text(instance, PipeAlignmentParameters.Addin, PipeAlignmentParameters.AddinValue);
                writer.Text(instance, PipeAlignmentParameters.Part, PipeAlignmentParameters.BendPartValue);
                writer.Text(instance, PipeAlignmentParameters.NominalDiameter, PipeAlignmentParameters.DiameterText(plan.DiameterMm));
                writer.Text(instance, PipeAlignmentParameters.Material, MaterialCodeOf(plan.PipeKind));
                // 강관 곡관의 등급은 F12/F15/F20(곡관 종류)이다 — 직관 등급 STWW는 곡관 제원과 무관하다(2026-09-22 사용자 확정).
                var isSteel = PipeKindCatalog.MaterialOf(plan.PipeKind) == PipeMaterial.Steel;
                writer.Text(instance, PipeAlignmentParameters.Grade, isSteel ? info.SteelFittingClass.ToString() : plan.PipeKind);
                writer.YesNo(instance, PipeAlignmentParameters.NightWork, info.IsNightWork);   // docs/141 § C-4
                writer.Text(instance, PipeAlignmentParameters.ElevationDatum, PipeAlignmentParameters.Label(info.ZDatum));
                writer.Integer(instance, PipeAlignmentParameters.NodeId, plan.NodeId);
                writer.AngleDeg(instance, PipeAlignmentParameters.ActualDeflection, plan.DeflectionDeg);
                writer.AngleDeg(instance, PipeAlignmentParameters.StandardAngle, plan.StandardAngleDeg);
                writer.AngleDeg(instance, PipeAlignmentParameters.AllowableDeflection, plan.EffectiveAllowableDeg);
                writer.AngleDeg(instance, PipeAlignmentParameters.ResidualDeflection, plan.ResidualDeg);
                writer.YesNo(instance, PipeAlignmentParameters.IsAcceptable, plan.IsAcceptable);
                // 강관 곡관에는 A/B형이 없어 형식은 비운다(docs/211 § 0-1 A4). 접합 개소·구성은 강관 전용 접합 적용(편측 1 · 양측 2)을 따른다 —
                // 곡관 형식(A/B)은 주철관 형상도 바꾸므로 강관은 따로 둔 설정을 본다(docs/243 § B, docs/211 § 0-1 A3 개정).
                var applicationMode = isSteel ? info.SteelApplicationMode : info.ApplicationMode;
                var socketEnds = applicationMode == JointApplicationMode.BothJoints ? 2 : 1;
                // 접합 개소는 새로 재는 값이 아니라 접합 적용(편측/양측) 설정을 옮겨 적는 것이다 — 곡관 하나는 양 끝이 붙어도
                // 편측이면 1개소로 센다(docs/166 § 3-1). 부설은 요소당 1이며 접합수와 무관한 별도 계수다.
                // 편측·양측은 개수의 문제일 뿐 종류·규격은 같으므로 슬롯 1벌이다. 규격은 요소 관경과 같아 비운다(docs/186 § 5).
                writer.JointSlots(instance, PipeKindCatalog.MaterialOf(plan.PipeKind), info.BendConnection,
                    new[] { (Spec: string.Empty, Count: socketEnds) },
                    info.UseRestraintGland);
                writer.Integer(instance, PipeAlignmentParameters.LayingCount, PipeCountRules.PlannedBendLayingCount);
                // 곡관은 자르지 않는다 — 0과 미기록을 구분하려고 명시한다(docs/266 § 2-4).
                writer.Integer(instance, PipeAlignmentParameters.CutCount, PipeCountRules.PlannedBendCutCount);
                // 접합 구성은 소켓 단부 수로 적는다 — A형(양쪽 소켓) `2S`, B형(한쪽 소켓) `1S`. 위 슬롯 개소와 같은 축이다(docs/199 § 2).
                writer.JointComposition(instance, info.BendConnection, endCount: 2, socketEnds: socketEnds);
                // 곡관은 곡관표의 한 개 무게(소켓 포함)를 그대로 적는다(docs/178 § A-2). 단위중량은 직관에만 있다.
                writer.MassKg(instance, PipeAlignmentParameters.Weight, plan.WeightKg);
                writer.Text(instance, PlacementStationParameters.SourceFile, plan.SourceFile);
                writer.Text(instance, PlacementStationParameters.Alignment, plan.RecordNumber);
                writer.Text(instance, PlacementStationParameters.Profile, string.Empty);
                writer.StationM(instance, PlacementStationParameters.StaStart, plan.StaStartM);
                writer.StationM(instance, PlacementStationParameters.StaEnd, plan.StaEndM);
                writer.StationM(instance, PlacementStationParameters.ZOffset, plan.ZOffsetM);
                writer.YesNo(instance, PlacementStationParameters.Flip, false);
                writer.Text(instance, PlacementStationParameters.VerticalBasis, "GlobalZ");
            }

            using (PlacementProfiler.Step("07f 곡관 형상/회전 파라미터 기록"))
            {
            if (plan.DiameterParameterName is not null)
                SetShapeParameter(instance, plan, plan.DiameterParameterName, plan.DiameterMm, missingParameters, ref shapeParameterSuccesses);
            if (plan.WallThicknessParameterName is not null)
                SetShapeParameter(instance, plan, plan.WallThicknessParameterName, plan.WallThicknessEMm, missingParameters, ref shapeParameterSuccesses);
            // OD는 곡관 형상 구동값이다. 직관 제원표에 (관종, DN)이 없으면 OuterDiameterMm이 null이라 형상이 기본값으로 남는다.
            if (plan.OuterDiameterParameterName is not null && plan.OuterDiameterMm is { } outerDiameterMm)
                SetShapeParameter(instance, plan, plan.OuterDiameterParameterName, outerDiameterMm, missingParameters, ref shapeParameterSuccesses);
            else if (plan.OuterDiameterParameterName is not null)
                missingOuterDiameter.Add((plan.PipeKind, plan.DiameterMm));
            for (var i = 0; i < 5; i++)
            {
                SetRequiredAngleParameter(rotationParameters[i].Xy, plan, $"{RotationXyParameterPrefix}{i + 1}",
                    BendOrientation.ApplyProjectNorth(plan.RotXYDeg[i], origin.TrueNorthClockwiseDegrees));
                SetRequiredAngleParameter(rotationParameters[i].Xz, plan, $"{RotationXzParameterPrefix}{i + 1}", plan.RotXZDeg[i]);
            }
            }
            // 각 곡관의 좌표·형상·회전값은 서로 독립적이고 배치 계획도 Revit 진입 전에 확정된다.
            // 2026-08-20 배치 처리 시 함께 발견된 좌표 확대는 Regenerate 타이밍이 아니라
            // BendArcGeometry.Compute 선택 인자 오버로드 오바인딩이 원인이었으므로, 문서 재생성만 묶어 처리한다.
            if (++pendingRegeneration >= RegenerateBatchSize)
            {
                using (PlacementProfiler.Step("07g 곡관 Regenerate")) doc.Regenerate();
                pendingRegeneration = 0;
            }
            if (!plan.IsAcceptable || plan.HasLegOverlap)   // docs/217 § B — 다리 겹침도 적색
                exceededOverrides.Add((instance.Id, plan.NodeId));
            count++;
            // 진행률은 Regenerate 주기와 분리해 곡관 1개마다 보고한다.
            progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingBends, count, plans.Count));
        }
        if (pendingRegeneration > 0) { using (PlacementProfiler.Step("07g 곡관 Regenerate")) doc.Regenerate(); }
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingBends, plans.Count, plans.Count));
        PlacementProfiler.Count("곡관 인스턴스", count);
        // 신규 인스턴스 형상이 재생성된 뒤 기존과 같은 허용 초과 적색 재지정을 적용한다.
        using (PlacementProfiler.Step("07h 허용초과 적색 표시"))
            foreach (var (elementId, nodeId) in exceededOverrides)
                BendOverrideGraphics.ApplyExceededOverride(overrideView, elementId, solidFillPatternId, nodeId, overrideWarnings);

        var warnings = missingParameters
            .GroupBy(x => (x.Family, x.Type))
            .Select(g => $"곡관 패밀리 '{g.Key.Family}:{g.Key.Type}'에 {string.Join(", ", g.Select(x => x.Parameter))} 파라미터가 없거나 읽기전용이라 값을 설정하지 못했습니다.")
            .ToList();
        var representative = plans.GroupBy(x => (x.PipeKind, x.DiameterMm)).OrderByDescending(x => x.Count()).ThenBy(x => x.Key.PipeKind).ThenBy(x => x.Key.DiameterMm).First();
        var representativePlan = representative.First();
        warnings.Insert(0,
            $"[정보] 곡관 형상 파라미터 — DN='{representativePlan.DiameterParameterName}', OD='{representativePlan.OuterDiameterParameterName}', thk='{representativePlan.WallThicknessParameterName}'\n"
            + $"기록 예시({representative.Key.PipeKind}/DN{representative.Key.DiameterMm:0.##}): DN={representativePlan.DiameterMm:0.##}mm, OD={representativePlan.OuterDiameterMm:0.##}mm, e={representativePlan.WallThicknessEMm:0.##}mm / 기록 성공 {shapeParameterSuccesses}건, 실패 {missingParameters.Count}건");
        if (missingOuterDiameter.Count > 0)
            warnings.Add($"직관 제원표에 외경 OD가 없어 곡관 형상이 패밀리 기본값으로 남은 관종/DN {missingOuterDiameter.Count}건: "
                + string.Join(", ", missingOuterDiameter.OrderBy(x => x.PipeKind).ThenBy(x => x.DiameterMm).Select(x => $"{x.PipeKind}/DN{x.DiameterMm:0.##}")));
        warnings.AddRange(overrideWarnings);
        if (writer.Missing.Count > 0)
            warnings.Add($"프로젝트 매개변수 {string.Join(", ", writer.Missing.OrderBy(x => x))}를 곡관 인스턴스에 기록하지 못했습니다(바인딩 실패 또는 읽기전용).");
        if (info is { Enabled: true } && writer.UnmappedJointWarning is { } jointCountWarning)
            warnings.Add(jointCountWarning);
        var outerDiameters = plans.Where(x => x.OuterDiameterMm is not null)
            .GroupBy(x => (x.PipeKind, x.DiameterMm))
            .ToDictionary(x => x.Key, x => x.First().OuterDiameterMm!.Value);
        return new AdaptiveBendPlacementResult(count, warnings, outerDiameters);
    }

    public IReadOnlyList<(string FamilyName, string TypeName)> FindMissingSymbols(IEnumerable<(string FamilyName, string TypeName)> pairs)
    {
        var doc = _doc();
        var distinct = pairs.Distinct().ToList();
        if (doc is null) return distinct;

        var loaded = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Select(x => (x.Family.Name, x.Name))
            .ToHashSet();
        return distinct.Where(x => !loaded.Contains(x)).ToList();
    }

    // 같은 (패밀리, 타입) 조합은 FilteredElementCollector 재조회 없이 캐시에서 재사용한다.
    private static FamilySymbol ResolveSymbol(Document doc, Dictionary<(string, string), FamilySymbol> cache, string familyName, string typeName)
    {
        var key = (familyName, typeName);
        if (cache.TryGetValue(key, out var cached)) return cached;

        var symbol = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(x => x.Family.Name == familyName && x.Name == typeName)
            ?? throw new DhbimwaterException(ErrorCodes.PA_BendFamilyNotFound, $"곡관 패밀리를 찾을 수 없습니다: {familyName} - {typeName}");
        if (!AdaptiveComponentFamilyUtils.IsAdaptiveComponentFamily(symbol.Family))
            throw new DhbimwaterException(ErrorCodes.PA_NotAdaptiveFamily, $"'{familyName}:{typeName}'은 가변(Adaptive Component) 패밀리가 아닙니다.");
        if (!symbol.IsActive)
        {
            symbol.Activate();
            doc.Regenerate();
        }

        cache[key] = symbol;
        return symbol;
    }

    private static void SetShapeParameter(FamilyInstance instance, AdaptiveBendPlacementPlan plan, string name, double valueMm, HashSet<(string, string, string)> missingParameters, ref int successes)
    {
        if (AdaptiveParameterWriter.TryWrite(instance, name, valueMm.ToString("0.##"), UC.MmToFt(valueMm), (int)Math.Round(valueMm))) successes++;
        else missingParameters.Add((plan.FamilyName, plan.TypeName, name));
    }

    private static (Parameter Xy, Parameter Xz)[] ResolveRotationParameters(FamilyInstance instance, AdaptiveBendPlacementPlan plan)
    {
        var result = new (Parameter Xy, Parameter Xz)[5];
        for (var i = 0; i < 5; i++)
            result[i] = (
                RequireAngleParameter(instance, plan, $"{RotationXyParameterPrefix}{i + 1}"),
                RequireAngleParameter(instance, plan, $"{RotationXzParameterPrefix}{i + 1}"));
        return result;
    }

    private static Parameter RequireAngleParameter(FamilyInstance instance, AdaptiveBendPlacementPlan plan, string name)
    {
        var param = instance.LookupParameter(name)
            ?? throw new DhbimwaterException(ErrorCodes.PA_AngleParameterUnwritable, $"곡관 패밀리 '{plan.FamilyName}:{plan.TypeName}'에 인스턴스 각도 파라미터 '{name}'이 없습니다.");
        if (param.IsReadOnly)
            throw new DhbimwaterException(ErrorCodes.PA_AngleParameterUnwritable, $"곡관 패밀리 '{plan.FamilyName}:{plan.TypeName}'의 '{name}' 파라미터가 읽기전용입니다.");
        if (param.StorageType != StorageType.Double || !param.Definition.GetDataType().Equals(SpecTypeId.Angle))
            throw new DhbimwaterException(ErrorCodes.PA_AngleParameterUnwritable, $"곡관 패밀리 '{plan.FamilyName}:{plan.TypeName}'의 '{name}'은 쓰기 가능한 각도(Angle) 인스턴스 파라미터여야 합니다.");
        return param;
    }

    private static void SetRequiredAngleParameter(Parameter param, AdaptiveBendPlacementPlan plan, string name, double degrees)
    {
        if (!param.Set(UC.DegToRad(degrees)))
            throw new DhbimwaterException(ErrorCodes.PA_RotationWriteFailed, $"곡관 패밀리 '{plan.FamilyName}:{plan.TypeName}'의 '{name}'에 회전값 {degrees:0.###}°를 기록하지 못했습니다.");
    }

    private static string MaterialCodeOf(string pipeKind) => PipeKindCatalog.MaterialOf(pipeKind) is { } material ? PipeAlignmentParameters.Code(material) : string.Empty;
}
