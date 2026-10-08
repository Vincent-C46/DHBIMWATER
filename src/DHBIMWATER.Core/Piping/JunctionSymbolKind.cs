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
        if (value.Contains("제수", StringComparison.Ordinal)) return JunctionSymbolKind.Gate;
        if (value.Contains("공기", StringComparison.Ordinal)) return JunctionSymbolKind.Air;
        return JunctionSymbolKind.Generic;
    }
}

public sealed record JunctionPreviewItem(long ElementId, int ItemNumber, string Name, string Spec,
    JunctionSymbolKind SymbolKind, int? Order, SymbolVector BasisX, SymbolVector BasisY, SymbolVector BasisZ,
    SymbolVector WorldUp, double RotationDegrees, bool Connected = true)
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

public sealed record JunctionPreviewPoint(JunctionPreviewItem Item, double X, double Y)
{
    public double CanvasLeft => X - 36;
    public double CanvasTop => Y - 21;
}

public static class JunctionPreviewLayout
{
    public static IReadOnlyList<JunctionPreviewPoint> Layout(IReadOnlyList<JunctionPreviewItem> items,
        double stepPaperMm, bool expandVertical)
    {
        const double originX = 270;
        const double originY = 370;
        return items.Select(item => new JunctionPreviewPoint(item, originX,
            expandVertical && item.Order is { } order ? originY - order * stepPaperMm * 8 : originY)).ToList();
    }
}
