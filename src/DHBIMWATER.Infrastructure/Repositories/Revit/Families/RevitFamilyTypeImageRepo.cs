using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces.Families;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Families;

/// <summary>
/// 패밀리 문서 유형 이미지 지정(docs/309 § 4-1). 트랜잭션은 UseCase가 세션으로 연다.
/// <para>Import 방식 → 이미지가 .rfa 안에 저장. 사용자 PC에 원본 파일 불필요.</para>
/// </summary>
internal sealed class RevitFamilyTypeImageRepo : IFamilyTypeImageRepo
{
    public int Apply(IFamilyDocumentSession session, string imagePath)
    {
        var document = ((RevitFamilyDocumentSession)session).Document;
        var manager = document.FamilyManager;
        var parameter = manager.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_IMAGE)
            ?? throw new InvalidOperationException("이 패밀리 카테고리에는 유형 이미지 매개변수가 없습니다.");

        var types = manager.Types.Cast<FamilyType>().ToList();
        if (types.Count == 0)
            throw new InvalidOperationException("패밀리 유형이 없습니다. 패밀리 유형 대화상자에서 유형을 하나 만든 뒤 다시 실행하세요.");

        // 덮어쓰기 전 이전 이미지 수집 → 지정 후 안 쓰면 삭제(§ 2 기존 이미지 덮어쓰기).
        var oldImageIds = types.Select(t => t.AsElementId(parameter))
            .Where(id => id is not null && id != ElementId.InvalidElementId)
            .Distinct()
            .ToList();

        var image = ImageType.Create(document, new ImageTypeOptions(imagePath, false, ImageTypeSource.Import));

        // CurrentType 바꿔 가며 지정 → 끝나면 원래 현재 유형으로 복귀(저장 시 기본 유형 유지).
        var originalType = manager.CurrentType;
        foreach (var type in types)
        {
            manager.CurrentType = type;
            manager.Set(parameter, image.Id);
        }
        if (originalType is not null) manager.CurrentType = originalType;

        // 뷰에 놓인 이미지(ImageInstance)가 쓰는 유형은 남김.
        var placed = new FilteredElementCollector(document).OfClass(typeof(ImageInstance))
            .Select(e => e.GetTypeId()).ToHashSet();
        foreach (var oldId in oldImageIds.Where(id => id != image.Id && !placed.Contains(id)))
        {
            if (document.GetElement(oldId) is ImageType) document.Delete(oldId);
        }

        return types.Count;
    }
}
