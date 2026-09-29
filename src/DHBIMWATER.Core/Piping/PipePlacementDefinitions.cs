using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>Phase 2 Revit 생성 경계에서 사용할 순수 데이터 정의.</summary>
/// <param name="DiameterMm">이 선 하나의 관경(mm). 선마다 다를 수 있다(docs/152 § A-5).</param>
public sealed record PipeEdgeDefinition(Guid Id, Point2D Start, Point2D End, double Elevation, double DiameterMm);
/// <param name="TypeKey">부속 종류. 이음관은 <see cref="InlineJoint.TypeKey"/>로 구분한다(docs/175 § C) —
/// 밸브의 속성이 아니라 독립 부속이므로 부모·방향 정보는 없다. 자리는 캔버스 점이며, 밸브 근처면 Revit 경계가 밀착시킨다(docs/175 § D-2).</param>
/// <param name="Bypass">바이패스 세트 입력(docs/228 § A). 이때 <paramref name="FamilyTypeName"/>은 본관 밸브다.</param>
public sealed record FittingPlacementDefinition(Guid Id, string TypeKey, string FamilyTypeName, Point2D Position, double Elevation, Guid? EdgeId = null, Guid? NodeId = null,
    BypassSetSpec? Bypass = null)
{
    public bool IsJoint => string.Equals(TypeKey, InlineJoint.TypeKey, StringComparison.Ordinal);
    public bool IsBypassSet => Bypass is not null && string.Equals(TypeKey, BypassSet.TypeKey, StringComparison.Ordinal);
}
