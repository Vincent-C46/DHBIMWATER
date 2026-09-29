using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>되가져오기 — 밸브부관 A 2 · B 2 · 밸브 묶음을 바이패스 세트 1개로 접는다(docs/229 § C).
/// 형상: 본관 X축, A 중심 (0,0)·(2000,0), 바이패스는 +Y 쪽 y=700.</summary>
public sealed class BypassSetReconstructionTests
{
    private const string Butterfly = "DH_버터플라이 밸브 : 1650A";
    private const string Gate = "DH_게이트 밸브 : 300A";

    [Fact]
    public void SetWithMainValveAndMainPipes_BecomesOneEdge_SetPlusOrdinaryMainValve()
    {
        var import = PipeNetworkReconstructor.Build([.. SetParts(withMainValve: true), .. MainPipes()]);

        var edge = Assert.Single(import.Edges);
        Assert.Equal(-2000, Math.Min(edge.Start.X, edge.End.X), 3);
        Assert.Equal(4000, Math.Max(edge.Start.X, edge.End.X), 3);
        // A–A 사이 밸브는 세트가 아니라 일반 인라인 밸브로 돌아온다(docs/230 § D).
        var mainValve = Assert.Single(import.Fittings, x => x.Bypass is null);
        Assert.Equal(Butterfly, mainValve.FamilyTypeName);
        Assert.Equal(32, mainValve.ElementId);
        var fitting = Assert.Single(import.Fittings, x => x.Bypass is not null);
        Assert.Equal("", fitting.FamilyTypeName);
        Assert.Equal(Gate, fitting.Bypass!.BypassValveFamilyTypeName);
        Assert.Equal(300, fitting.Bypass.BypassDiameterMm, 3);
        Assert.Equal(0, fitting.Bypass.Normal.X, 6);
        Assert.Equal(1, fitting.Bypass.Normal.Y, 6);
        // 세트 중심 (1000,0)
        var centerX = edge.Start.X + (edge.End.X - edge.Start.X) * fitting.T;
        Assert.Equal(1000, centerX, 3);
        // 세트 = A 2 · B 2 · 바이패스 관 2 + 밸브 = 7. 본관 관(31·33)·밸브(32)는 세트가 아니다.
        Assert.Equal(7, fitting.Bypass!.ElementIds.Count);
        Assert.DoesNotContain(32L, fitting.Bypass.ElementIds);
        Assert.Contains(31L, edge.ElementIds);
        Assert.DoesNotContain(edge.ElementIds, x => x <= 0);   // 합성 관(A 자리)은 하이라이트 대상이 아니다
        Assert.Empty(import.NodeFittings ?? []);
        Assert.Equal("DH_밸브부관A : 1650A", import.ValveTeeFamilyTypeName);
        Assert.Equal("DH_밸브부관B : 300A", import.ValveBendFamilyTypeName);
    }

    [Fact]
    public void SetWithoutMainValve_AndWithoutMainPipes_StillBecomesSet()
    {
        // 사용자 사례 — 본관 밸브를 모델링하지 않았고 본관 관도 고르지 않았다.
        var import = PipeNetworkReconstructor.Build(SetParts(withMainValve: false).ToList());

        var edge = Assert.Single(import.Edges);
        Assert.Equal(-200, Math.Min(edge.Start.X, edge.End.X), 3);
        Assert.Equal(2200, Math.Max(edge.Start.X, edge.End.X), 3);
        var fitting = Assert.Single(import.Fittings);
        Assert.Equal("", fitting.FamilyTypeName);
        Assert.NotNull(fitting.Bypass);
        Assert.Equal(1000, edge.Start.X + (edge.End.X - edge.Start.X) * fitting.T, 3);
        Assert.Equal(1650, edge.DiameterMm, 3);
        Assert.Empty(edge.ElementIds);   // A 자리·빈 틈 모두 합성 관
        Assert.DoesNotContain(import.Warnings, x => x.Contains("캔버스에 올리지 못했습니다"));
    }

