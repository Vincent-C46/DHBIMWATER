using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Core.Piping;

/// <summary>접합 슬롯 한 벌(종류 · 규격 · 개수). 규격이 빈 문자열이면 수량산출이 요소 관경을 쓴다(docs/186 § 4-3).</summary>
public sealed record PipeJointSlotValue(string Kind, string Spec, int Count);

/// <summary>밸브실 부재 한 개의 개수 계산 입력. Revit 타입·전용 열거형 없이 모델에서 읽은 값만 담는다.</summary>
/// <param name="PartName">`DH_Part` 또는 부재명(`2F 단관`). 접합 표기는 있어도 된다.</param>
/// <param name="Composition">`DH_접합구성`(`1S1F`). 비어 있으면 <paramref name="IsFlanged"/>로 부재 전체를 한 형식으로 본다.</param>
/// <param name="MaterialCode">`DH_관종` 값(`SP`·`DCIP` 또는 한글 라벨). 비어 있으면 강관으로 간주한다(경고 대상).</param>
/// <param name="IsValve">배관 밸브류 카테고리인지(밸브부관은 아니다, R-7).</param>
/// <param name="IsFlanged">부재 접합 방식이 플랜지인지(구성이 비었을 때만 쓴다).</param>
/// <param name="UseRestraintGland">주철관 소켓이 이탈방지압륜인지.</param>
public sealed record PipeMemberCountInput(string? PartName, string? Composition, string? MaterialCode, bool IsValve,
    bool IsFlanged, bool UseRestraintGland = false);

/// <summary>밸브실 부재 한 개의 개수. null인 칸은 쓰지 않는다.</summary>
/// <param name="ValveJointLayingCount">밸브류 `DH_접합및부설수`. 밸브만 1이다(R-8).</param>
/// <param name="SocketSlots">소켓(용접·KP·압륜) 슬롯. 플랜지는 <see cref="PipeJointConnectionPass"/>가 연결로 정한다.</param>
/// <param name="MaterialAssumedSteel">관종이 비어 강관으로 간주했는지(경고 대상).</param>
public sealed record PipeMemberCounts(int? ValveJointLayingCount, int? LayingCount, int? CutCount,
    IReadOnlyList<PipeJointSlotValue> SocketSlots, bool MaterialAssumedSteel);

/// <summary>계획관로 직관 계열 한 토막의 부재 종류와 개수(R-1·R-6).</summary>
public sealed record PlannedSegmentCounts(string PartName, int LayingCount, int CutCount);

/// <summary>계획관로 부재 한 개의 개수(직접 배치 자동입력, docs/267 § 2-2).</summary>
/// <param name="MaterialUnknown">관종을 몰라 소켓 슬롯 종류를 정하지 못했는지(슬롯은 비운다 — 지어내지 않는다).</param>
public sealed record PlannedMemberCounts(int LayingCount, int CutCount, IReadOnlyList<PipeJointSlotValue> SocketSlots, bool MaterialUnknown);

/// <summary>
/// 배관 부재의 부설 · 절단 · 소켓 접합 개수 규칙(docs/266 § 2, docs/267 § 2-2). 신규 배치 · 재기록 · 직접 배치 자동입력이
/// 같은 값을 내도록 규칙을 <b>이 클래스 한곳</b>에만 둔다 — 기록기(<c>PipeJointCountWriter</c>)와 Repo는 결과를 쓰기만 한다.
/// 연결에 의존하는 값(플랜지 · 유입부 절단)은 <see cref="PipeJointConnectionPass"/>가 맡는다.
/// </summary>
public static class PipeCountRules
{
    /// <summary>플랜지 슬롯 종류 문자열. 수량산출 공종 키와 같아야 한다(<see cref="PipeAlignmentParameters.JointKindFor"/>).</summary>
    public static string FlangeJointKind => PipeAlignmentParameters.JointKindFor(null, BendConnection.Flanged)!;

    /// <summary>밸브실 부재의 개수(R-2·R-3·R-4·R-6·R-8).
    /// <para>밸브류: `DH_접합및부설수 = 1`만 쓴다 — 접합·부설이 밸브 품에 들어 있다.</para>
    /// <para>그 밖: 부설 = 직관 0 · 나머지 1, 절단 = 평단 단관 1 · 나머지 0(유입부 몫은 연결 패스가 더한다),
    /// 소켓 슬롯 = 소켓 단부 수(관종 + 압륜으로 종류를 정한다).</para></summary>
    public static PipeMemberCounts ForValveRoomMember(PipeMemberCountInput input)
    {
        if (input.IsValve) return new PipeMemberCounts(1, null, null, [], false);

        var ends = PipeJointComposition.Decode(input.Composition);
        if (ends.IsEmpty) ends = input.IsFlanged ? new PipeJointEnds(0, 1) : new PipeJointEnds(1, 0);

        var slots = new List<PipeJointSlotValue>();
        var assumedSteel = false;
        if (ends.Socket > 0)
        {
            var material = PipeAlignmentParameters.MaterialFromModelValue(input.MaterialCode);
            if (material is null)
            {
                material = PipeMaterial.Steel;
                assumedSteel = true;
            }
            if (PipeAlignmentParameters.JointKindFor(material, BendConnection.Socket, input.UseRestraintGland) is { } kind)
                slots.Add(new PipeJointSlotValue(kind, string.Empty, ends.Socket));
        }
        return new PipeMemberCounts(null, LayingCount(input.PartName), BaseCutCount(input.PartName, input.Composition), slots, assumedSteel);
    }

