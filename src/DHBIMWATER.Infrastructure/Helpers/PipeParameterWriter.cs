using Autodesk.Revit.DB;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Helpers;

/// <summary>DH_*와 배치 태그 기록. 기존 이름 정의 재사용.</summary>
internal sealed class PipeParameterWriter
{
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private readonly HashSet<string> _invalidTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unmappedJointMaterials = new(StringComparer.Ordinal);
    public IReadOnlyCollection<string> Missing => _missing;
    public IReadOnlyCollection<string> InvalidTypes => _invalidTypes;

    public void Text(Element element, string name, string? value) => Write(element, name, p => p.Set(value ?? string.Empty), StorageType.String, SpecTypeId.String.Text);
    public void LengthMm(Element element, string name, double? millimeters)
    {
        if (millimeters is not null) Write(element, name, p => p.Set(UC.MmToFt(millimeters.Value)), StorageType.Double, SpecTypeId.Length);
    }
    public void LengthM(Element element, string name, double meters) => Write(element, name, p => p.Set(UC.MToFt(meters)), StorageType.Double, SpecTypeId.Length);
    public void AngleDeg(Element element, string name, double degrees) => Write(element, name, p => p.Set(UC.DegToRad(degrees)), StorageType.Double, SpecTypeId.Angle);
    public void Number(Element element, string name, double value) => Write(element, name, p => p.Set(value), StorageType.Double, SpecTypeId.Number);
    public void Integer(Element element, string name, int value) => Write(element, name, p => p.Set(value), StorageType.Integer, SpecTypeId.Int.Integer);
    public void YesNo(Element element, string name, bool value) => Write(element, name, p => p.Set(value ? 1 : 0), StorageType.Integer, SpecTypeId.Boolean.YesNo);

    /// <summary>접합 슬롯(<c>DH_접합{n}_종류</c>·<c>_규격</c>·<c>_수</c>)을 기록한다(docs/186 § 4·§ 6-2).
    /// <para>종류를 매개변수 <b>이름</b>이 아니라 <b>값</b>으로 적으므로 한 부재가 규격이 다른 접합 둘을 가질 수 있다 —
    /// 이경관 `D2400x1650`이 슬롯 2벌(`D2400` 1개소 + `D1650` 1개소)로 갈린다.</para>
    /// <para><paramref name="slots"/>가 비면(직관) 아무것도 쓰지 않는다. <b>빈 슬롯에 0을 채우지 않는다</b> —
    /// 슬롯은 "있으면 적는" 칸이라 `종류`가 비면 그 자체로 없는 것이다. 개소 매개변수 넷이 0을 명시적으로 쓰던 규칙
    /// (빈칸 = 미태깅과 구분)은 칸이 <b>고정</b>이라 필요했던 것이다.</para>
    /// <para>종류를 못 찾는 관종(PE·PVC 소켓)은 슬롯을 비우고 <see cref="UnmappedJointWarning"/>에 모은다.</para></summary>
    /// <param name="slots">슬롯 1벌 = (규격, 개수). 규격이 빈 문자열이면 수량산출이 요소 관경을 쓴다(docs/186 § 4-3).</param>
    public void JointSlots(Element element, PipeMaterial? material, BendConnection connection,
                           IReadOnlyList<(string Spec, int Count)> slots, bool useRestraintGland = false)
    {
        var kind = PipeAlignmentParameters.JointKindFor(material, connection, useRestraintGland);
        if (kind is null)
        {
            if (slots is { Count: > 0 })
                _unmappedJointMaterials.Add(material is { } m ? PipeAlignmentParameters.Label(m) : "관종 미상");
            Integer(element, PipeAlignmentParameters.JointTotalCount, 0);
            return;
        }

        JointSlots(element, slots?.Select(slot => (Kind: kind, Spec: slot.Spec, Count: slot.Count)).ToArray()
            ?? Array.Empty<(string Kind, string Spec, int Count)>());
    }

    public void JointSlots(Element element, IReadOnlyList<(string Kind, string Spec, int Count)> slots)
    {
        var writtenCount = 0;
        var grouped = (slots ?? Array.Empty<(string Kind, string Spec, int Count)>())
            .Where(slot => !string.IsNullOrWhiteSpace(slot.Kind) && slot.Count > 0)
            .GroupBy(slot => (slot.Kind, slot.Spec ?? string.Empty))
            .Select(group => (group.Key.Kind, group.Key.Item2, Count: group.Sum(slot => slot.Count)));

        var index = 0;
        foreach (var slotValue in grouped)
        {
            var slot = ++index;
            if (slot > PipeAlignmentParameters.JointSlotDefinitionCount)
            {
                _missing.Add(PipeAlignmentParameters.JointSlotKind(slot));
                continue;
            }
            Text(element, PipeAlignmentParameters.JointSlotKind(slot), slotValue.Kind);
            Text(element, PipeAlignmentParameters.JointSlotSpec(slot), slotValue.Item2);
            Integer(element, PipeAlignmentParameters.JointSlotCount(slot), slotValue.Count);
            writtenCount += slotValue.Count;
        }
        Integer(element, PipeAlignmentParameters.JointTotalCount, writtenCount);
    }

