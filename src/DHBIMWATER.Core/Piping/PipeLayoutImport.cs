using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>재구성된 캔버스 엣지 1개. 좌표는 Revit 프로젝트 mm이며, 기준점 빼기는 ViewModel이 한다(docs/158 § A-2).</summary>
public sealed record ImportedEdge(Point2D Start, Point2D End, double DiameterMm)
{
    /// <summary>이 엣지를 이루던 관(직관·단관)의 ElementId. 캔버스 선 → Revit 부재 하이라이트에 쓴다(docs/179 § A-1).
    /// <para>위치 인자가 아니라 init 프로퍼티인 이유: 이 레코드를 위치 인자로 만드는 호출부·테스트를
    /// 깨지 않기 위해서다. 밸브·이음관은 여기 넣지 않는다 — 자기 <see cref="ImportedFitting.ElementId"/>를 갖는다.</para></summary>
    public IReadOnlyList<long> ElementIds { get; init; } = [];
}

/// <summary>재구성된 인라인 부속 1개.</summary>
/// <param name="EdgeIndex"><see cref="PipeLayoutImport.Edges"/>에서의 소속 엣지 인덱스.</param>
/// <param name="T">엣지 시작점 기준 0~1.</param>
/// <param name="IsJoint">이음관이면 true(docs/175 § C-4). 캔버스는 이 값으로 <see cref="InlineJoint.TypeKey"/>를 붙인다 —
/// 밸브와 같은 독립 인라인 부속이며 위치는 자기 커넥터 중점의 T다.</param>
public sealed record ImportedFitting(int EdgeIndex, string FamilyTypeName, double T, bool IsJoint = false)
{
    /// <summary>이 부속 부재의 ElementId. 0이면 모름(docs/179 § A-2).</summary>
    public long ElementId { get; init; }

    /// <summary>바이패스 세트로 묶어 되가져온 부속이면 값이 있다(docs/229 § C). 이때 <see cref="FamilyTypeName"/>은 본관 밸브이고,
    /// 본관 밸브 없이 모델링된 세트면 빈 문자열이다.</summary>
    public ImportedBypass? Bypass { get; init; }
}

/// <summary>되가져온 바이패스 세트의 바이패스 쪽 정보(docs/229 § C).</summary>
/// <param name="BypassValveFamilyTypeName">B–B 사이 바이패스 밸브("패밀리명 : 유형명").</param>
/// <param name="BypassDiameterMm">바이패스 관경(mm). B 커넥터 → A 분기 커넥터 순으로 읽는다.</param>
/// <param name="Normal">본관에서 바이패스 쪽을 향하는 평면 단위벡터. 캔버스가 엣지 방향과 비교해 Side를 정한다.</param>
/// <param name="ElementIds">세트를 이루던 부재 전부(A·B·밸브·채움관). [Revit에서 보기]가 세트를 통째로 고른다.</param>
public sealed record ImportedBypass(string BypassValveFamilyTypeName, double BypassDiameterMm, Point2D Normal, IReadOnlyList<long> ElementIds)
{
    /// <summary>밸브 2개 세트면 두 번째 밸브("패밀리명 : 유형명", docs/255). 1개 세트면 null.
    /// 이 단계에서는 체인 순서일 뿐이다 — V1(엣지 시작 쪽)은 캔버스가 <see cref="ValvePositions"/>로 정한다.</summary>
    public string? SecondValveFamilyTypeName { get; init; }

    /// <summary>두 밸브 사이 관 길이 합(mm). 1개 세트이거나 두 밸브가 직결이면 0.</summary>
    public double SpoolLengthMm { get; init; }

    /// <summary>두 밸브 원점(프로젝트 mm). [0] = <see cref="BypassValveFamilyTypeName"/>, [1] = <see cref="SecondValveFamilyTypeName"/>. 1개 세트면 빈 목록.</summary>
    public IReadOnlyList<Point2D> ValvePositions { get; init; } = [];
}

