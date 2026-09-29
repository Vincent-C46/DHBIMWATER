using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Services;
using DHBIMWATER.Core.Structures;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>문서에 로드된 독립기초 유형으로 공기밸브실용 유형과 인스턴스를 만든다.</summary>
public sealed class RevitFoundationCommandRepo : IFoundationCommandRepo
{
    private readonly Func<Document?> _doc;

    public RevitFoundationCommandRepo(Func<Document?> doc) => _doc = doc;

    public int CreateFoundationFromFirstInstance(FoundationDefinition definition)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var sourceType = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralFoundation)
            .WhereElementIsElementType()
            .OfType<FamilySymbol>()
            .OrderBy(symbol => symbol.Id.Value)
            .FirstOrDefault();
        // 이 Repo는 밸브실(공기밸브실)만 쓴다. 대화상자 + 0 반환이면 UseCase에서 2중 대화상자가 된다(docs/207 § B-3).
        if (sourceType == null)
            throw new DhbimwaterException(ErrorCodes.VR_FoundationTypeNotFound, "공기밸브실을 만들려면 독립기초 패밀리 유형을 하나 이상 로드해야 합니다.");

        var targetType = GetOrCreateAirValveFoundationType(doc, sourceType, definition);
        if (!targetType.IsActive)
        {
            targetType.Activate();
            doc.Regenerate();
        }

        var level = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .FirstOrDefault(item => item.Name.Equals(ValveRoomGeometryCalculator.BaseLevelName, StringComparison.OrdinalIgnoreCase))
            ?? throw new DhbimwaterException(ErrorCodes.VR_LevelNotFound, $"Level '{ValveRoomGeometryCalculator.BaseLevelName}'를 찾을 수 없습니다.");
        var targetTop = UC.MmToFt(definition.Position.Z);
        var foundation = doc.Create.NewFamilyInstance(
            new XYZ(UC.MmToFt(definition.Position.X), UC.MmToFt(definition.Position.Y), level.Elevation),
            targetType,
            level,
            StructuralType.Footing);
        var rotationCenter = new XYZ(UC.MmToFt(definition.Position.X), UC.MmToFt(definition.Position.Y), level.Elevation);
        ElementTransformUtils.RotateElement(
            doc,
            foundation.Id,
            Line.CreateBound(rotationCenter, rotationCenter + XYZ.BasisZ),
            -Math.PI / 2);
        doc.Regenerate();
        var foundationBox = foundation.get_BoundingBox(null)
            ?? throw new DhbimwaterException(ErrorCodes.VR_FoundationGeometryUnreadable, "생성한 독립기초의 위치 정보를 읽을 수 없습니다.");
        ElementTransformUtils.MoveElement(doc, foundation.Id, XYZ.BasisZ * (targetTop - foundationBox.Max.Z));
        foundation.LookupParameter("DH_Addin")?.Set("DHBIMWATER");
        foundation.LookupParameter("DH_Category")?.Set("기초");
        foundation.LookupParameter("DH_ElementCode")?.Set(definition.ElementCode);
        foundation.LookupParameter("DH_Part")?.Set(definition.Part);
        foundation.LookupParameter("DH_Zone")?.Set(definition.Zone);
        return (int)foundation.Id.Value;
    }

    private static FamilySymbol GetOrCreateAirValveFoundationType(Document doc, FamilySymbol sourceType, FoundationDefinition definition)
    {
        var typeName = $"공기밸브실 기초 - {definition.Length:0.##}x{definition.Width:0.##}x{definition.Thickness:0.##}mm";
        var existing = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralFoundation)
            .WhereElementIsElementType()
            .OfType<FamilySymbol>()
            .FirstOrDefault(symbol => symbol.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var duplicated = (FamilySymbol)sourceType.Duplicate(typeName);
        SetTypeDimension(duplicated, BuiltInParameter.STRUCTURAL_FOUNDATION_LENGTH, definition.Length, "길이");
        SetTypeDimension(duplicated, BuiltInParameter.STRUCTURAL_FOUNDATION_WIDTH, definition.Width, "폭");
        SetTypeDimension(duplicated, BuiltInParameter.STRUCTURAL_FOUNDATION_THICKNESS, definition.Thickness, "두께");
        return duplicated;
    }

    private static void SetTypeDimension(FamilySymbol symbol, BuiltInParameter parameterId, double valueMm, string name)
    {
        var parameter = symbol.get_Parameter(parameterId);
        if (parameter == null || parameter.IsReadOnly)
            throw new DhbimwaterException(ErrorCodes.VR_FoundationParameterMissing, $"독립기초 유형에서 기초 {name} 파라미터를 찾을 수 없습니다.");

        parameter.Set(UC.MmToFt(valueMm));
    }
}
