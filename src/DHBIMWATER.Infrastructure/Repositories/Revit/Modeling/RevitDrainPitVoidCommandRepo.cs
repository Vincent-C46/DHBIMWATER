using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>일반모델 Void를 기초 상단 EL에 놓고 기초·버림콘크리트를 자른다.</summary>
public sealed class RevitDrainPitVoidCommandRepo : IDrainPitVoidCommandRepo
{
    private readonly Func<Document?> _doc;
    public RevitDrainPitVoidCommandRepo(Func<Document?> doc) => _doc = doc;

    public string? CreateDrainPitVoid(IReadOnlyList<int> hostElementIds, long levelId,
        DrainPitDefinition definition, DrainPitVoidFamilyMappingDto mapping)
    {
        var doc = _doc() ?? throw new InvalidOperationException("활성 Revit 문서를 찾을 수 없습니다.");
        if (hostElementIds.Count == 0) throw new InvalidOperationException("드레인 피트 절단 대상이 없습니다.");
        if (string.IsNullOrWhiteSpace(mapping.DepthParameterName))
            throw new InvalidOperationException("드레인 피트 굴착 깊이를 결정하려면 Depth 매개변수를 매핑해야 합니다.");

        var separator = mapping.FamilyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        if (separator <= 0) throw new InvalidOperationException("드레인 피트 Void 패밀리 매핑이 올바르지 않습니다.");
        var familyName = mapping.FamilyTypeName[..separator];
        var typeName = mapping.FamilyTypeName[(separator + 3)..];
        var symbol = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .FirstOrDefault(x => x.Family.Name == familyName && x.Name == typeName);
        if (symbol == null) throw new InvalidOperationException($"드레인 피트 Void 패밀리 '{mapping.FamilyTypeName}'을(를) 프로젝트에 로드해야 합니다.");
        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

        var level = doc.GetElement(new ElementId(levelId)) as Level
            ?? throw new InvalidOperationException("드레인 피트 참조 레벨을 찾을 수 없습니다.");

        // RFA 원점은 피트 상단 중심. 기초 상면 EL에 그대로 놓는다(상단 여유 없음).
        var target = new XYZ(UC.MmToFt(definition.TopCenter.X), UC.MmToFt(definition.TopCenter.Y), UC.MmToFt(definition.TopCenter.Z));
        var instance = doc.Create.NewFamilyInstance(target, symbol, level, StructuralType.NonStructural);
        doc.Regenerate();
        AlignZ(doc, instance, target);

        Set(instance, mapping.LengthParameterName, definition.LengthX);
        Set(instance, mapping.WidthParameterName, definition.WidthY);
        Set(instance, mapping.DepthParameterName, definition.Depth);
        instance.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
        instance.LookupParameter("DH_Category")?.Set("개구부");
        instance.LookupParameter("DH_Part")?.Set("드레인피트");
        doc.Regenerate();

        var warnings = new List<string>();
        foreach (var hostId in hostElementIds)
        {
            var host = doc.GetElement(new ElementId(hostId));
            if (host == null) { warnings.Add($"드레인 피트 절단 대상(Id {hostId})을 찾을 수 없습니다."); continue; }
            var warning = Cut(doc, host, instance);
            if (warning != null) warnings.Add(warning);
        }
        return warnings.Count == 0 ? null : string.Join(" / ", warnings);
    }

    /// <summary>호스트 하나를 자른다. 이미 잘렸으면 건너뛴다.</summary>
    private static string? Cut(Document doc, Element host, FamilyInstance instance)
    {
        var before = Volume(host);
        if (InstanceVoidCutUtils.InstanceVoidCutExists(host, instance)) return null;
        if (!InstanceVoidCutUtils.CanBeCutWithVoid(host))
            return $"호스트 '{host.Name}'은(는) Void 인스턴스로 절단될 수 없습니다.";
        try { InstanceVoidCutUtils.AddInstanceVoidCut(doc, host, instance); doc.Regenerate(); }
        catch (Exception ex) { return $"'{host.Name}' Void 절단 실패: {ex.Message}"; }
        return before - Volume(host) > 1e-4
            ? null
            : $"'{host.Name}'에 절단 관계는 만들었지만 체적이 줄지 않았습니다. Void RFA의 '로드할 때 보이드로 절단' 설정을 확인하세요.";
    }

    /// <summary>레벨 배치가 Z를 바꾸면 목표 EL로 옮긴다.</summary>
    private static void AlignZ(Document doc, FamilyInstance instance, XYZ target)
    {
        if (instance.Location is not LocationPoint point) return;
        var dz = target.Z - point.Point.Z;
        if (Math.Abs(dz) < 1e-9) return;
        ElementTransformUtils.MoveElement(doc, instance.Id, new XYZ(0, 0, dz));
        doc.Regenerate();
    }

    private static void Set(FamilyInstance instance, string name, double value)
    {
        var parameter = instance.LookupParameter(name);
        if (parameter == null || parameter.IsReadOnly)
            throw new InvalidOperationException($"드레인 피트 Void 패밀리에서 쓰기 가능한 인스턴스 매개변수 '{name}'을(를) 찾을 수 없습니다. 유형 매개변수면 인스턴스 매개변수로 변경해야 합니다.");
        parameter.Set(UC.MmToFt(value));
    }

    private static double Volume(Element host)
    {
        var geometry = host.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        return geometry == null ? 0 : geometry.OfType<Solid>().Sum(s => s.Volume);
    }
}
