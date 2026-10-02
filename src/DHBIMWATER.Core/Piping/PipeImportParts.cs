using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>역가져오기에서 판정한 부재 종류(docs/158 § A-1).
/// <para>판정 규칙은 <c>RevitPipeMaterialBackfillRepo.Describe</c>(docs/136)와 같아야 한다 —
/// 배관 밸브류는 밸브, 이음쇠는 커넥터 수와 편각으로 관·곡관·T형을 가른다.</para></summary>
public enum PipePartRole
{
    /// <summary>직관·단관. 커넥터 2개가 일직선(편각 ≤ 1°)인 이음쇠.</summary>
    Segment,
    /// <summary>곡관. 커넥터 2개가 1°를 넘게 꺾인 이음쇠. 캔버스에서는 절점이 된다.</summary>
    Bend,
    /// <summary>T형. 커넥터 3개인 이음쇠. 캔버스에서는 절점이 된다.</summary>
    Tee,
    /// <summary>십자. 커넥터 4개인 이음쇠. 캔버스에서는 절점이 된다(docs/175 § B-4).</summary>
    Cross,
    /// <summary>배관 밸브류. 캔버스에서는 엣지 위의 인라인 부속이 된다.</summary>
    Accessory,
    /// <summary>이음관. 패밀리명에 `이음관`이 든 2포트 이음쇠다(docs/167 § F-1).
    /// <para>편각만 보면 직관과 구분되지 않으므로 <see cref="Segment"/>로 분류돼 관 패밀리 최빈값 투표까지
    /// 오염시킨다. 캔버스에서는 밸브와 같은 독립 인라인 부속이 된다(docs/175 § C).</para></summary>
    Joint,
    /// <summary>종류를 판정하지 못했다. 그래프에 넣지 않고 경고로만 남긴다.</summary>
    Unknown,
    /// <summary>Y형. 커넥터 3개인데 분기가 직교가 아니라 30~60°로 붙은 이음쇠다(docs/187 § 5-1).
    /// <para>맨 뒤에 두어 기존 값의 순서를 보존한다. 캔버스에서는 T형과 같이 절점이 된다.</para></summary>
    Wye,
    /// <summary>밸브부관 B. 패밀리명에 `밸브부관`이 든 2포트·꺾인 배관 부속류다(docs/225 § E).
    /// <para>키워드로 먼저 떼지 않으면 커넥터 수·편각 판정에서 곡관으로 잡힌다.
    /// 캔버스에서는 곡관처럼 절점이 되고, 그 절점을 밸브부관으로 표시한다.
    /// 저장소가 밸브 쪽(긴 레그)을 먼저 넘기므로 <c>Connectors[0]</c>이 밸브 쪽이다(docs/247).</para></summary>
    ValveBend,
    /// <summary>밸브부관 A. 패밀리명에 `밸브부관`이 든 4포트 배관 부속류다(docs/227 § E).
    /// <para>수직분기 커넥터는 픽 단계에서 빼고 넘어오므로 커넥터 3개(주관 2·수평분기 1)로 T형과 같이 절점이 된다.</para></summary>
    ValveTee
}

/// <summary>실측을 끝낸 커넥터 하나. Revit 타입은 Core에 들어올 수 없으므로 값만 넘어온다(docs/158 § A-1).</summary>
/// <param name="Point">커넥터 원점의 평면 좌표(프로젝트 mm).</param>
/// <param name="ZMm">커넥터 원점의 높이(프로젝트 mm). 표고 판정·경고에만 쓴다.</param>
/// <param name="Outward">커넥터가 향하는 바깥 방향 단위벡터의 <b>평면 성분</b>. 길이가 0이면 수직 커넥터다.</param>
/// <param name="DiameterMm">원형이 아니면 null.</param>
/// <param name="ConnectedElementIds">Revit이 물리적으로 연결해 둔 상대 요소 Id.</param>
public sealed record PickedConnector(
    Point2D Point,
    double ZMm,
    Point2D Outward,
    double? DiameterMm,
    IReadOnlyList<long> ConnectedElementIds);

/// <summary>피킹으로 읽어 온 배관 부재 하나. 좌표는 전부 Revit 프로젝트 mm다.</summary>
/// <param name="SkipReason">판정에 실패한 사유. 값이 있으면 그래프에 넣지 않는다.</param>
public sealed record PickedPipePart(
    long ElementId,
    PipePartRole Role,
    string FamilyTypeName,
    Point2D Origin,
    double OriginZMm,
    IReadOnlyList<PickedConnector> Connectors,
    string? SkipReason = null,
    /// <summary>이 부재에 기록돼 있던 <c>DH_Zone</c>(밸브실 유형). 없으면 빈 문자열이다(docs/161 § G).</summary>
    string Zone = "",
    /// <summary>이 부재에 기록돼 있던 <c>DH_관종</c> 값(`SP`·`DCIP` 등). 없으면 빈 문자열이다(docs/170 § G).</summary>
    string Material = "");