    /// <summary>접합 구성(<c>DH_접합구성</c>)을 적는다 — 플랜지면 단부 전부가 플랜지(`2F`), 소켓 계열(용접·KP·압륜)이면
    /// <b>소켓 단부 수</b>만 적는다(`1S`·`2S`). 스피곳(삽입) 단부는 접합 형상이 아니라 세지 않는다(docs/199, 2026-09-21).
    /// <para>docs/178 § B는 단부 수 기준 `2S`로 정했으나 <see cref="PipeJointComposition"/>의 정의(소켓 수 + 플랜지 수)와
    /// 어긋났다 — B형(한쪽 소켓)은 `1S`다. 밸브실(<see cref="PipeJointFlagReader"/>)이 단부 플래그를 세어 찍는 값과 같아진다.</para>
    /// <para><see cref="JointSlots"/>의 개소(편측 1·양측 2)와는 여전히 별개다 — 그쪽은 산출 개소, 이쪽은 부재의 실제 단부 형상이다.</para></summary>
    /// <param name="endCount">부재의 단부 수. 플랜지일 때 전부 플랜지로 적는다.</param>
    /// <param name="socketEnds">소켓 단부 수. 직관 1(항상), 곡관 A형 2·B형 1, 이경관 2.</param>
    public void JointComposition(Element element, BendConnection connection, int endCount, int socketEnds)
        => Text(element, PipeAlignmentParameters.JointComposition,
            connection == BendConnection.Flanged ? PipeJointComposition.Encode(0, endCount) : PipeJointComposition.Encode(socketEnds, 0));

    /// <summary>접합 종류를 정하지 못한 관종이 있으면 결과 창에 남길 한 줄. 전부 대응됐으면 null.
    /// <para>관종은 요소마다 다를 수 있어(한 실행에 여러 선형) 실행 단위가 아니라 <b>기록하면서</b> 모은다.</para></summary>
    public string? UnmappedJointWarning
        => _unmappedJointMaterials.Count == 0
            ? null
            : $"관종 {string.Join(", ", _unmappedJointMaterials.OrderBy(x => x, StringComparer.Ordinal))}은(는) 대응하는 접합 공종이 없어 "
                + $"접합 슬롯({PipeAlignmentParameters.JointSlotKind(1)} 계열)을 비워 두었습니다. "
                + "수량산출 애드인의 접합및부설 집계에서 이 요소들은 접합 공종으로 잡히지 않습니다.";

    private void Write(Element element, string name, Action<Parameter> set, StorageType expectedStorage, ForgeTypeId expectedDataType)
    {
        var parameter = FindParameter(element, name);
        if (parameter is not { IsReadOnly: false }
            || parameter.StorageType != expectedStorage
            || !parameter.Definition.GetDataType().Equals(expectedDataType))
        {
            _missing.Add(name);
            if (parameter is not null
                && (parameter.StorageType != expectedStorage || !parameter.Definition.GetDataType().Equals(expectedDataType)))
                _invalidTypes.Add(name);
            return;
        }
        set(parameter);
    }

    private static Parameter? FindParameter(Element element, string name)
    {
        // GUID 우선, 이름 폴백. 같은 이름을 다른 GUID로 먼저 바인딩한 문서(연동 애드인의 PlacementSourceTagger는
        // GUID 없이 만들어 매번 랜덤 GUID가 붙는다)에서는 EnsureParameters가 기존 정의를 그대로 쓰므로
        // 우리 GUID가 문서에 들어가지 않는다. GUID로만 찾으면 그 문서에서 DH_*가 통째로 기록되지 않는다(docs/141).
        if (PipeAlignmentParameters.Guids.TryGetValue(name, out var pipeGuid))
            return element.get_Parameter(pipeGuid) ?? element.LookupParameter(name);
        if (!PlacementStationParameters.Guids.TryGetValue(name, out var placementGuid)) return null;
        return element.get_Parameter(placementGuid) ?? element.LookupParameter(name);
    }
}
