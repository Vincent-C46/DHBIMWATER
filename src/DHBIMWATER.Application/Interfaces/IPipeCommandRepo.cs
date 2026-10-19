using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces;

/// <summary>배관 생성 결과 요약. 경고가 있어도 배치 자체는 성공한 것이므로 예외로 던지지 않는다
/// — 예외를 던지면 UseCase가 트랜잭션을 롤백해 배치가 통째로 사라진다.</summary>
public sealed record PipeCreationResult(string Summary, IReadOnlyList<string> Warnings)
{
    public PipeCreationResult(string summary) : this(summary, []) { }

    /// <summary>모델링할 수 없어 배치하지 않고 건너뛴 곡관·T형·밸브(docs/145 § 4).
    /// 위치 인자가 아니라 init 프로퍼티인 이유: 다른 출력 구현의 <c>new PipeCreationResult(...)</c> 호출부를 깨지 않기 위해서다.
    /// 캔버스 오류 표식이 <see cref="PipeConnectionFailure"/>를 이미 소비하므로 타입을 재사용한다.</summary>
    public IReadOnlyList<PipeConnectionFailure> Skipped { get; init; } = [];

    /// <summary>사용자에게 알림창으로 보일 문구(docs/301 § E). 일반 경고(<see cref="Warnings"/>)는 로그로만 간다.</summary>
    public IReadOnlyList<string> Notices { get; init; } = [];

    /// <summary>캔버스 Guid(엣지·인라인 부속·절점) → 이번에 만든 Revit ElementId 목록(docs/179 § C).
    /// 한 엣지가 정척 분할로 여러 본이 되므로 1:N이다.
    /// <para><see cref="Skipped"/>와 같은 이유로 위치 인자가 아니라 init 프로퍼티다 —
    /// MEP·GenericModel 출력 구현의 <c>new PipeCreationResult(...)</c> 호출부를 깨지 않는다.</para></summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<long>> PlacedElementIds { get; init; }
        = new Dictionary<Guid, IReadOnlyList<long>>();
}

/// <summary>Revit 배관 출력 구현(MEP Pipe / GenericModel / 직관·단관 패밀리)의 공통 경계.</summary>
public interface IPipeCommandRepo
{
    PipeOutputMode OutputMode { get; }
    PipeCreationResult CreateNetwork(PipeNetworkDefinition network);
}
