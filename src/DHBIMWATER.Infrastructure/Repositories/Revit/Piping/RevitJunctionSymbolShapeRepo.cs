using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Infrastructure.Converters;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

internal sealed class RevitJunctionSymbolShapeRepo : IJunctionSymbolShapeRepo
{
    private const int MaximumPoints = 5000;
    private readonly Func<Document?> _document;

    public RevitJunctionSymbolShapeRepo(Func<Document?> document) => _document = document;

    public JunctionSymbolShapeReadResult Read(IReadOnlyList<long> elementIds)
    {
        var document = _document() ?? throw new InvalidOperationException("활성 Revit 문서가 없습니다.");
        var instances = elementIds.Distinct().Select(id => document.GetElement(new ElementId(id)))
            .OfType<FamilyInstance>().ToList();
        var shapes = new Dictionary<long, JunctionSymbolShape>();
        var diagnostics = new List<JunctionSymbolShapeDiagnostic>();
        var foundIds = instances.Select(x => x.Id.Value).ToHashSet();
        diagnostics.AddRange(elementIds.Where(id => !foundIds.Contains(id)).Select(id =>
            new JunctionSymbolShapeDiagnostic(id, id.ToString(), 0, 0, 0, 0, "확인 안 함", "부재를 찾지 못함")));
        if (instances.Count == 0) return new(shapes, diagnostics);

        using var sub = new SubTransaction(document);
        sub.Start();
        try
        {
            var level = RevitJunctionDetailRepo.SelectLevel(document, null, instances)
                ?? throw new InvalidOperationException("대상 부재의 평면뷰 레벨을 찾을 수 없습니다.");
            var view = RevitJunctionDetailRepo.CreateView(document,
                $"__격점기호선읽기_{Guid.NewGuid():N}", level);
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                try { view.ViewTemplateId = ElementId.InvalidElementId; }
                catch (Exception ex)
                {
                    AddFailureDiagnostics(instances, diagnostics, $"뷰 템플릿 해제 실패: {ex.Message}");
                    sub.RollBack();
                    return new(shapes, diagnostics);
                }
            }
            view.DetailLevel = ViewDetailLevel.Coarse;
            RevitJunctionDetailRepo.ConfigureRange(view, level, instances);
            EnsurePipingCategoriesVisible(document, view);
            document.Regenerate();

            foreach (var instance in instances)
            {
                var transform = instance.GetTransform();
                var read = ReadGeometry(instance, view);
                var offset = ReadOffset(instance);
                var polylines = read.Polylines.Select(line => (IReadOnlyList<ShapePoint>)line
                    .Select(point => Project(JunctionSymbolShapeMath.RestoreOffset(ToVector(point - transform.Origin),
                        offset, ToVector(transform.BasisX), ToVector(transform.BasisY), ToVector(transform.BasisZ)),
                        view.RightDirection, view.UpDirection))
                    .ToList()).Where(line => line.Count >= 2).ToList();
                var points = polylines.SelectMany(x => x).ToList();
                var response = VerifyOffset(instance, view, diagnostics);
                string note = read.UsedSymbolGeometry ? "GetSymbolGeometry + GetTransform" : "GetInstanceGeometry";
                if (read.PointCount > MaximumPoints)
                {
                    note += "; 점이 너무 많음";
                    points.Clear();
                    polylines.Clear();
                }
                if (points.Count > 0)
                {
                    var shape = JunctionSymbolShape.FromPolylines(polylines);
                    if (shape.MaxX - shape.MinX > 0.01 && shape.MaxY - shape.MinY > 0.01)
                        shapes[instance.Id.Value] = shape;
                    else note += "; 크기가 0인 도형";
                }
                if (read.PointCount == 0) note += "; 기호선을 찾지 못함";
                diagnostics.Add(new JunctionSymbolShapeDiagnostic(instance.Id.Value, instance.Name,
                    read.CurveCount, read.PointCount, points.Count == 0 ? 0 : points.Max(x => x.X) - points.Min(x => x.X),
                    points.Count == 0 ? 0 : points.Max(x => x.Y) - points.Min(x => x.Y), response, note));
            }
            sub.RollBack();
            return new(shapes, diagnostics);
        }
        catch
        {
            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
            throw;
        }
    }

    private static GeometryRead ReadGeometry(FamilyInstance instance, ViewPlan view)
    {
        var options = new Options { View = view, ComputeReferences = false, IncludeNonVisibleObjects = false };
        var primary = new GeometryRead();
        Collect(instance.get_Geometry(options), primary, null, false);
        if (primary.PointCount > 0) return primary;
        var fallback = new GeometryRead { UsedSymbolGeometry = true };
        Collect(instance.get_Geometry(options), fallback, null, true);
        return fallback;
    }

    private static void Collect(GeometryElement? geometry, GeometryRead result, Transform? parent, bool useSymbolGeometry)
    {
        if (geometry is null) return;
        foreach (var item in geometry)
        {
            if (item is Curve curve)
            {
                var points = curve.Tessellate().Select(point => parent is null ? point : parent.OfPoint(point)).ToList();
                if (points.Count >= 2) { result.Polylines.Add(points); result.CurveCount++; result.PointCount += points.Count; }
            }
            else if (item is PolyLine polyLine)
            {
                var points = polyLine.GetCoordinates().Select(point => parent is null ? point : parent.OfPoint(point)).ToList();
                if (points.Count >= 2) { result.Polylines.Add(points); result.CurveCount++; result.PointCount += points.Count; }
            }
            else if (item is GeometryInstance nested)
            {
                if (useSymbolGeometry)
                {
                    var transform = parent is null ? nested.Transform : parent.Multiply(nested.Transform);
                    Collect(nested.GetSymbolGeometry(), result, transform, true);
                }
                else
                {
                    var pointCount = result.PointCount;
                    Collect(nested.GetInstanceGeometry(), result, null, false);
                    if (result.PointCount == pointCount)
                    {
                        result.UsedSymbolGeometry = true;
                        Collect(nested.GetSymbolGeometry(), result, nested.Transform, true);
                    }
                }
            }
        }
    }

    private static ShapePoint Project(SymbolVector point, XYZ right, XYZ up)
    {
        var xyz = new XYZ(point.X, point.Y, point.Z);
        return new ShapePoint(RevitUnitConverter.FtToMm(xyz.DotProduct(right)),
            RevitUnitConverter.FtToMm(xyz.DotProduct(up)));
    }

    private static SymbolVector ReadOffset(FamilyInstance instance)
    {
        double Read(string name)
        {
            var parameter = instance.LookupParameter(name);
            return parameter is { StorageType: StorageType.Double } ? parameter.AsDouble() : 0;
        }
        return new SymbolVector(Read(JunctionDetailParameters.SymbolOffsetX), Read(JunctionDetailParameters.SymbolOffsetY),
            Read(JunctionDetailParameters.SymbolOffsetZ));
    }

    private static string VerifyOffset(FamilyInstance instance, ViewPlan view,
        List<JunctionSymbolShapeDiagnostic> diagnostics)
    {
        var parameters = new[] { JunctionDetailParameters.SymbolOffsetX, JunctionDetailParameters.SymbolOffsetY }
            .Select(instance.LookupParameter).Where(x => x is { IsReadOnly: false, StorageType: StorageType.Double }).ToList();
        if (parameters.Count == 0)
            return new[] { JunctionDetailParameters.SymbolOffsetX, JunctionDetailParameters.SymbolOffsetY }
                .Any(name => instance.LookupParameter(name) is not null) ? "확인 안 함" : "매개변수 없음";

        var parameter = parameters[0];
        var original = parameter.AsDouble();
        try
        {
            var before = AverageWorldPoint(instance, view);
            if (before is null) return "확인 안 함";
            var delta = RevitUnitConverter.MmToFt(100);
            parameter.Set(original + delta);
            instance.Document.Regenerate();
            var after = AverageWorldPoint(instance, view);
            var direction = parameter.Definition.Name == JunctionDetailParameters.SymbolOffsetX ? XYZ.BasisX : XYZ.BasisY;
            var expected = instance.GetTransform().OfVector(direction * delta);
            var moved = after is not null && (after - before).DistanceTo(expected) <= RevitUnitConverter.MmToFt(5);
            return moved ? "반응함" : "반응 없음";
        }
        catch { return "확인 안 함"; }
        finally
        {
            try { parameter.Set(original); instance.Document.Regenerate(); }
            catch (Exception ex)
            {
                diagnostics.Add(new JunctionSymbolShapeDiagnostic(instance.Id.Value, instance.Name,
                    0, 0, 0, 0, "확인 안 함", $"오프셋 원복 실패: {ex.Message}"));
            }
        }
    }

    private static XYZ? AverageWorldPoint(FamilyInstance instance, ViewPlan view)
    {
        var read = ReadGeometry(instance, view);
        var points = read.Polylines.SelectMany(x => x).ToList();
        return points.Count == 0 ? null : new XYZ(points.Average(x => x.X), points.Average(x => x.Y), points.Average(x => x.Z));
    }

    private static void EnsurePipingCategoriesVisible(Document document, ViewPlan view)
    {
        // 배관 2종 + 일반 모델(심볼 .rfa 카테고리). 일반 모델 꺼지면 기호선 못 읽음 (docs/284)
        foreach (var categoryId in new[]
        {
            BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_GenericModel
        })
        {
            var category = Category.GetCategory(document, categoryId);
            if (category is null || !view.CanCategoryBeHidden(category.Id)) continue;
            view.SetCategoryHidden(category.Id, false);
            RevitJunctionDetailRepo.ShowSubCategories(view, category);
        }
    }

    private static void AddFailureDiagnostics(IEnumerable<FamilyInstance> instances,
        List<JunctionSymbolShapeDiagnostic> diagnostics, string note)
    {
        diagnostics.AddRange(instances.Select(x => new JunctionSymbolShapeDiagnostic(x.Id.Value,
            x.Name, 0, 0, 0, 0, "확인 안 함", note)));
    }

    private static SymbolVector ToVector(XYZ vector) => new(vector.X, vector.Y, vector.Z);

    private sealed class GeometryRead
    {
        public List<List<XYZ>> Polylines { get; } = [];
        public int CurveCount { get; set; }
        public int PointCount { get; set; }
        public bool UsedSymbolGeometry { get; set; }
    }
}
