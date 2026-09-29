using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>일반모델 패밀리를 기초 상단 EL에 놓는다. Void 패밀리면 기초·버림콘크리트를 자르고,
/// 실물 피트 패밀리면 절단 없이 배치만 한다(docs/153 § E-3).</summary>
public sealed class RevitDrainPitVoidCommandRepo : IDrainPitVoidCommandRepo
{
    private readonly Func<Document?> _doc;
    public RevitDrainPitVoidCommandRepo(Func<Document?> doc) => _doc = doc;

    public DrainPitPlacementResult CreateDrainPitVoid(IReadOnlyList<int> hostElementIds, long levelId,
        DrainPitDefinition definition, DrainPitVoidFamilyMappingDto mapping)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        if (hostElementIds.Count == 0) throw new DhbimwaterException(ErrorCodes.VR_NoCutHost, "드레인 피트 절단 대상이 없습니다.");
        if (string.IsNullOrWhiteSpace(mapping.DepthParameterName))
            throw new DhbimwaterException(ErrorCodes.VR_DrainPitMappingRequired, "드레인 피트 굴착 깊이를 결정하려면 Depth 매개변수를 매핑해야 합니다.");

        var separator = mapping.FamilyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        if (separator <= 0) throw new DhbimwaterException(ErrorCodes.VR_DrainPitMappingRequired, "드레인 피트 Void 패밀리 매핑이 올바르지 않습니다.");
        var familyName = mapping.FamilyTypeName[..separator];
        var typeName = mapping.FamilyTypeName[(separator + 3)..];
        var symbol = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .FirstOrDefault(x => x.Family.Name == familyName && x.Name == typeName);
        if (symbol == null) throw new DhbimwaterException(ErrorCodes.VR_VoidFamilyNotLoaded, $"드레인 피트 Void 패밀리 '{mapping.FamilyTypeName}'을(를) 프로젝트에 로드해야 합니다.");
        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

        // 패밀리의 "로드할 때 보이드로 절단" 설정. 켜져 있으면 절단용 Void, 꺼져 있으면 실물 피트다.
        // 실물 패밀리에 AddInstanceVoidCut을 걸면 실패하거나 체적이 줄지 않아 경고만 쏟아진다(docs/153 § E-1).
        var isVoidCutting = symbol.Family
            .get_Parameter(BuiltInParameter.FAMILY_ALLOW_CUT_WITH_VOIDS)?.AsInteger() == 1;

        var level = doc.GetElement(new ElementId(levelId)) as Level
            ?? throw new DhbimwaterException(ErrorCodes.VR_LevelNotFound, "드레인 피트 참조 레벨을 찾을 수 없습니다.");

        // RFA 원점은 피트 상단 중심. 기초 상면 EL에 그대로 놓는다(상단 여유 없음).
        var target = new XYZ(UC.MmToFt(definition.TopCenter.X), UC.MmToFt(definition.TopCenter.Y), UC.MmToFt(definition.TopCenter.Z));
        var instance = doc.Create.NewFamilyInstance(target, symbol, level, StructuralType.NonStructural);
        doc.Regenerate();
        AlignZ(doc, instance, target);

        Set(instance, mapping.LengthParameterName, definition.LengthX);
        Set(instance, mapping.WidthParameterName, definition.WidthY);
        Set(instance, mapping.DepthParameterName, definition.Depth);
        instance.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
        instance.LookupParameter("DH_Category")?.Set(isVoidCutting ? "개구부" : "드레인피트");
        instance.LookupParameter("DH_Part")?.Set("드레인피트");
        doc.Regenerate();

        var warnings = new List<string>();
        if (isVoidCutting)
        {
            foreach (var hostId in hostElementIds)
            {
                var host = doc.GetElement(new ElementId(hostId));
                if (host == null) { warnings.Add($"드레인 피트 절단 대상(Id {hostId})을 찾을 수 없습니다."); continue; }
                var warning = Cut(doc, host, instance);
                if (warning != null) warnings.Add(warning);
            }
        }
        return new DrainPitPlacementResult((int)instance.Id.Value, isVoidCutting,
            warnings.Count == 0 ? null : string.Join(" / ", warnings));
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
            throw new DhbimwaterException(ErrorCodes.VR_VoidParameterUnwritable, $"드레인 피트 패밀리에서 쓰기 가능한 인스턴스 매개변수 '{name}'을(를) 찾을 수 없습니다. 유형 매개변수면 인스턴스 매개변수로 변경해야 합니다.");
        // 길이(실수형) 매개변수만 받는다 — 문자·정수형이면 Set(double)이 false를 돌려주고 피트 치수가 조용히 빠졌다(docs/238 § E).
        if (parameter.StorageType != StorageType.Double || !parameter.Set(UC.MmToFt(value)))
            throw new DhbimwaterException(ErrorCodes.VR_VoidParameterUnwritable, $"드레인 피트 매개변수 '{name}'에 길이 값을 쓰지 못했습니다. 길이(실수형) 인스턴스 매개변수를 고르세요.");
    }

    private static double Volume(Element host)
    {
        var geometry = host.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        return geometry == null ? 0 : geometry.OfType<Solid>().Sum(s => s.Volume);
    }
}
