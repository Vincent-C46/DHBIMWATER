using Autodesk.Revit.DB;

namespace DHBIMWATER.Infrastructure.Helpers;

internal static class BendOverrideGraphics
{
    internal static View3D? ResolveOverrideView(Document doc)
    {
        if (doc.ActiveView is View3D active && !active.IsTemplate) return active;
        return new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, "{3D}", StringComparison.Ordinal));
    }

    internal static ElementId FindSolidFillPatternId(Document doc) => new FilteredElementCollector(doc)
        .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
        .FirstOrDefault(x => x.GetFillPattern().IsSolidFill)?.Id ?? ElementId.InvalidElementId;

    internal static void ApplyExceededOverride(View3D? view, ElementId elementId, ElementId solidFillPatternId, int nodeId, List<string> warnings)
    {
        if (view is null)
        {
            if (!warnings.Any()) warnings.Add("허용 초과 곡관을 표시할 3D 뷰를 찾지 못해 적색 재지정을 건너뛰었습니다.");
            return;
        }
        try
        {
            var red = new Color(229, 57, 53);
            var settings = new OverrideGraphicSettings().SetProjectionLineColor(red).SetSurfaceForegroundPatternColor(red);
            if (solidFillPatternId != ElementId.InvalidElementId) settings.SetSurfaceForegroundPatternId(solidFillPatternId);
            view.SetElementOverrides(elementId, settings);
        }
        catch (Exception ex) { warnings.Add($"절점 {nodeId}: 3D 뷰 적색 표시를 적용하지 못했습니다({ex.Message})."); }
    }
}
