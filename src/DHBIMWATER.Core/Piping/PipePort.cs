namespace DHBIMWATER.Core.Piping;

/// <summary>포트의 흐름 방향. 화살표 색·방향과 프리셋의 시작/끝 판정에 쓴다(docs/172 § A-1).</summary>
public enum PortFlow { In, Out }

/// <summary>포트가 붙는 외곽 변. 1차 UI는 Left/Right만 노출한다(docs/172 § 0-3 Q2). Top/Bottom은 확장용.</summary>
public enum OutlineSide { Left, Right, Top, Bottom }

/// <summary>IN/OUT 포트 1개. 배치 결과로 Revit 부재가 되지는 않지만, 창을 닫아도 남도록
/// 프로젝트 DataStorage에 저장한다(docs/204 § B — docs/172 § 0-2의 "기록하지 않는다"를 뒤집는다).</summary>
/// <param name="OffsetMm">그 변의 기준 모서리에서의 거리(mm). Left/Right는 상하 이격(기준은 ArrowFromTop),
/// Top/Bottom은 좌우 이격(기준은 좌측).</param>
/// <param name="Label">표시용. "IN"·"IN2"·"OUT"… 색은 Label이 아니라 <see cref="Flow"/>로 정한다.</param>
public sealed record PipePort(Guid Id, PortFlow Flow, OutlineSide Side, double OffsetMm, string Label)
{
    public static PipePort Create(PortFlow flow, OutlineSide side, double offsetMm, string label)
        => new(Guid.NewGuid(), flow, side, offsetMm, label);
}

/// <summary>저장본에 실리는 포트 1개(docs/204 § B-2). <see cref="PipePort"/>에 없는
/// <paramref name="UserEdited"/>(UI의 <c>PipePortItem.UserEdited</c>)를 함께 들고 다닌다 —
/// 이 플래그가 빠지면 복원 후 외곽을 다시 잡을 때 사용자 입력 이격이 자동값으로 덮인다.</summary>
public sealed record StoredPipePort(PipePort Port, bool UserEdited);

/// <summary>기본 포트 구성. 종전 동작(IN 좌 1 + OUT 우 1)과 같다.</summary>
public static class PipePortDefaults
{
    /// <summary>외곽이 없을 때의 이격 기본값. 종전 `_inOffsetMm = 500`과 같은 값.</summary>
    public const double DefaultOffsetMm = 500;

    public static IReadOnlyList<PipePort> Standard(double offsetMm = DefaultOffsetMm) =>
    [
        PipePort.Create(PortFlow.In, SideFor(PortFlow.In), offsetMm, LabelFor(PortFlow.In, 1)),
        PipePort.Create(PortFlow.Out, SideFor(PortFlow.Out), offsetMm, LabelFor(PortFlow.Out, 1)),
    ];

    /// <summary>같은 흐름의 n번째 포트 라벨. 1번은 접미사 없음 — 기존 "IN"/"OUT" 표기 유지.</summary>
    public static string LabelFor(PortFlow flow, int ordinal)
        => (flow == PortFlow.In ? "IN" : "OUT") + (ordinal <= 1 ? string.Empty : ordinal.ToString());

    /// <summary><see cref="LabelFor"/>의 역함수. 접미사가 없거나 숫자가 아니면 1. 목록 정렬(IN, IN2, IN3…, OUT, OUT2…)에 쓴다.</summary>
    public static int OrdinalOf(PortFlow flow, string label)
    {
        var prefix = flow == PortFlow.In ? "IN" : "OUT";
        if (!label.StartsWith(prefix, StringComparison.Ordinal)) return 1;
        return int.TryParse(label.AsSpan(prefix.Length), out var ordinal) && ordinal > 1 ? ordinal : 1;
    }

    /// <summary>기본 포트(IN·OUT, 접미사 없음)인가. 기본 포트는 지울 수 없고 추가된 것부터 지울 수 있다(사용자 확정 2026-09-15).</summary>
    public static bool IsDefault(PortFlow flow, string label) => string.Equals(label, LabelFor(flow, 1), StringComparison.Ordinal);

    /// <summary>흐름별 고정 변(docs/172 Q2). IN=Left, OUT=Right.</summary>
    public static OutlineSide SideFor(PortFlow flow) => flow == PortFlow.In ? OutlineSide.Left : OutlineSide.Right;

    /// <summary>같은 변에 포트가 count개일 때 rank(1-based)번째 포트의 자동 이격(mm). 변을 count+1 등분해 벽 중심 기준으로 대칭 배치한다(docs/174 § A-1).
    /// count=1이면 중앙(sideLengthMm/2) — 종전 "내부 높이의 절반" 규칙과 같다. 반올림 1자리는 VM의 종전 코드와 같다.</summary>
    public static double EvenOffsetMm(double sideLengthMm, int rank, int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "포트 수는 1 이상이어야 합니다.");
        if (rank < 1 || rank > count) throw new ArgumentOutOfRangeException(nameof(rank), rank, "순위는 1..count 범위여야 합니다.");
        return Math.Round(sideLengthMm * rank / (count + 1), 1);
    }
}
