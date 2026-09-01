using Autodesk.Revit.DB;

namespace DHBIMWATER.Infrastructure.Helpers;

internal static class PipeSolidFactory
{
    private const string DcipMaterialName = "DCIP";

    internal static ElementId FindOrCreateDcipMaterial(Document doc)
    {
        var existing = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
            .FirstOrDefault(x => string.Equals(x.Name, DcipMaterialName, StringComparison.OrdinalIgnoreCase));
        return existing?.Id ?? Material.Create(doc, DcipMaterialName);
    }

    internal static CurveLoop CircleLoop(XYZ center, double radiusFt, XYZ xAxis, XYZ yAxis)
    {
        var loop = new CurveLoop();
        loop.Append(Arc.Create(center, radiusFt, 0, Math.PI, xAxis, yAxis));
        loop.Append(Arc.Create(center, radiusFt, Math.PI, 2 * Math.PI, xAxis, yAxis));
        return loop;
    }
}
