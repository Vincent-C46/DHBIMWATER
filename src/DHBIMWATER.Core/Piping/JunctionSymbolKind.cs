namespace DHBIMWATER.Core.Piping;

public enum JunctionSymbolKind
{
    Pipe,
    Tee,
    Gate,
    Air,
    Generic
}

public static class JunctionSymbolKindMapper
{
    public static JunctionSymbolKind Map(string? part, string? itemName)
    {
        var value = string.IsNullOrWhiteSpace(part) ? itemName?.Trim() : part.Trim();
        if (string.IsNullOrEmpty(value)) return JunctionSymbolKind.Generic;
        if (value.Contains("직관", StringComparison.Ordinal) || value.Contains("단관", StringComparison.Ordinal)) return JunctionSymbolKind.Pipe;
        if (value.Contains("T형", StringComparison.Ordinal) || value.Contains("T관", StringComparison.Ordinal)) return JunctionSymbolKind.Tee;
        if (value.Contains("제수", StringComparison.Ordinal) || value.Contains("게이트", StringComparison.Ordinal)) return JunctionSymbolKind.Gate;
        if (value.Contains("공기", StringComparison.Ordinal)) return JunctionSymbolKind.Air;
        return JunctionSymbolKind.Generic;
    }
}

public sealed record JunctionPreviewItem(long ElementId, int ItemNumber, string Name, string Spec,
    JunctionSymbolKind SymbolKind, int? Order, SymbolVector BasisX, SymbolVector BasisY, SymbolVector BasisZ,
    SymbolVector ViewUp, double RotationDegrees, bool Connected = true,
    SymbolVector ViewRight = default, JunctionSymbolShape? Shape = null)
{
    public string SymbolLabel => SymbolKind switch
    {
        JunctionSymbolKind.Pipe => "단관",
        JunctionSymbolKind.Tee => "T관",
        JunctionSymbolKind.Gate => "제수변",
        JunctionSymbolKind.Air => "공기변",
        _ => "기타"
    };
}

public readonly record struct ShapePoint(double X, double Y);

public sealed record JunctionSymbolShape(IReadOnlyList<IReadOnlyList<ShapePoint>> Polylines,
    double MinX, double MinY, double MaxX, double MaxY)
{
    public static JunctionSymbolShape FromPolylines(IReadOnlyList<IReadOnlyList<ShapePoint>> polylines)
    {
        var points = polylines.SelectMany(x => x).ToList();
        return points.Count == 0
            ? new JunctionSymbolShape(polylines, 0, 0, 0, 0)
            : new JunctionSymbolShape(polylines, points.Min(x => x.X), points.Min(x => x.Y),
                points.Max(x => x.X), points.Max(x => x.Y));
    }
}

public sealed record JunctionSymbolShapeDiagnostic(long ElementId, string Name, int CurveCount,
    int PointCount, double WidthMm, double HeightMm, string OffsetResponse, string Note);

public sealed record JunctionSymbolShapeReadResult(
    IReadOnlyDictionary<long, JunctionSymbolShape> Shapes,
    IReadOnlyList<JunctionSymbolShapeDiagnostic> Diagnostics);

public static class JunctionSymbolShapeMath
{
    public static SymbolVector RestoreOffset(SymbolVector point, SymbolVector localOffset,
        SymbolVector basisX, SymbolVector basisY, SymbolVector basisZ)
        => new(point.X - (basisX.X * localOffset.X + basisY.X * localOffset.Y + basisZ.X * localOffset.Z),
            point.Y - (basisX.Y * localOffset.X + basisY.Y * localOffset.Y + basisZ.Y * localOffset.Z),
            point.Z - (basisX.Z * localOffset.X + basisY.Z * localOffset.Y + basisZ.Z * localOffset.Z));
}

public sealed record JunctionPreviewPoint(JunctionPreviewItem Item, double X, double Y)
{
    public double CanvasLeft => X - 36;
    public double CanvasTop => Y - 21;
}

public static class JunctionPreviewLayout
{
    public static IReadOnlyList<JunctionPreviewPoint> Layout(IReadOnlyList<JunctionPreviewItem> items,
        double stepPaperMm, bool expandVertical, JunctionExpandAxis axis = JunctionExpandAxis.Y,
        bool positive = true)
    {
        const double originX = 270;
        const double originY = 370;
        return items.Select(item =>
        {
            var distance = expandVertical && item.Order is { } order ? order * stepPaperMm * 8 : 0;
            return new JunctionPreviewPoint(item,
                axis == JunctionExpandAxis.X ? originX + (positive ? distance : -distance) : originX,
                axis == JunctionExpandAxis.Y ? originY + (positive ? -distance : distance) : originY);
        }).ToList();
    }
}
