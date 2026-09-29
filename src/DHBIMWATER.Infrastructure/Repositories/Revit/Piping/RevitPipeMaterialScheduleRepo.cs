using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 격점상세도 배관재료표를 Revit 일람표(<see cref="ViewSchedule"/>)로 만든다(docs/132).
/// <para>열·정렬·항목화는 전부 규약으로 고정한다. 사용자가 고를 것이 없다 —
/// 값은 <see cref="RevitPipeSegmentCommandRepo"/>가 배치 시점에 이미 다 기록했다(docs/131 § B).</para>
/// <para>트랜잭션은 열지 않는다. UseCase가 연다(CLAUDE.md).</para>
/// </summary>
internal sealed class RevitPipeMaterialScheduleRepo : IPipeMaterialScheduleRepo
{
    private readonly Func<Document?> _document;
    public RevitPipeMaterialScheduleRepo(Func<Document?> document) => _document = document;

    /// <summary>수량 열 앞에 오는 파라미터 열. 순서가 곧 일람표 열 순서다(docs/131 § 0 표, docs/140).</summary>
    private static readonly string[] LeadingFields =
    [
        ValvePipingParameters.ItemNumber,   // 번호
        ValvePipingParameters.ItemName,     // 명칭
        ValvePipingParameters.ItemSpec,     // 규격
        // 형상(유형 이미지)은 공유 매개변수가 아니라 Revit 내장 파라미터다. AddTypeImageField가 따로 넣는다.
        // 수량(Count)도 파라미터가 아니라 일람표 내장 필드다. AddCountField가 따로 넣는다.
    ];

    /// <summary>수량 열 뒤에 오는 파라미터 열(docs/140 § 0).</summary>
    private static readonly string[] TrailingFields =
    [
        ValvePipingParameters.Remark,       // 비고
    ];

    /// <summary>형상 열이 쓰는 Revit 내장 유형 파라미터(패밀리 유형의 "유형 이미지").
    /// 이름이 아니라 파라미터 ID로 찾는다 — Revit 언어팩마다 표시 이름이 다르다(AddCountField와 같은 이유).</summary>
    private static readonly ElementId TypeImageParameterId = new(BuiltInParameter.ALL_MODEL_TYPE_IMAGE);

    /// <summary>형상 열 제목. 드래프팅뷰 도면 표(RevitPipeMaterialDraftingRepo.Columns)와 같은 이름이어야
    /// 같은 격점상세도 안에서 두 표의 열 제목이 어긋나지 않는다.</summary>
    private const string ShapeColumnHeading = "형상";

    public IReadOnlyList<PipeMaterialPart> GetParts()
        => PipeMaterialPartCollector.Read(Document());

    /// <summary>종류를 가리지 않는다 — 다른 종류의 뷰가 같은 이름을 쓰고 있어도 schedule.Name 대입이 던진다(docs/208 § C-4).</summary>
    public IReadOnlySet<string> GetExistingViewNames()
        => new FilteredElementCollector(Document())
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(x => !x.IsTemplate)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

    public void CreateSchedule(string scheduleName, string junctionId)
    {
        var document = Document();
        // 격점 하나의 실패가 나머지 격점까지 되돌리지 않도록 그 격점의 변경만 묶는다(docs/208 § C-1).
        // Transaction은 UseCase가 연다(CLAUDE.md) — SubTransaction은 그 안에서만 동작한다.
        using var sub = new SubTransaction(document);
        sub.Start();
        try { CreateScheduleCore(document, scheduleName, junctionId); sub.Commit(); }
        catch { sub.RollBack(); throw; }
    }

    private Document Document()
        => _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");

