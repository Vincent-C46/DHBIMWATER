using Autodesk.Revit.DB;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Infrastructure.Helpers;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 문서 전체 연결 패스를 돌려 플랜지 슬롯 · 유입부 절단을 기록한다(docs/266 § 1). 값은 <see cref="PipeJointConnectionPass"/>가 정하고
/// 여기서는 쓰기만 한다. 신규 배치(PipeLayout) · 재기록 · 계획관로 배치가 같은 함수를 부른다.
/// <para>부재별 기록(<see cref="PipeJointCountWriter.Write"/>)이 소켓 슬롯을 다시 쓰면 플랜지 슬롯이 지워지므로, 그 뒤에 불러야 한다.
/// 기존 슬롯 중 플랜지가 아닌 것(소켓)은 그대로 두고 플랜지 슬롯만 갈아 끼운다. 값이 같은 부재는 건드리지 않는다.</para>
/// <para>트랜잭션은 열지 않는다. 호출부(UseCase)가 연 트랜잭션 안에서 부른다(CLAUDE.md).</para>
/// </summary>
internal static class PipeJointConnectionRecorder
{
    /// <summary>패스를 돌려 기록한다.</summary>
    /// <param name="warnings">짝 없는 플랜지 · 판정 불가 · 쓰기 실패 경고를 더한다.</param>
    /// <returns>값이 바뀐 부재 수.</returns>
    public static int Apply(Document document, ICollection<string> warnings)
    {
        var snapshot = PipeJointEndReader.Read(document);
        if (snapshot.Members.Count == 0) return 0;

        var result = PipeJointConnectionPass.Run(snapshot.Members, snapshot.Ends, snapshot.IsValveRoom);
        var writer = new PipeParameterWriter();
        var changed = new HashSet<long>();

        foreach (var (id, flangeSlots) in result.FlangeSlots)
        {
            if (!snapshot.Elements.TryGetValue(id, out var element)) continue;
            var existing = ReadSlots(element);
            var merged = existing.Where(x => !string.Equals(x.Kind, PipeCountRules.FlangeJointKind, StringComparison.Ordinal))
                .Concat(flangeSlots)
                .ToList();
            if (SameSlots(existing, merged)) continue;
            writer.JointSlots(element, merged.Select(x => (x.Kind, x.Spec, x.Count)).ToList());
            changed.Add(id);
        }

        foreach (var (id, cut) in result.CutCounts)
        {
            if (!snapshot.Elements.TryGetValue(id, out var element)) continue;
            var parameter = element.LookupParameter(PipeAlignmentParameters.CutCount);
            if (parameter is { StorageType: StorageType.Integer, HasValue: true } && parameter.AsInteger() == cut) continue;
            writer.Integer(element, PipeAlignmentParameters.CutCount, cut);
            changed.Add(id);
        }

        foreach (var warning in result.Warnings) warnings.Add(warning);
        var missing = writer.Missing.Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            warnings.Add($"연결 기준 접합·절단 매개변수 {string.Join(", ", missing)}에 값을 쓰지 못했습니다.");
        return changed.Count;
    }

    /// <summary>요소에 지금 적힌 슬롯 1..n. 종류가 비었거나 개수가 0이면 없는 슬롯이다(docs/186 § 4).</summary>
    private static List<PipeJointSlotValue> ReadSlots(Element element)
    {
        var slots = new List<PipeJointSlotValue>();
        for (var slot = 1; slot <= PipeAlignmentParameters.JointSlotDefinitionCount; slot++)
        {
            var kind = element.LookupParameter(PipeAlignmentParameters.JointSlotKind(slot))?.AsString();
            var count = element.LookupParameter(PipeAlignmentParameters.JointSlotCount(slot))?.AsInteger() ?? 0;
            if (string.IsNullOrWhiteSpace(kind) || count <= 0) continue;
            slots.Add(new PipeJointSlotValue(kind, element.LookupParameter(PipeAlignmentParameters.JointSlotSpec(slot))?.AsString() ?? string.Empty, count));
        }
        return slots;
    }

    private static bool SameSlots(IReadOnlyList<PipeJointSlotValue> a, IReadOnlyList<PipeJointSlotValue> b)
        => a.Count == b.Count && a.OrderBy(Key, StringComparer.Ordinal).SequenceEqual(b.OrderBy(Key, StringComparer.Ordinal));

    private static string Key(PipeJointSlotValue slot) => $"{slot.Kind}\u0001{slot.Spec}\u0001{slot.Count}";
}
