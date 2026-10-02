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
/// <para>밸브부관 B의 밸브 쪽 판정(<see cref="ValveBendValveSideFirst"/>)만은 배치·되가져오기가 함께 쓴다(docs/247).</para>
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

    /// <summary>밸브부관 B 레그 길이가 이 값 이하로 다르면 대칭으로 본다(docs/247).</summary>
    private const double ValveBendLegToleranceMm = 1.0;

    /// <summary>밸브부관 B 커넥터를 "밸브 쪽 먼저" 순서로 돌려준다(docs/247, docs/225 § 0-5 번복).
    /// <para>두 커넥터의 바깥 방향 축(평면)을 연장해 만나는 코너에서 각 커넥터까지의 거리 = 레그 길이.
    /// 긴 레그 = 밸브 쪽, 짧은 레그 = 밸브부관 A 쪽이다. 커넥터 Id는 패밀리 작성 순서라 방향 판정에 쓰지 않는다.</para>
    /// <para>레그 차이가 1mm 이내(대칭)이거나, 커넥터가 2개가 아니거나, 두 축이 평행해 코너가 없으면
    /// 최소 Id를 먼저 두는 고정 순서만 준다 — 대칭이면 방향이 의미 없고, 나머지는 호출부가 개수·평행을 따로 검증한다.</para></summary>
    public static List<Connector> ValveBendValveSideFirst(IEnumerable<Connector> connectors)
    {
        var ordered = connectors.OrderBy(x => x.Id).ToList();
        if (ordered.Count != 2) return ordered;

        var p0 = ordered[0].Origin; var d0 = FlatUnit(ordered[0].CoordinateSystem.BasisZ);
        var p1 = ordered[1].Origin; var d1 = FlatUnit(ordered[1].CoordinateSystem.BasisZ);
        if (d0 is null || d1 is null) return ordered;
        // p0 + t·d0 = p1 + s·d1 (평면) — RevitPipeSegmentCommandRepo.MeasureBend와 같은 식이다.
        var cross = d0.X * d1.Y - d0.Y * d1.X;
        if (Math.Abs(cross) < 1e-6) return ordered;
        var delta = p1 - p0;
        var t = (delta.X * d1.Y - delta.Y * d1.X) / cross;
        var cornerX = p0.X + d0.X * t;
        var cornerY = p0.Y + d0.Y * t;
        var leg0 = UC.FtToMm(Math.Sqrt((p0.X - cornerX) * (p0.X - cornerX) + (p0.Y - cornerY) * (p0.Y - cornerY)));
        var leg1 = UC.FtToMm(Math.Sqrt((p1.X - cornerX) * (p1.X - cornerX) + (p1.Y - cornerY) * (p1.Y - cornerY)));

        if (Math.Abs(leg0 - leg1) <= ValveBendLegToleranceMm) return ordered;
        return leg0 > leg1 ? ordered : [ordered[1], ordered[0]];
    }

    /// <summary>평면(XY) 단위벡터. 수직 방향이라 평면 성분이 없으면 null.</summary>
    private static XYZ? FlatUnit(XYZ vector)
    {
        var flat = new XYZ(vector.X, vector.Y, 0);
        return flat.GetLength() < 1e-9 ? null : flat.Normalize();
    }
}