    private static void CreateScheduleCore(Document document, string scheduleName, string junctionId)
    {

        // 밸브류(인라인 밸브)와 이음쇠(관·곡관·T형)를 한 표에 담아야 하므로 다중 카테고리 일람표다(docs/133 § 3-5).
        // 다중 카테고리는 InvalidElementId로 만든다 — BuiltInCategory에는 해당 항목이 없다(RevitAPI: "InvalidElementId for a multi-category schedule").
        var schedule = ViewSchedule.CreateSchedule(document, ElementId.InvalidElementId);
        schedule.Name = scheduleName;
        var definition = schedule.Definition;

        // schedulable 목록은 다중 카테고리라 수백~수천 개다. 한 번만 받아 세 곳(파라미터·형상·수량)이 나눠 쓴다(docs/208 § D-4).
        var fields = definition.GetSchedulableFields();
        // 이름은 카테고리·인스턴스/유형·외부 애드인 파라미터가 겹친다(docs/208 § 0-3). 공유 매개변수 GUID → ElementId로 고른다.
        // GUID가 다른 옛 바인딩(RevitSharedParameterRepository 주석의 경우)만 이름으로 폴백한다.
        var byParameterId = fields.GroupBy(x => x.ParameterId).ToDictionary(x => x.Key, x => x.First());
        SchedulableField? Find(string name)
        {
            if (ValvePipingParameters.Guids.TryGetValue(name, out var guid)
                && SharedParameterElement.Lookup(document, guid) is { } element
                && byParameterId.TryGetValue(element.Id, out var byId))
                return byId;
            return fields.FirstOrDefault(x => string.Equals(x.GetName(document), name, StringComparison.Ordinal));
        }

        var missing = new List<string>();
        var numberFieldId = (ScheduleFieldId?)null;

        foreach (var name in LeadingFields)
        {
            var field = AddField(definition, Find, name, missing);
            if (field is not null && name == ValvePipingParameters.ItemNumber) numberFieldId = field.FieldId;
        }

        // 이 세 줄의 순서가 곧 형상·수량·비고 열 순서다(docs/140 § 0). 바꾸지 말 것.
        AddTypeImageField(definition, fields, missing);
        AddCountField(definition, fields);
        foreach (var name in TrailingFields)
            AddField(definition, Find, name, missing);

        // 다중 카테고리 일람표는 카테고리로 범위를 좁힐 수 없다. 지금까지 카테고리가 하던 역할을 DH_Addin 필터가 대신한다 —
        // 없으면 DHBIMWATER가 만들지 않은 밸브류·이음쇠까지 표에 섞인다(docs/133 § 3-5).
        var addinField = AddField(definition, Find, ValvePipingParameters.Addin, missing);
        if (addinField is not null)
        {
            addinField.IsHidden = true;
            definition.AddFilter(new ScheduleFilter(
                addinField.FieldId, ScheduleFilterType.Equal, ValvePipingParameters.AddinValue));
        }

        // 격점 필터는 DH_격점번호 열이 일람표에 있어야 걸 수 있다. 도면에는 보일 필요가 없으니 숨긴다.
        if (!string.IsNullOrWhiteSpace(junctionId))
        {
            var junctionField = AddField(definition, Find, ValvePipingParameters.JunctionId, missing);
            if (junctionField is not null)
            {
                junctionField.IsHidden = true;
                definition.AddFilter(new ScheduleFilter(junctionField.FieldId, ScheduleFilterType.Equal, junctionId));
            }
        }

        // 수량은 항목화를 꺼야 집계된다. 켜 두면 인스턴스마다 한 줄이라 재료표가 아니라 부재 목록이 된다(docs/131 § 0-3).
        definition.IsItemized = false;

        // 정렬은 부재번호 오름차순. Integer 파라미터라 1·2·10 순으로 나온다(docs/131 § A-2).
        if (numberFieldId is not null)
            definition.AddSortGroupField(new ScheduleSortGroupField(numberFieldId, ScheduleSortOrder.Ascending));

        if (missing.Count > 0)
            throw new DhbimwaterException(ErrorCodes.MT_ScheduleFieldMissing,
                $"일람표 '{scheduleName}'에 넣을 열 {string.Join(", ", missing.Distinct(StringComparer.Ordinal))}을(를) 찾지 못했습니다." +
                $"{Environment.NewLine}DH_로 시작하는 이름이 있으면, 밸브실 배관을 한 번 생성해 " +
                $"배관 밸브류·배관 이음쇠 카테고리에 공유 매개변수가 바인딩됐는지 확인하세요.");
    }

    private static ScheduleField? AddField(ScheduleDefinition definition,
        Func<string, SchedulableField?> find, string parameterName, ICollection<string> missing)
    {
        if (find(parameterName) is not { } field) { missing.Add(parameterName); return null; }
        return definition.AddField(field);
    }

    /// <summary>형상 열. 패밀리 유형에 넣어 둔 png를 그대로 끌어온다 —
    /// 값을 쓰는 코드는 없다. 이미지를 붙이는 것은 패밀리 쪽 작업이다(docs/140).
    /// <para>내장 파라미터라 이름이 아니라 <see cref="TypeImageParameterId"/>로 찾는다.</para></summary>
    private static void AddTypeImageField(ScheduleDefinition definition, IList<SchedulableField> fields, ICollection<string> missing)
    {
        var field = fields.FirstOrDefault(x => x.ParameterId == TypeImageParameterId);
        if (field is null) { missing.Add(ShapeColumnHeading); return; }
        // 열 제목은 언어팩 기본값("유형 이미지"/"Type Image") 대신 재료표 관례를 따른다.
        definition.AddField(field).ColumnHeading = ShapeColumnHeading;
    }

    /// <summary>수량 열. 파라미터가 아니라 일람표 내장 필드(<see cref="ScheduleFieldType.Count"/>)라
    /// 이름이 아니라 종류로 찾는다 — Revit 언어팩마다 표시 이름이 다르다.</summary>
    private static void AddCountField(ScheduleDefinition definition, IList<SchedulableField> fields)
    {
        var countField = fields.FirstOrDefault(x => x.FieldType == ScheduleFieldType.Count);
        if (countField is null) return;
        var added = definition.AddField(countField);
        // 열 제목은 언어팩 기본값("개수"/"Count") 대신 재료표 관례를 따른다.
        added.ColumnHeading = "수량";
    }
}
