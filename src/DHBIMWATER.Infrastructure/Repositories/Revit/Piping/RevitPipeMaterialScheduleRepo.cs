using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

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

    /// <summary>표에 실제로 보이는 열. 순서가 곧 일람표 열 순서다(docs/131 § 0 표).</summary>
    private static readonly string[] VisibleFields =
    [
        ValvePipingParameters.ItemNumber,   // 번호
        ValvePipingParameters.ItemName,     // 명칭
        ValvePipingParameters.ItemSpec,     // 규격
        // 수량(Count)은 파라미터가 아니라 일람표 내장 필드다. AddCountField가 따로 넣는다.
        ValvePipingParameters.Remark,       // 비고
    ];

    public PipeMaterialScheduleScan Scan()
    {
        var document = _document() ?? throw new InvalidOperationException("활성 Revit 문서가 없습니다.");
        var junctionIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unassigned = 0;
        var total = 0;

        foreach (var instance in PipeMaterialPartCollector.AddinInstances(document))
        {
            total++;
            var junctionId = instance.LookupParameter(ValvePipingParameters.JunctionId)?.AsString();
            if (string.IsNullOrWhiteSpace(junctionId)) { unassigned++; continue; }
            if (seen.Add(junctionId)) junctionIds.Add(junctionId);
        }
        return new PipeMaterialScheduleScan(junctionIds, unassigned, total);
    }

    public bool ScheduleExists(string scheduleName)
    {
        var document = _document() ?? throw new InvalidOperationException("활성 Revit 문서가 없습니다.");
        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Any(x => !x.IsTemplate && string.Equals(x.Name, scheduleName, StringComparison.Ordinal));
    }

    public void CreateSchedule(string scheduleName, string junctionId)
    {
        var document = _document() ?? throw new InvalidOperationException("활성 Revit 문서가 없습니다.");

        var schedule = ViewSchedule.CreateSchedule(document, new ElementId(BuiltInCategory.OST_PipeAccessory));
        schedule.Name = scheduleName;
        var definition = schedule.Definition;

        // 이름 → 일람표에 넣을 수 있는 필드. 이름 조회를 반복하면 느려서 한 번만 만든다.
        var schedulable = definition.GetSchedulableFields()
            .GroupBy(x => x.GetName(document), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        var missing = new List<string>();
        var numberFieldId = (ScheduleFieldId?)null;

        foreach (var name in VisibleFields)
        {
            var field = AddField(definition, schedulable, name, missing);
            if (field is not null && name == ValvePipingParameters.ItemNumber) numberFieldId = field.FieldId;
        }

        AddCountField(document, definition);

        // 격점 필터는 DH_격점번호 열이 일람표에 있어야 걸 수 있다. 도면에는 보일 필요가 없으니 숨긴다.
        if (!string.IsNullOrWhiteSpace(junctionId))
        {
            var junctionField = AddField(definition, schedulable, ValvePipingParameters.JunctionId, missing);
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
            throw new InvalidOperationException(
                $"일람표 '{scheduleName}'에 넣을 매개변수 {string.Join(", ", missing.Distinct(StringComparer.Ordinal))}을(를) 찾지 못했습니다." +
                $"{Environment.NewLine}밸브실 배관을 한 번 생성해 배관 밸브류 카테고리에 공유 매개변수가 바인딩됐는지 확인하세요.");
    }

    private static ScheduleField? AddField(ScheduleDefinition definition,
        IReadOnlyDictionary<string, SchedulableField> schedulable, string parameterName, ICollection<string> missing)
    {
        if (!schedulable.TryGetValue(parameterName, out var field)) { missing.Add(parameterName); return null; }
        return definition.AddField(field);
    }

    /// <summary>수량 열. 파라미터가 아니라 일람표 내장 필드(<see cref="ScheduleFieldType.Count"/>)라
    /// 이름이 아니라 종류로 찾는다 — Revit 언어팩마다 표시 이름이 다르다.</summary>
    private static void AddCountField(Document document, ScheduleDefinition definition)
    {
        var countField = definition.GetSchedulableFields()
            .FirstOrDefault(x => x.FieldType == ScheduleFieldType.Count);
        if (countField is null) return;
        var added = definition.AddField(countField);
        // 열 제목은 언어팩 기본값("개수"/"Count") 대신 재료표 관례를 따른다.
        added.ColumnHeading = "수량";
    }
}
