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
    SymbolVector ViewRight = default, JunctionSymbolShape? Shape = null,
    ShapePoint Position = default, bool IsVertical = true, long SetId = 0)
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

    // 직관 기호선은 한 줄이라 한쪽 폭 0 → 둘 중 하나만 있으면 그림
    public bool IsRenderable => Polylines.Count > 0 && Math.Max(MaxX - MinX, MaxY - MinY) > 0.01;
}

public sealed record JunctionSymbolShapeDiagnostic(long ElementId, string Name, int CurveCount,
    int PointCount, double WidthMm, double HeightMm, string OffsetResponse, string Note);

public sealed record JunctionSymbolShapeReadResult(
    IReadOnlyDictionary<long, JunctionSymbolShape> Shapes,
    IReadOnlyList<JunctionSymbolShapeDiagnostic> Diagnostics);

public static class JunctionSymbolShapeMath
{
    // 평면 투영 길이 이 미만 선 = 세로 방향 선(평면에서 점) → 기호선 아님. 캔버스 점·범위 부풀림 원인(docs/299)
    public const double MinPlanLengthMm = 1;

    public static bool IsVisibleInPlan(IReadOnlyList<ShapePoint> line)
    {
        var length = 0d;
        for (var i = 1; i < line.Count; i++)
            length += Math.Sqrt(Math.Pow(line[i].X - line[i - 1].X, 2) + Math.Pow(line[i].Y - line[i - 1].Y, 2));
        return length >= MinPlanLengthMm;
    }

    public static SymbolVector RestoreOffset(SymbolVector point, SymbolVector localOffset,
        SymbolVector basisX, SymbolVector basisY, SymbolVector basisZ)
        => new(point.X - (basisX.X * localOffset.X + basisY.X * localOffset.Y + basisZ.X * localOffset.Z),
            point.Y - (basisX.Y * localOffset.X + basisY.Y * localOffset.Y + basisZ.Y * localOffset.Z),
            point.Z - (basisX.Z * localOffset.X + basisY.Z * localOffset.Y + basisZ.Z * localOffset.Z));

    /// <summary>세계 X·Y 기호선 → 뷰 오른쪽·위쪽 축. 회전한 평면뷰에서도 부재 위치와 같은 축이라 기호끼리 이어짐.
    /// right·up 0 벡터면(평면뷰 정보 없음) 그대로.</summary>
    public static JunctionSymbolShape ToView(JunctionSymbolShape shape, SymbolVector right, SymbolVector up)
    {
        if (right is { X: 0, Y: 0 } || up is { X: 0, Y: 0 }) return shape;
        if (right is { X: 1, Y: 0 } && up is { X: 0, Y: 1 }) return shape;
        return JunctionSymbolShape.FromPolylines(shape.Polylines
            .Select(line => (IReadOnlyList<ShapePoint>)line
                .Select(p => new ShapePoint(p.X * right.X + p.Y * right.Y, p.X * up.X + p.Y * up.Y)).ToList())
            .ToList());
    }
}
