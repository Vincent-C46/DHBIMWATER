using Autodesk.Revit.DB;
using System.IO;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Infrastructure.Helpers;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Gis;

internal sealed class RevitReducerDirectShapeRepo : IReducerDirectShapeRepo
{
    private readonly Func<Document?> _doc;
    public RevitReducerDirectShapeRepo(Func<Document?> doc) => _doc = doc;

    public ReducerPlacementResult Place(IReadOnlyList<ReducerDirectShapePlacementPlan> plans, AlignmentPlacementOrigin origin,
        PipeInfoParameterContext? info = null, IProgress<PipeAlignmentProgress>? progress = null)
    {
        if (plans.Count == 0) return new ReducerPlacementResult(0, Array.Empty<string>());
        var doc = _doc() ?? throw new InvalidOperationException("활성 Revit 문서가 없습니다.");
        var basePoint = AlignmentPlacementMapper.GetProjectBasePoint(doc);
        var materialId = PipeSolidFactory.FindOrCreateDcipMaterial(doc);
        var writer = new PipeParameterWriter();
        var count = 0;
        var failed = 0;
        var solidReducers = 0;
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingReducers, 0, plans.Count));

        foreach (var plan in plans)
        {
            try
            {
                var start = AlignmentPlacementMapper.ToXyz(plan.Placement.UpstreamEnd, plan.ZOffsetM, origin.X, origin.Y, basePoint);
                var end = AlignmentPlacementMapper.ToXyz(plan.Placement.DownstreamEnd, plan.ZOffsetM, origin.X, origin.Y, basePoint);
                if (!TryBuildSolid(start, end, plan, materialId, out var solid, out var isSolid))
                {
                    failed++;
                    continue;
                }
                if (isSolid) solidReducers++;
                var shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                shape.SetShape(new GeometryObject[] { solid });
                var large = Math.Max(plan.Placement.UpstreamDiameterMm, plan.Placement.DownstreamDiameterMm);
                var small = Math.Min(plan.Placement.UpstreamDiameterMm, plan.Placement.DownstreamDiameterMm);
                shape.Name = $"이경관_{plan.Placement.PipeKind}_{large:0}x{small:0}";
                if (info is { Enabled: true }) WriteParameters(writer, shape, plan, info);
                count++;
            }
            catch (Exception) { failed++; }
            finally { progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingReducers, count, plans.Count)); }
        }
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingReducers, plans.Count, plans.Count));

        var warnings = new List<string>();
        if (failed > 0) warnings.Add($"이경관 DirectShape 생성 예외 또는 퇴화 형상 {failed}곳을 건너뛰었습니다.");
        if (solidReducers > 0) warnings.Add($"이경관 제원에 두께가 없어 속 찬 원뿔대로 배치한 곳이 {solidReducers}곳 있습니다.");
        if (writer.InvalidTypes.Count > 0) warnings.Add($"프로젝트 매개변수 {string.Join(", ", writer.InvalidTypes.OrderBy(x => x))}의 타입이 배치 태그 계약과 달라 값을 건너뛰었습니다.");
        var missing = writer.Missing.Except(writer.InvalidTypes).OrderBy(x => x).ToList();
        if (missing.Count > 0) warnings.Add($"프로젝트 매개변수 {string.Join(", ", missing)}를 이경관 DirectShape에 기록하지 못했습니다(바인딩 실패 또는 읽기전용).");
        return new ReducerPlacementResult(count, warnings);
    }

    private static bool TryBuildSolid(XYZ start, XYZ end, ReducerDirectShapePlacementPlan plan, ElementId materialId,
        out Solid solid, out bool isSolid)
    {
        solid = null!;
        isSolid = false;
        var axis = new XYZ(plan.Placement.RunDirection.X, plan.Placement.RunDirection.Y, plan.Placement.RunDirection.Z);
        if (axis.GetLength() <= 1e-9) return false;
        axis = axis.Normalize();
        var seed = Math.Abs(axis.Z) > 0.9 ? XYZ.BasisX : XYZ.BasisZ;
        var xAxis = axis.CrossProduct(seed).Normalize();
        var yAxis = axis.CrossProduct(xAxis).Normalize();
        var profiles = plan.EndProfiles();
        var options = new SolidOptions(materialId, ElementId.InvalidElementId);
        var outer = GeometryCreationUtilities.CreateBlendGeometry(
            PipeSolidFactory.CircleLoop(start, UC.MmToFt(profiles.Upstream.OuterDiameterMm / 2d), xAxis, yAxis),
            PipeSolidFactory.CircleLoop(end, UC.MmToFt(profiles.Downstream.OuterDiameterMm / 2d), xAxis, yAxis), null, options);
        var upstreamInner = profiles.Upstream.OuterDiameterMm / 2d - profiles.Upstream.WallThicknessMm;
        var downstreamInner = profiles.Downstream.OuterDiameterMm / 2d - profiles.Downstream.WallThicknessMm;
        if (profiles.Upstream.WallThicknessMm <= 0d || profiles.Downstream.WallThicknessMm <= 0d || upstreamInner <= 0.01d || downstreamInner <= 0.01d)
        {
            solid = outer;
            isSolid = true;
            return true;
        }
        var inner = GeometryCreationUtilities.CreateBlendGeometry(
            PipeSolidFactory.CircleLoop(start, UC.MmToFt(upstreamInner), xAxis, yAxis),
            PipeSolidFactory.CircleLoop(end, UC.MmToFt(downstreamInner), xAxis, yAxis), null, options);
        solid = BooleanOperationsUtils.ExecuteBooleanOperation(outer, inner, BooleanOperationsType.Difference);
        return true;
    }

    private static void WriteParameters(PipeParameterWriter writer, DirectShape shape, ReducerDirectShapePlacementPlan plan, PipeInfoParameterContext info)
    {
        var placement = plan.Placement;
        var large = Math.Max(placement.UpstreamDiameterMm, placement.DownstreamDiameterMm);
        var small = Math.Min(placement.UpstreamDiameterMm, placement.DownstreamDiameterMm);
        writer.Text(shape, PipeAlignmentParameters.Addin, PipeAlignmentParameters.AddinValue);
        writer.Text(shape, PipeAlignmentParameters.Part, PipeAlignmentParameters.ReducerPartValue);
        writer.Text(shape, PipeAlignmentParameters.AlignmentId, $"{Path.GetFileNameWithoutExtension(plan.SourceFile)}#{plan.RecordNumber}");
        writer.Text(shape, PipeAlignmentParameters.NominalDiameter, $"DN{large:0.##}×{small:0.##}");
        writer.Text(shape, PipeAlignmentParameters.Material, PipeKindCatalog.MaterialOf(placement.PipeKind) is { } material ? PipeAlignmentParameters.Label(material) : string.Empty);
        writer.Text(shape, PipeAlignmentParameters.Grade, placement.PipeKind);
        writer.LengthMm(shape, PipeAlignmentParameters.OuterDiameter, plan.LargeOuterDiameterMm);
        writer.LengthMm(shape, PipeAlignmentParameters.WallThickness, plan.LargeWallThicknessMm);
        writer.Text(shape, PipeAlignmentParameters.JointType, info.JointType);
        writer.Text(shape, PipeAlignmentParameters.ElevationDatum, PipeAlignmentParameters.Label(info.ZDatum));
        writer.Text(shape, PipeAlignmentParameters.SourceFile, Path.GetFileName(plan.SourceFile));
        writer.Text(shape, PipeAlignmentParameters.RecordNumber, plan.RecordNumber);
        writer.LengthMm(shape, PipeAlignmentParameters.LayingLength, placement.BodyLengthMm);
        writer.Text(shape, PlacementStationParameters.SourceFile, plan.SourceFile);
        writer.Text(shape, PlacementStationParameters.Alignment, plan.RecordNumber);
        writer.Text(shape, PlacementStationParameters.Profile, string.Empty);
        writer.Number(shape, PlacementStationParameters.StaStart, placement.StaStartM);
        writer.Number(shape, PlacementStationParameters.StaEnd, placement.StaEndM);
        writer.Number(shape, PlacementStationParameters.ZOffset, plan.ZOffsetM);
        writer.YesNo(shape, PlacementStationParameters.Flip, false);
        writer.Text(shape, PlacementStationParameters.VerticalBasis, "GlobalZ");
        writer.Text(shape, PlacementStationParameters.ShapeKind, "Reducer");
    }
}
