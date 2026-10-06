using Autodesk.Revit.DB;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Application.Interfaces.Piping;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Helpers;

/// <summary>DH_*와 배치 태그 기록. 기존 이름 정의 재사용.</summary>
internal sealed class PipeParameterWriter
{
    private readonly IPipeQuantityRules _rules;
    public PipeParameterWriter(IPipeQuantityRules rules) => _rules = rules;
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
    /// <summary>질량(kg). 질량 형식 매개변수에만 쓴다(docs/264).</summary>
    public void MassKg(Element element, string name, double kilograms)
        => Write(element, name, p => p.Set(UC.KgToInternal(kilograms)), StorageType.Double, SpecTypeId.Mass);
    /// <summary>단위 길이당 질량(kg/m). 내부 단위가 kg/ft라 반드시 변환한다(docs/264).</summary>
    public void MassPerLengthKgPerM(Element element, string name, double kilogramsPerMeter)
        => Write(element, name, p => p.Set(UC.KgPerMToInternal(kilogramsPerMeter)), StorageType.Double, SpecTypeId.MassPerUnitLength);
    /// <summary>측점·Z 오프셋(m). 길이 형식(`ZOffset`)이면 피트로 바꾸고, Number 형식(`StaStart(m)`·`StaEnd(m)`, 옛 `ZOffset`)이면 m 숫자를 그대로 쓴다(docs/264).
    /// 매개변수가 없거나 둘 다 아니면 <see cref="Write"/>가 Missing/InvalidTypes로 처리한다.</summary>
    public void StationM(Element element, string name, double meters)
    {
        if (FindParameter(element, name)?.Definition.GetDataType().Equals(SpecTypeId.Length) == true)
            Write(element, name, p => p.Set(UC.MToFt(meters)), StorageType.Double, SpecTypeId.Length);
        else
            Write(element, name, p => p.Set(meters), StorageType.Double, SpecTypeId.Number);
    }
    public void Integer(Element element, string name, int value) => Write(element, name, p => p.Set(value), StorageType.Integer, SpecTypeId.Int.Integer);
    public void YesNo(Element element, string name, bool value) => Write(element, name, p => p.Set(value ? 1 : 0), StorageType.Integer, SpecTypeId.Boolean.YesNo);

