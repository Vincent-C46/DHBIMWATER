using Autodesk.Revit.DB;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 배치된 부속의 커넥터를 실측해 직경·길이·꺾임각을 읽는다(docs/136 § 3-5).
/// <para>재기록 경로 전용이다. 배치 당시의 UI 입력(관경·정척·격점번호)이 모델에 남아 있지 않으므로
/// 재료표 값을 여기서 복원한다.</para>
/// <para><see cref="RevitPipeSegmentCommandRepo"/>의 <c>GetMepConnectors</c>·<c>ConnectorSpanMm</c>과
/// 같은 규칙이지만 일부러 복제했다 — 그쪽은 배치 경로라 손대면 회귀 위험이 있다.
/// 규칙(물리 배관 도메인의 End 커넥터)이 바뀌면 두 곳을 함께 고쳐야 한다.</para>
/// </summary>
internal static class PipeMaterialConnectorReader
{
    /// <summary>물리 배관 커넥터(End)만 고른다. 곡선·논리 커넥터는 세지 않는다.</summary>
    public static List<Connector> EndConnectors(FamilyInstance instance)
        => instance.MEPModel?.ConnectorManager?.Connectors.Cast<Connector>()
            .Where(x => x.Domain == Domain.DomainPiping && x.ConnectorType == ConnectorType.End)
            .ToList() ?? [];

    /// <summary>커넥터 직경(mm). 원형이 아니면 null — 규격 문자열을 만들 수 없다.</summary>
    public static double? DiameterMm(Connector connector)
    {
        if (connector.Shape != ConnectorProfileType.Round) return null;
        try
        {
            return UC.FtToMm(connector.Radius * 2);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Radius는 원형이 아닌 커넥터에서 예외를 던진다. Shape로 걸러도 남는 경계 케이스를 삼킨다.
            return null;
        }
    }

    /// <summary>두 커넥터 원점 사이 거리(mm). 관 길이·인라인 부속 폭이 이 값이다.</summary>
    public static double SpanMm(Connector a, Connector b)
        => UC.FtToMm(a.Origin.DistanceTo(b.Origin));

    /// <summary>두 커넥터의 바깥 방향(<c>CoordinateSystem.BasisZ</c>) 단위벡터 내적.
    /// 일직선으로 관통하면 -1, 직각으로 꺾이면 0이다.
    /// <see cref="Core.Piping.PipeMaterialSpecText.DeflectionDeg"/>에 그대로 넘긴다.</summary>
    public static double OutwardDot(Connector a, Connector b)
        => a.CoordinateSystem.BasisZ.Normalize().DotProduct(b.CoordinateSystem.BasisZ.Normalize());
}