/// <summary>재구성된 절점 부속(곡관·T형·Y형·십자) 1개(docs/216 § A-1).
/// ViewModel이 <paramref name="Position"/>에 가장 가까운 캔버스 절점 Guid에 <paramref name="ElementId"/>를 건다 —
/// 모델 생성 경로(PlaceNodeFittings)와 같은 키라서 [Revit에서 보기]의 절점 부속 선택이 가져온 배관에서도 동작한다.</summary>
/// <param name="Position">절점 좌표. <see cref="ImportedEdge"/>와 같은 Revit 프로젝트 mm.</param>
/// <param name="FittingKind">밸브부관 A·B면 그 종류(docs/225 § E, docs/227 § E). 캔버스가 그 절점의 부속 종류를 바꾼다.</param>
/// <param name="ValveSideDirection">밸브부관 B의 밸브 쪽 커넥터(긴 레그, docs/247)의 평면 방향. 캔버스 밸브 쪽 지정으로 되살린다.</param>
public sealed record ImportedNodeFitting(Point2D Position, long ElementId,
    PipeNodeFittingKind FittingKind = PipeNodeFittingKind.Bend, Point2D? ValveSideDirection = null);

/// <summary>역가져오기 결과. ViewModel이 이대로 캔버스에 옮긴다(docs/158 § A-2).</summary>
/// <param name="SourceElementIds">피킹한 부재 전부의 ElementId. 재생성 시 삭제 대상이 된다(docs/163 § A).
/// 캔버스에 오르지 못한 부재도 포함한다 — 남겨 두면 재생성한 배관과 겹치기 때문이다. 대신 <see cref="Warnings"/>로 알린다.</param>
public sealed record PipeLayoutImport(
    IReadOnlyList<ImportedEdge> Edges,
    IReadOnlyList<ImportedFitting> Fittings,
    double ElevationMm,
    IReadOnlyList<long> SourceElementIds,
    IReadOnlyList<string> Warnings,
    double? DominantDiameterMm = null,
    string? SegmentFamilyTypeName = null,
    string? BendFamilyTypeName = null,
    string? TeeFamilyTypeName = null,
    /// <summary>가져온 부재의 <c>DH_Zone</c>(밸브실 유형). 값이 섞여 있으면 빈 문자열이다(docs/161 § G-3).</summary>
    string Zone = "",
    /// <summary>가져온 부재의 <c>DH_관종</c>. 값이 섞여 있으면 빈 문자열이다(docs/170 § G).
    /// <para>이 필드가 없던 동안 역가져오기 → 재생성은 창 기본값(덕타일주철관)으로 관종을 덮어썼다.
    /// 강관 밸브실이 <c>SP</c>에서 <c>DCIP</c>로 바뀌어 수량산출 관종 필터가 통째로 틀어지는 버그였다.</para></summary>
    string Material = "",
    /// <summary>가져온 십자 부재의 최빈 패밀리(docs/175 § B-4). 없으면 null.
    /// <para>맨 끝에 두는 이유: 테스트·호출부가 이 레코드를 위치 인자로 만든다. 중간에 끼우면 Zone·Material이 밀린다.</para></summary>
    string? CrossFamilyTypeName = null,
    /// <summary>가져온 Y형 부재의 최빈 패밀리(docs/187 § 5-1). 없으면 null.
    /// <para>이것이 없으면 Y형 부재가 T형 최빈값 투표에 섞여 T형 칸을 오염시킨다.</para></summary>
    string? WyeFamilyTypeName = null,
    /// <summary>캔버스에 올라간 절점 부속의 위치·ElementId(docs/216 § A-1). 없으면 null.
    /// <para>맨 끝에 두는 이유는 <see cref="CrossFamilyTypeName"/>과 같다.</para></summary>
    IReadOnlyList<ImportedNodeFitting>? NodeFittings = null,
    /// <summary>가져온 밸브부관 B 부재의 최빈 패밀리(docs/225 § E). 없으면 null. 맨 끝에 두는 이유는 위와 같다.</summary>
    string? ValveBendFamilyTypeName = null,
    /// <summary>가져온 밸브부관 A 부재의 최빈 패밀리(docs/227 § E). 없으면 null.</summary>
    string? ValveTeeFamilyTypeName = null)
{
    public static PipeLayoutImport Empty { get; } = new([], [], 0, [], []);

    public bool IsEmpty => Edges.Count == 0;
}