    /// <summary>접합 슬롯(<c>DH_접합{n}_종류</c>·<c>_규격</c>·<c>_수</c>)을 기록한다(docs/186 § 4·§ 6-2).
    /// <para>종류를 매개변수 <b>이름</b>이 아니라 <b>값</b>으로 적으므로 한 부재가 규격이 다른 접합 둘을 가질 수 있다 —
    /// 이경관 `D2400x1650`이 슬롯 2벌(`D2400` 1개소 + `D1650` 1개소)로 갈린다.</para>
    /// <para>슬롯은 "있으면 적는" 칸이라 `종류`가 비면 그 자체로 없는 것이다. 개소 매개변수 넷이 0을 명시적으로 쓰던 규칙
    /// (빈칸 = 미태깅과 구분)은 칸이 <b>고정</b>이라 필요했던 것이다. 다만 <b>다시 기록</b>할 때 쓴 벌 뒤의 옛 슬롯은 비운다 —
    /// 남겨 두면 수량산출이 옛 `종류`·`수`까지 세어 접합이 과다 계상된다(docs/243 § A).</para>
    /// <para>종류를 못 찾는 관종(PE·PVC 소켓)은 슬롯을 비우고 <see cref="UnmappedJointWarning"/>에 모은다.</para>
    /// <para>플랜지는 슬롯을 비운다 — 플랜지 접합은 연결 패스(<c>PipeJointConnectionRecorder</c>)가 모델 연결 1건 = 1개소로 세어 따로 적는다(docs/265 § 2, docs/266 § 1).</para></summary>
    /// <param name="slots">슬롯 1벌 = (규격, 개수). 규격이 빈 문자열이면 수량산출이 요소 관경을 쓴다(docs/186 § 4-3).</param>
    public void JointSlots(Element element, PipeMaterial? material, BendConnection connection,
                           IReadOnlyList<(string Spec, int Count)> slots, bool useRestraintGland = false)
    {
        if (connection == BendConnection.Flanged)
        {
            // 의도된 빈 슬롯이다 — 매핑 실패(_unmappedJointMaterials)로 세지 않는다. 빈 목록이 옛 슬롯을 비우고 DH_접합수 = 0을 쓴다.
            JointSlots(element, Array.Empty<(string Kind, string Spec, int Count)>());
            return;
        }
        var kind = _rules.JointKindFor(material, connection, useRestraintGland);
        if (kind is null)
        {
            if (slots is { Count: > 0 })
                _unmappedJointMaterials.Add(material is { } m ? PipeAlignmentParameters.Label(m) : "관종 미상");
            // 빈 목록으로 부르면 옛 슬롯 1..3을 비우고 DH_접합수 = 0을 쓴다(docs/243 § A).
            JointSlots(element, Array.Empty<(string Kind, string Spec, int Count)>());
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
        for (var slot = index + 1; slot <= PipeAlignmentParameters.JointSlotDefinitionCount; slot++)
            ClearSlot(element, slot);
        Integer(element, PipeAlignmentParameters.JointTotalCount, writtenCount);
    }

    /// <summary>다시 기록할 때 남은 옛 슬롯을 비운다. 매개변수가 없으면 남은 값도 없으므로 조용히 건너뛴다 — <c>Missing</c>을 늘리지 않는다.</summary>
    private static void ClearSlot(Element element, int slot)
    {
        Clear(element, PipeAlignmentParameters.JointSlotKind(slot), StorageType.String, p => p.Set(string.Empty));
        Clear(element, PipeAlignmentParameters.JointSlotSpec(slot), StorageType.String, p => p.Set(string.Empty));
        Clear(element, PipeAlignmentParameters.JointSlotCount(slot), StorageType.Integer, p => p.Set(0));
    }

    private static void Clear(Element element, string name, StorageType storage, Action<Parameter> set)
    {
        if (FindParameter(element, name) is { IsReadOnly: false } parameter && parameter.StorageType == storage) set(parameter);
    }

    /// <summary>접합 구성(<c>DH_접합구성</c>)을 적는다 — 플랜지면 단부 전부가 플랜지(`2F`), 소켓 계열(용접·KP·압륜)이면
    /// <b>소켓 단부 수</b>만 적는다(`1S`·`2S`). 스피곳(삽입) 단부는 접합 형상이 아니라 세지 않는다(docs/199, 2026-09-21).
    /// <para>docs/178 § B는 단부 수 기준 `2S`로 정했으나 접합 구성의 정의(소켓 수 + 플랜지 수)와
    /// 어긋났다 — B형(한쪽 소켓)은 `1S`다. 밸브실(<see cref="PipeJointFlagReader"/>)이 단부 플래그를 세어 찍는 값과 같아진다.</para>
    /// <para><see cref="JointSlots"/>의 개소(편측 1·양측 2)와는 여전히 별개다 — 그쪽은 산출 개소, 이쪽은 부재의 실제 단부 형상이다.</para></summary>
    /// <param name="endCount">부재의 단부 수. 플랜지일 때 전부 플랜지로 적는다.</param>
    /// <param name="socketEnds">소켓 단부 수. 직관 1(항상), 곡관 A형 2·B형 1, 이경관 2.</param>
    public void JointComposition(Element element, BendConnection connection, int endCount, int socketEnds)
        => Text(element, PipeAlignmentParameters.JointComposition,
            connection == BendConnection.Flanged
                ? _rules.EncodeComposition(0, endCount)
                : _rules.EncodeComposition(socketEnds, 0));

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
