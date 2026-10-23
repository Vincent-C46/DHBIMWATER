using Autodesk.Revit.DB;
using System.IO;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Infrastructure.Helpers;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Gis;

internal sealed class RevitReducerDirectShapeRepo : IReducerDirectShapeRepo
{
    private readonly Func<Document?> _doc;
    private readonly IProjectLocationCoordinateTransformRepo _coordinateTransform;
    private readonly IPipeQuantityRules _rules;
    private readonly IPipeCatalogSource _catalogSource;
    public RevitReducerDirectShapeRepo(Func<Document?> doc, IProjectLocationCoordinateTransformRepo coordinateTransform,
        IPipeQuantityRules rules, IPipeCatalogSource catalogSource)
    { _doc = doc; _coordinateTransform = coordinateTransform; _rules = rules; _catalogSource = catalogSource; }

    public ReducerPlacementResult Place(IReadOnlyList<ReducerDirectShapePlacementPlan> plans, AlignmentPlacementOrigin origin,
        PipeInfoParameterContext? info = null, IProgress<PipeAlignmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (plans.Count == 0) return new ReducerPlacementResult(0, Array.Empty<string>());
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var materialIds = new Dictionary<PipeMaterial, ElementId>();   // 관종별 재질(docs/212 § B)
        var writer = new PipeParameterWriter(_rules);
        var catalog = info is { Enabled: true } ? _catalogSource.OpenCatalog() : null;
        var count = 0;
        var failed = 0;
        var solidReducers = 0;
        var failures = new List<string>();
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingReducers, 0, plans.Count));

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();   // docs/209 § A — 사용자 취소
            try
            {
                // 단면 평면의 법선은 위치와 같은 좌표 프레임이어야 한다. `RunDirection`은 공유좌표(진북 프레임)
                // 벡터라 기준점 진북각(BASEPOINT_ANGLETON) 회전을 타지 않는다 — 그대로 쓰면 단면이 그 각만큼
                // 비틀린다(직관도 같은 이유로 변환 좌표 기준 절단면으로 고쳤다).
                // 상류점에서 진행방향으로 1m 떨어진 프로브 점을 같은 변환에 함께 실어 보내고, 변환된 두 점의
                // 차로 모델좌표 방향을 얻는다. 변환이 아핀이라 평행이동은 상쇄되고 회전만 남는다.
                var runDirection = plan.Placement.RunDirection;
                var probe = new DHBIMWATER.Core.Geometry.Point3D(
                    plan.Placement.UpstreamEnd.X + runDirection.X,
                    plan.Placement.UpstreamEnd.Y + runDirection.Y,
                    plan.Placement.UpstreamEnd.Z + runDirection.Z);
                var points = ProjectLocationCoordinateConverter.Convert(_coordinateTransform,
                    new[] { plan.Placement.UpstreamEnd, plan.Placement.DownstreamEnd, probe },
                    new[] { plan.UpstreamZOffsetM, plan.DownstreamZOffsetM, plan.UpstreamZOffsetM });
                var start = points[0];
                var end = points[1];
                if (!TryBuildSolids(start, end, points[2] - points[0], plan, PipeSolidFactory.MaterialIdFor(doc, materialIds, plan.Placement.PipeKind), out var solids, out var isSolid))
                {
                    failed++;
                    continue;
                }
                if (isSolid) solidReducers++;
                var shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                shape.SetShape(solids.Cast<GeometryObject>().ToList());
                var large = Math.Max(plan.Placement.UpstreamDiameterMm, plan.Placement.DownstreamDiameterMm);
                var small = Math.Min(plan.Placement.UpstreamDiameterMm, plan.Placement.DownstreamDiameterMm);
                shape.Name = $"이경관_{plan.Placement.PipeKind}_{large:0}x{small:0}";
                if (info is { Enabled: true }) WriteParameters(writer, shape, plan, info, catalog!);
                count++;
            }
            catch (Exception ex)
            {
                // 사유·절점을 버리지 않는다 — 곡관 DirectShape와 같은 모양(docs/209 § G).
                failed++;
                failures.Add($"{plan.Placement.PipeKind} DN{plan.Placement.UpstreamDiameterMm:0}×{plan.Placement.DownstreamDiameterMm:0}: {ex.GetType().Name} — {ex.Message}");
            }
            finally { progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingReducers, count, plans.Count)); }
        }
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingReducers, plans.Count, plans.Count));

        var warnings = new List<string>();
        if (failed > 0) warnings.Add($"이경관 DirectShape 생성 예외 또는 퇴화 형상 {failed}곳을 건너뛰었습니다: {string.Join("; ", failures.Take(10))}{(failures.Count > 10 ? " …" : string.Empty)}");
        if (solidReducers > 0) warnings.Add($"이경관 제원에 두께가 없어 속 찬 원뿔대로 배치한 곳이 {solidReducers}곳 있습니다.");
        if (writer.InvalidTypes.Count > 0) warnings.Add($"프로젝트 매개변수 {string.Join(", ", writer.InvalidTypes.OrderBy(x => x))}의 타입이 배치 태그 계약과 달라 값을 건너뛰었습니다.");
        var missing = writer.Missing.Except(writer.InvalidTypes).OrderBy(x => x).ToList();
        if (missing.Count > 0) warnings.Add($"프로젝트 매개변수 {string.Join(", ", missing)}를 이경관 DirectShape에 기록하지 못했습니다(바인딩 실패 또는 읽기전용).");
        if (info is { Enabled: true } && writer.UnmappedJointWarning is { } jointCountWarning)
            warnings.Add(jointCountWarning);
        return new ReducerPlacementResult(count, warnings);
    }

    /// <param name="modelRunDirection">모델좌표 진행방향. 단부면은 이 방향에 직각이다 — 편심 이경관은
    /// 상·하류 Z 오프셋이 달라 축이 기울지만 단부면은 앞뒤 직관과 같은 관축 직각면이어야 한다.</param>
    /// <remarks>대경 원통 + 원뿔 + 소경 원통 최대 세 솔리드다 — 주철 B형(소경 200)·C형(대경 200)(docs/260 § G-4), 강관 C·E(docs/261).
    /// 원통은 진행방향과 평행하게 그쪽 끝 Z를 유지하고, 합집합 연산 없이 솔리드를 그대로 싣는다.</remarks>
    private static bool TryBuildSolids(XYZ start, XYZ end, XYZ modelRunDirection, ReducerDirectShapePlacementPlan plan, ElementId materialId,
        out IReadOnlyList<Solid> solids, out bool isSolid)
    {
        solids = Array.Empty<Solid>();
        isSolid = false;
        var axis = modelRunDirection;
        if (axis.GetLength() <= 1e-9) return false;
        axis = axis.Normalize();
        var seed = Math.Abs(axis.Z) > 0.9 ? XYZ.BasisX : XYZ.BasisZ;
        var xAxis = axis.CrossProduct(seed).Normalize();
        var yAxis = axis.CrossProduct(xAxis).Normalize();
        var profiles = plan.EndProfiles();
        var options = new SolidOptions(materialId, ElementId.InvalidElementId);
        // out 매개변수는 로컬 함수가 캡처할 수 없어 지역 변수로 받는다.
        var solidOnly = profiles.Upstream.WallThicknessMm <= 0d || profiles.Downstream.WallThicknessMm <= 0d
            || profiles.Upstream.OuterDiameterMm / 2d - profiles.Upstream.WallThicknessMm <= 0.01d
            || profiles.Downstream.OuterDiameterMm / 2d - profiles.Downstream.WallThicknessMm <= 0.01d;
        isSolid = solidOnly;

        // 대경·소경 원통 길이(docs/260 § G-4, docs/261). 상류가 대경이면 상류 쪽이 대경 원통이다.
        var placement = plan.Placement;
        var upstreamIsLarge = placement.UpstreamDiameterMm >= placement.DownstreamDiameterMm;
        var upstreamStraightFt = UC.MmToFt(upstreamIsLarge ? placement.LargeStraightMm : placement.SmallStraightMm);
        var downstreamStraightFt = UC.MmToFt(upstreamIsLarge ? placement.SmallStraightMm : placement.LargeStraightMm);
        var spanFt = (end - start).GetLength();
        if (upstreamStraightFt + downstreamStraightFt >= spanFt - 1e-6)
        {
            // 원통이 몸통을 다 차지하면(대체 길이가 짧은 경우 등) 원뿔 하나로 둔다.
            solids = new[] { Segment(start, end, profiles.Upstream, profiles.Downstream) };
            return true;
        }

        // 원통은 진행방향과 평행하게 그쪽 끝 Z를 유지하고, 원뿔이 두 원통 끝을 잇는다(편심이면 원뿔 축만 기운다).
        var taperStart = start + axis * upstreamStraightFt;
        var taperEnd = end - axis * downstreamStraightFt;
        var list = new List<Solid>(3);
        if (upstreamStraightFt > 1e-9) list.Add(Segment(start, taperStart, profiles.Upstream, profiles.Upstream));
        list.Add(Segment(taperStart, taperEnd, profiles.Upstream, profiles.Downstream));
        if (downstreamStraightFt > 1e-9) list.Add(Segment(taperEnd, end, profiles.Downstream, profiles.Downstream));
        solids = list;
        return true;

        Solid Segment(XYZ from, XYZ to, ReducerEndProfile fromProfile, ReducerEndProfile toProfile)
        {
            var outer = GeometryCreationUtilities.CreateBlendGeometry(
                PipeSolidFactory.CircleLoop(from, UC.MmToFt(fromProfile.OuterDiameterMm / 2d), xAxis, yAxis),
                PipeSolidFactory.CircleLoop(to, UC.MmToFt(toProfile.OuterDiameterMm / 2d), xAxis, yAxis), null, options);
            if (solidOnly) return outer;
            var inner = GeometryCreationUtilities.CreateBlendGeometry(
                PipeSolidFactory.CircleLoop(from, UC.MmToFt(fromProfile.OuterDiameterMm / 2d - fromProfile.WallThicknessMm), xAxis, yAxis),
                PipeSolidFactory.CircleLoop(to, UC.MmToFt(toProfile.OuterDiameterMm / 2d - toProfile.WallThicknessMm), xAxis, yAxis), null, options);
            return BooleanOperationsUtils.ExecuteBooleanOperation(outer, inner, BooleanOperationsType.Difference);
        }
    }

    private void WriteParameters(PipeParameterWriter writer, DirectShape shape, ReducerDirectShapePlacementPlan plan,
        PipeInfoParameterContext info, IPipeCatalog catalog)
    {
        var placement = plan.Placement;
        var large = Math.Max(placement.UpstreamDiameterMm, placement.DownstreamDiameterMm);
        var small = Math.Min(placement.UpstreamDiameterMm, placement.DownstreamDiameterMm);
        writer.Text(shape, PipeAlignmentParameters.Addin, PipeAlignmentParameters.AddinValue);
        writer.Text(shape, PipeAlignmentParameters.Part, PipeAlignmentParameters.ReducerPartValue);
        writer.Text(shape, PipeAlignmentParameters.NominalDiameter, PipeAlignmentParameters.DiameterText(large, small));
        // 수량산출 규격2 — 이경관은 작은 쪽이 분기 구경이다. DirectShape는 매번 새로 만들어지므로 그대로 쓴다.
        writer.Text(shape, PipeAlignmentParameters.BranchSpec, PipeAlignmentParameters.BranchSpecText(small));
        // 현장 절단 개소. 이경관도 관 부재라 기본 1을 적어 두고 사용자가 고친다.
        writer.Integer(shape, PipeAlignmentParameters.CutCount, _rules.PlannedReducerCutCount);
        writer.Text(shape, PipeAlignmentParameters.Material, PipeKindCatalog.MaterialOf(placement.PipeKind) is { } material ? PipeAlignmentParameters.Code(material) : string.Empty);
        writer.Text(shape, PipeAlignmentParameters.Grade, placement.PipeKind);
        writer.YesNo(shape, PipeAlignmentParameters.NightWork, info.IsNightWork);   // docs/141 § C-4
        writer.Text(shape, PipeAlignmentParameters.ElevationDatum, PipeAlignmentParameters.Label(info.ZDatum));
        // 이경관은 접합 1개소·부설 1개소다(docs/166 § 6). 곡관과 달리 편측/양측 구분이 없다.
        // 두 단부는 종류가 같고 규격이 다르므로 슬롯 2벌로 가른다 — `D2400` 1개소 + `D1650` 1개소.
        // 한 칸으로는 산출서(`수량산출` 90~93행)의 어느 규격 열에 들어갈지 적을 자리가 없다(docs/186 § 2-1·§ 5).
        writer.JointSlots(shape, PipeKindCatalog.MaterialOf(placement.PipeKind), info.BendConnection,
            new[] { (Spec: $"D{large:0}", Count: 1), (Spec: $"D{small:0}", Count: 1) },
            info.UseRestraintGland);
        writer.Integer(shape, PipeAlignmentParameters.LayingCount, _rules.PlannedReducerLayingCount);
        if (!placement.IsFallback && PipeKindCatalog.MaterialOf(placement.PipeKind) is { } weightMaterial
            && _rules.ReducerWeightKg(catalog, weightMaterial, info.BendConnection, plan.Form,
                info.JointType, info.SteelFittingClass, large, small) is { } weightKg)
            writer.MassKg(shape, PipeAlignmentParameters.Weight, weightKg);
        // 플랜지는 창에서 고른 접합 방식이 근거라 `2F`를 적는다 — 연결 패스가 이것을 보고 이경관 양쪽 플랜지 연결을 센다(docs/305 § 3-1).
        // 소켓은 `DH_접합구성`을 비워 둔다(docs/199 § 2, 2026-09-21 사용자 확정). DirectShape라 단부 플래그(`좌측_소켓`…)로
        // 소켓 단부를 정할 근거가 없다 — 필요하면 사용자가 속성창에서 `1S1F`처럼 직접 적는다.
        if (info.BendConnection == BendConnection.Flanged)
            writer.JointComposition(shape, info.BendConnection, endCount: 2, socketEnds: 2);
        // DirectShape는 측점편집 대상이 아니다(docs/214) — 수량산출이 읽는 셋만 남긴다.
        writer.Text(shape, PlacementStationParameters.Alignment, plan.RecordNumber);
        writer.StationM(shape, PlacementStationParameters.StaStart, placement.StaStartM);
        writer.StationM(shape, PlacementStationParameters.StaEnd, placement.StaEndM);
    }
}
