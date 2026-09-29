using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>패밀리 의존성이 없는 GenericModel 대체 출력. 선은 DirectShape로 기록한다.
/// 부속(밸브 등)은 사용자가 일반모델 FamilySymbol을 선택했으면 실제 패밀리로 배치하고(2026-08-12 변경 —
/// 파이프처럼 커넥터 연결은 하지 않고 지점에만 놓는다), 선택하지 않았으면 기존처럼 DirectShape 자리표시자를 남긴다.</summary>
internal sealed class RevitPipeGenericModelCommandRepo : IPipeCommandRepo
{
    private readonly Func<Document?> _document;
    public RevitPipeGenericModelCommandRepo(Func<Document?> document) => _document = document;
    public PipeOutputMode OutputMode => PipeOutputMode.GenericModel;

    // docs/158 § F: 원본 삭제(SourceElementIdsToDelete)는 PipeAccessorySegment 경로 전용이다. 여기서는 무시한다.
    // docs/161: DH_Zone(밸브실 유형) 기록도 같다 — 이 경로는 network.Zone을 무시한다.
    public PipeCreationResult CreateNetwork(PipeNetworkDefinition network)
    {
        var document = _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        foreach (var edge in network.Edges)
        {
            var shape = DirectShape.CreateElement(document, new ElementId(BuiltInCategory.OST_GenericModel));
            shape.ApplicationId = "DHBIMWATER";
            shape.ApplicationDataId = edge.Id.ToString();
            shape.SetShape([Line.CreateBound(ToXyz(edge.Start, edge.Elevation, network.ReferencePoint), ToXyz(edge.End, edge.Elevation, network.ReferencePoint))]);
        }
        foreach (var fitting in network.InlineFittings)
        {
            var point = ToXyz(fitting.Position, fitting.Elevation, network.ReferencePoint);
            if (!string.IsNullOrWhiteSpace(fitting.FamilyTypeName))
            {
                var symbol = FindSymbol(document, fitting.FamilyTypeName)
                    ?? throw new DhbimwaterException(ErrorCodes.PL_FamilyNotFound, $"배관부속 패밀리 '{fitting.FamilyTypeName}'를 찾을 수 없습니다. 프로젝트에 로드돼 있는지 확인하세요.");
                if (!symbol.IsActive) { symbol.Activate(); document.Regenerate(); }
                document.Create.NewFamilyInstance(point, symbol, StructuralType.NonStructural);
                continue;
            }
            var shape = DirectShape.CreateElement(document, new ElementId(BuiltInCategory.OST_GenericModel));
            shape.ApplicationId = "DHBIMWATER";
            shape.ApplicationDataId = fitting.Id.ToString();
            shape.SetShape([Line.CreateBound(point, point + XYZ.BasisZ * UC.MmToFt(50))]);
        }
        return new PipeCreationResult($"배관 {network.Edges.Count}개를 생성했습니다.");
    }

    private static FamilySymbol? FindSymbol(Document document, string familyTypeName)
    {
        var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        if (separator <= 0 || separator >= familyTypeName.Length - 3) return null;
        var familyName = familyTypeName[..separator];
        var typeName = familyTypeName[(separator + 3)..];
        return new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .FirstOrDefault(x => x.FamilyName == familyName && x.Name == typeName);   // FamilyName은 요소 조회가 없다(docs/206 § H)
    }

    private static XYZ ToXyz(DHBIMWATER.Core.Geometry.Point2D point, double elevation, DHBIMWATER.Core.Geometry.Point2D referencePoint) => new(UC.MmToFt(point.X + referencePoint.X), UC.MmToFt(point.Y + referencePoint.Y), UC.MmToFt(elevation));
}