    /// <summary>부설 수 — 직관 0, 그 밖(단관·곡관·이형관·밸브부관·신축관) 1(R-2·R-3).</summary>
    public static int LayingCount(string? partName)
        => PipeMaterialSpecText.StripJointMark(partName) == PipeMaterialSpecText.StraightName ? 0 : 1;

    /// <summary>부재 자체 절단 수(밸브실) — 평단 단관(`DH_접합구성`이 비었거나 소켓·플랜지가 0) 1, 나머지 0(R-6).</summary>
    public static int BaseCutCount(string? partName, string? composition)
        => IsShortPipe(partName) && PipeJointComposition.Decode(composition).IsEmpty ? 1 : 0;

    /// <summary>관(직관·단관)인지. 플랜지 연결 귀속에서 이형관보다 앞선다(docs/266 § 1-3).</summary>
    public static bool IsPipe(string? partName)
        => PipeMaterialSpecText.StripJointMark(partName) is PipeMaterialSpecText.StraightName or PipeMaterialSpecText.ShortName;

    /// <summary>계획관로 직관 계열 — 6 m 정척 − 1 mm 이상이면 `직관`(부설 0 · 절단 0), 미만이면 `단관`(부설 1 · 절단 1)(R-1·R-6).</summary>
    public static PlannedSegmentCounts ForPlannedSegment(double lengthMm)
        => PipeMaterialSpecText.SegmentName(lengthMm) == PipeMaterialSpecText.ShortName
            ? new PlannedSegmentCounts(PipeMaterialSpecText.ShortName, 1, 1)
            : new PlannedSegmentCounts(PipeMaterialSpecText.StraightName, 0, 0);

    /// <summary>계획관로 곡관 절단 — 0(R-6). 0과 미기록을 구분하려고 명시해서 쓴다.</summary>
    public const int PlannedBendCutCount = 0;

    /// <summary>계획관로 곡관 부설 — 1(R-3).</summary>
    public const int PlannedBendLayingCount = 1;

    /// <summary>계획관로 이경관 절단 — 1(R-6, 현행 유지).</summary>
    public const int PlannedReducerCutCount = 1;

    /// <summary>계획관로 이경관 부설 — 1(R-3).</summary>
    public const int PlannedReducerLayingCount = 1;

    /// <summary>계획관로 부재(직관 · 단관 · 곡관)의 개수 — 자동 배치와 같은 값(docs/267 § 2-2).
    /// <para>직관: 부설 0 · 절단 0 / 단관: 부설 1 · 절단 1 / 곡관: 부설 1 · 절단 0. 소켓 슬롯 = 구성의 소켓 단부 수(직관 `1S` = 1).
    /// 플랜지는 연결 패스가 센다. 관종을 모르면 소켓 슬롯을 비우고 <see cref="PlannedMemberCounts.MaterialUnknown"/>을 세운다.</para></summary>
    public static PlannedMemberCounts ForPlannedMember(string partName, string? composition, string? materialCode, bool useRestraintGland = false)
    {
        var name = PipeMaterialSpecText.StripJointMark(partName);
        var (laying, cut) = name switch
        {
            PipeMaterialSpecText.StraightName => (0, 0),
            PipeMaterialSpecText.ShortName => (1, 1),
            _ => (PlannedBendLayingCount, PlannedBendCutCount),
        };
        var socket = PipeJointComposition.Decode(composition).Socket;
        if (socket <= 0) return new PlannedMemberCounts(laying, cut, [], false);
        var material = PipeAlignmentParameters.MaterialFromModelValue(materialCode);
        var kind = material is null ? null : PipeAlignmentParameters.JointKindFor(material, BendConnection.Socket, useRestraintGland);
        return kind is null
            ? new PlannedMemberCounts(laying, cut, [], true)
            : new PlannedMemberCounts(laying, cut, [new PipeJointSlotValue(kind, string.Empty, socket)], false);
    }

    /// <summary>패밀리 단부 플래그로 구성을 못 정했을 때의 계획관로 기본 구성 — 직관·단관은 소켓 한쪽 `1S`(docs/199 § 2),
    /// 곡관도 기본 형식(B형 · 편측)과 같은 `1S`다(<see cref="BendSettings.Default"/>).</summary>
    public static string DefaultPlannedComposition(string partName) => PipeJointComposition.Encode(1, 0);

    /// <summary>직접 배치한 계획관로 가변 패밀리의 부재 종류(docs/267 § 2-2). 배치점 2개 = 직관/단관(길이로), 5개 = 곡관, 그 밖은 null(대상 아님).</summary>
    public static string? AdaptivePartFor(int placementPointCount, double lengthMm) => placementPointCount switch
    {
        2 => PipeMaterialSpecText.SegmentName(lengthMm),
        5 => PipeMaterialSpecText.BendName,
        _ => null,
    };

    private static bool IsShortPipe(string? partName) => PipeMaterialSpecText.StripJointMark(partName) == PipeMaterialSpecText.ShortName;
}