    [Fact]
    public void BypassWithTwoValves_IsNotCollapsed()
    {
        var parts = SetParts(withMainValve: true).ToList();
        // 바이패스 관 하나를 밸브로 바꿔 밸브가 2개가 되게 한다.
        var index = parts.FindIndex(x => x.ElementId == 21);
        parts[index] = parts[index] with { Role = PipePartRole.Accessory, FamilyTypeName = Gate };

        var import = PipeNetworkReconstructor.Build(parts);

        Assert.DoesNotContain(import.Fittings, x => x.Bypass is not null);
    }

    private static IEnumerable<PickedPipePart> SetParts(bool withMainValve)
    {
        yield return Tee(1, P(0, 0));
        yield return Tee(2, P(2000, 0));
        // B: [0] = 밸브 쪽(중심을 향함), [1] = A 쪽
        yield return Bend(3, P(200, 700), P(1, 0), P(0, 300), P(0, -1));
        yield return Bend(4, P(1800, 700), P(-1, 0), P(2000, 300), P(0, -1));
        yield return Segment(21, P(200, 700), P(900, 700), 300);
        yield return Valve(22, P(900, 700), P(1100, 700), Gate, 300);
        yield return Segment(23, P(1100, 700), P(1800, 700), 300);
        if (!withMainValve) yield break;
        yield return Segment(31, P(200, 0), P(900, 0), 1650);
        yield return Valve(32, P(900, 0), P(1100, 0), Butterfly, 1650);
        yield return Segment(33, P(1100, 0), P(1800, 0), 1650);
    }

    private static IEnumerable<PickedPipePart> MainPipes()
    {
        yield return Segment(41, P(-2000, 0), P(-200, 0), 1650);
        yield return Segment(42, P(2200, 0), P(4000, 0), 1650);
    }

    /// <summary>밸브부관 A — 주관 ±X(반폭 200), 수평분기 +Y(300). 수직 커넥터는 픽 단계에서 빠진 상태다.</summary>
    private static PickedPipePart Tee(long id, Point2D center)
        => new(id, PipePartRole.ValveTee, "DH_밸브부관A : 1650A", center, 0,
        [
            new PickedConnector(P(center.X - 200, 0), 0, P(-1, 0), 1650, []),
            new PickedConnector(P(center.X + 200, 0), 0, P(1, 0), 1650, []),
            new PickedConnector(P(center.X, 300), 0, P(0, 1), 300, [])
        ]);

    private static PickedPipePart Bend(long id, Point2D valveSide, Point2D valveOut, Point2D teeSide, Point2D teeOut)
        => new(id, PipePartRole.ValveBend, "DH_밸브부관B : 300A", valveSide, 0,
        [
            new PickedConnector(valveSide, 0, valveOut, 300, []),
            new PickedConnector(teeSide, 0, teeOut, 300, [])
        ]);

    private static PickedPipePart Segment(long id, Point2D start, Point2D end, double diameterMm)
        => TwoPort(id, PipePartRole.Segment, "직관 : DN", start, end, diameterMm);

    private static PickedPipePart Valve(long id, Point2D start, Point2D end, string family, double diameterMm)
        => TwoPort(id, PipePartRole.Accessory, family, start, end, diameterMm);

    private static PickedPipePart TwoPort(long id, PipePartRole role, string family, Point2D start, Point2D end, double diameterMm)
    {
        var length = start.DistanceTo(end);
        var ux = (end.X - start.X) / length;
        var uy = (end.Y - start.Y) / length;
        return new PickedPipePart(id, role, family, new((start.X + end.X) / 2, (start.Y + end.Y) / 2), 0,
        [
            new PickedConnector(start, 0, P(-ux, -uy), diameterMm, []),
            new PickedConnector(end, 0, P(ux, uy), diameterMm, [])
        ]);
    }

    private static Point2D P(double x, double y) => new(x, y);
}
