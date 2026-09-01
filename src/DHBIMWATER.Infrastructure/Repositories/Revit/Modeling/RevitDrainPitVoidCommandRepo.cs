using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>Floor 상부면에 사각 Void 패밀리를 호스팅해 드레인 피트를 판다.</summary>
public sealed class RevitDrainPitVoidCommandRepo : IDrainPitVoidCommandRepo
{
    private const string FamilyName = "void_사각_면기반_2025";
    private readonly Func<Document?> _doc;
    public RevitDrainPitVoidCommandRepo(Func<Document?> doc) => _doc = doc;

    public string? CreateDrainPitVoid(int hostElementId, DrainPitDefinition definition)
    {
        var doc = _doc() ?? throw new InvalidOperationException("활성 Revit 문서를 찾을 수 없습니다.");
        var host = doc.GetElement(new ElementId(hostElementId)) ?? throw new InvalidOperationException("드레인 피트 호스트를 찾을 수 없습니다.");
        var symbol = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).WhereElementIsElementType().OfType<FamilySymbol>()
            .FirstOrDefault(item => item.FamilyName.Equals(FamilyName, StringComparison.OrdinalIgnoreCase));
        if (symbol == null) throw new InvalidOperationException($"면기반 Void 패밀리 '{FamilyName}'을(를) 프로젝트에 로드해야 합니다.");
        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

        var before = Volume(host);
        var desired = new XYZ(UC.MmToFt(definition.TopCenter.X), UC.MmToFt(definition.TopCenter.Y), UC.MmToFt(definition.TopCenter.Z));
        var face = FindTopFace(host, desired) ?? throw new InvalidOperationException("드레인 피트 위치의 호스트 상부면을 찾을 수 없습니다.");
        var instance = doc.Create.NewFamilyInstance(face.Reference, desired, XYZ.BasisX, symbol);
        Set(instance, "Length", definition.LengthX);
        Set(instance, "Width", definition.WidthY);
        Set(instance, "Depth", definition.Depth + DrainPitGeometry.TopOvershootMm);
        instance.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
        instance.LookupParameter("DH_Category")?.Set("개구부");
        instance.LookupParameter("DH_Part")?.Set("드레인피트");
        doc.Regenerate();
        if (before - Volume(host) > 1e-4 || InstanceVoidCutUtils.InstanceVoidCutExists(host, instance)) return null;
        if (!InstanceVoidCutUtils.CanBeCutWithVoid(host)) return "호스트 요소가 Void 인스턴스로 절단될 수 없습니다.";
        try { InstanceVoidCutUtils.AddInstanceVoidCut(doc, host, instance); doc.Regenerate(); }
        catch (Exception ex) { return $"Void 절단 실패: {ex.Message}"; }
        return before - Volume(host) > 1e-4 ? null : "절단 관계는 만들었지만 호스트 체적이 줄지 않았습니다. Void RFA 설정을 확인하세요.";
    }

    private static void Set(FamilyInstance instance, string name, double value)
    {
        var parameter = instance.LookupParameter(name);
        if (parameter == null || parameter.IsReadOnly)
            throw new InvalidOperationException($"면기반 Void 패밀리에서 쓰기 가능한 인스턴스 매개변수 '{name}'을(를) 찾을 수 없습니다. 유형 매개변수면 인스턴스 매개변수로 변경해야 합니다.");
        parameter.Set(UC.MmToFt(value));
    }

    private static PlanarFace? FindTopFace(Element host, XYZ desired)
    {
        var geometry = host.get_Geometry(new Options { ComputeReferences = true });
        if (geometry == null) return null;
        return geometry.OfType<Solid>().Where(s => s.Volume > 0).SelectMany(s => s.Faces.OfType<PlanarFace>())
            .FirstOrDefault(face => face.FaceNormal.DotProduct(XYZ.BasisZ) > 0.999 && face.Project(desired) is { } projection && face.IsInside(projection.UVPoint));
    }

    private static double Volume(Element host)
    {
        var geometry = host.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        return geometry == null ? 0 : geometry.OfType<Solid>().Sum(s => s.Volume);
    }
}
