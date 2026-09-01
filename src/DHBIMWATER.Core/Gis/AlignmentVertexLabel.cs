namespace DHBIMWATER.Core.Gis;

public static class AlignmentVertexLabel
{
    public static string Format(PipeAlignment alignment, int vertexIndex)
    {
        var station = alignment.VertexStationTexts is { } values && vertexIndex >= 0 && vertexIndex < values.Count
            ? values[vertexIndex]
            : null;
        return string.IsNullOrWhiteSpace(station)
            ? $"정점 {vertexIndex}"
            : $"측점 {station}(정점 {vertexIndex})";
    }
}
