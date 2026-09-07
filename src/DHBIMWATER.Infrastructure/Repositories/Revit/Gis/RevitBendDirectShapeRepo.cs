using Autodesk.Revit.DB;
using System.IO;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Infrastructure.Helpers;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Gis;

internal sealed class RevitBendDirectShapeRepo : IBendDirectShapeRepo
{
    private readonly Func<Document?> _doc;
    private readonly IProjectLocationCoordinateTransformRepo _coordinateTransform;
    public RevitBendDirectShapeRepo(Func<Document?> doc, IProjectLocationCoordinateTransformRepo coordinateTransform)
    { _doc = doc; _coordinateTransform = coordinateTransform; }

    public AdaptiveBendPlacementResult Place(IReadOnlyList<AdaptiveBendPlacementPlan> plans, AlignmentPlacementOrigin origin,
        PipeInfoParameterContext? info = null, IProgress<PipeAlignmentProgress>? progress = null)
    {
        if (plans.Count == 0) return new AdaptiveBendPlacementResult(0, Array.Empty<string>());
        var doc = _doc() ?? throw new InvalidOperationException("활성 Revit 문서가 없습니다.");
        var materialId = PipeSolidFactory.FindOrCreateDcipMaterial(doc);
        var writer = new PipeParameterWriter();
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
            try
            {
                var sourcePoints = new[] { plan.Points.Start, plan.Points.ArcStart, plan.Points.ArcMid, plan.Points.ArcEnd, plan.Points.End };
                var points = ProjectLocationCoordinateConverter.Convert(_coordinateTransform, sourcePoints,
                    Enumerable.Repeat(plan.ZOffsetM, sourcePoints.Length).ToArray());
                if (!TryBuildSolid(doc.Application.ShortCurveTolerance, points, plan, materialId, out var solid, out var failureReason))
                {
                    skipped++;
                    skippedNodes.Add($"{plan.NodeId}({failureReason})");
                    continue;
                }
                var shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                shape.SetShape(new GeometryObject[] { solid });
                shape.Name = $"곡관_{plan.PipeKind}_{plan.DiameterMm:0}_{plan.StandardAngleDeg:0.##}도";
                if (info is { Enabled: true }) WriteParameters(writer, shape, plan, info);
                if (plan.OuterDiameterMm is { } outerDiameterMm) outerDiameters[(plan.PipeKind, plan.DiameterMm)] = outerDiameterMm;
                if (!plan.IsAcceptable) BendOverrideGraphics.ApplyExceededOverride(overrideView, shape.Id, solidFillPatternId, plan.NodeId, overrideWarnings);
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

    private static void WriteParameters(PipeParameterWriter writer, DirectShape shape, AdaptiveBendPlacementPlan plan, PipeInfoParameterContext info)
    {
        writer.Text(shape, PipeAlignmentParameters.Addin, PipeAlignmentParameters.AddinValue);
        writer.Text(shape, PipeAlignmentParameters.Part, PipeAlignmentParameters.BendPartValue);
        writer.Text(shape, PipeAlignmentParameters.AlignmentId, $"{Path.GetFileNameWithoutExtension(plan.SourceFile)}#{plan.RecordNumber}");
        writer.Text(shape, PipeAlignmentParameters.NominalDiameter, PipeAlignmentParameters.DiameterText(plan.DiameterMm));
        writer.Text(shape, PipeAlignmentParameters.Material, PipeKindCatalog.MaterialOf(plan.PipeKind) is { } material ? PipeAlignmentParameters.Label(material) : string.Empty);
        writer.Text(shape, PipeAlignmentParameters.Grade, plan.PipeKind);
        writer.LengthMm(shape, PipeAlignmentParameters.OuterDiameter, plan.OuterDiameterMm ?? plan.DiameterMm);
        writer.LengthMm(shape, PipeAlignmentParameters.WallThickness, plan.WallThicknessEMm);
        writer.Text(shape, PipeAlignmentParameters.JointType, info.JointType);
        writer.Text(shape, PipeAlignmentParameters.ElevationDatum, PipeAlignmentParameters.Label(info.ZDatum));
        writer.Text(shape, PipeAlignmentParameters.SourceFile, Path.GetFileName(plan.SourceFile));
        writer.Text(shape, PipeAlignmentParameters.RecordNumber, plan.RecordNumber);
        writer.Integer(shape, PipeAlignmentParameters.NodeId, plan.NodeId);
        writer.AngleDeg(shape, PipeAlignmentParameters.ActualDeflection, plan.DeflectionDeg);
        writer.AngleDeg(shape, PipeAlignmentParameters.StandardAngle, plan.StandardAngleDeg);
        writer.AngleDeg(shape, PipeAlignmentParameters.AllowableDeflection, plan.EffectiveAllowableDeg);
        writer.AngleDeg(shape, PipeAlignmentParameters.ResidualDeflection, plan.ResidualDeg);
        writer.YesNo(shape, PipeAlignmentParameters.IsAcceptable, plan.IsAcceptable);
        writer.Text(shape, PipeAlignmentParameters.JointApplication, PipeAlignmentParameters.Label(info.ApplicationMode));
        writer.LengthMm(shape, PipeAlignmentParameters.CenterlineRadius, plan.CenterlineRadiusMm);
        writer.Text(shape, PipeAlignmentParameters.BendFormName, PipeAlignmentParameters.Label(plan.Form));
        writer.LengthMm(shape, PipeAlignmentParameters.LayingLength, plan.LayingLengthMm);
        writer.Number(shape, PipeAlignmentParameters.Weight, plan.WeightKg);
        writer.Text(shape, PipeAlignmentParameters.NodeStation, plan.NodeStationText);
        writer.Text(shape, PlacementStationParameters.SourceFile, plan.SourceFile);
        writer.Text(shape, PlacementStationParameters.Alignment, plan.RecordNumber);
        writer.Text(shape, PlacementStationParameters.Profile, string.Empty);
        writer.Number(shape, PlacementStationParameters.StaStart, plan.StaStartM);
        writer.Number(shape, PlacementStationParameters.StaEnd, plan.StaEndM);
        writer.Number(shape, PlacementStationParameters.ZOffset, plan.ZOffsetM);
        writer.YesNo(shape, PlacementStationParameters.Flip, false);
        writer.Text(shape, PlacementStationParameters.VerticalBasis, "GlobalZ");
        writer.Text(shape, PlacementStationParameters.ShapeKind, "Bend");
    }
}
