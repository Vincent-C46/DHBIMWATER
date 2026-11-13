using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Application.Services;
using DHBIMWATER.Core.Geometry;
using Xunit;

namespace DHBIMWATER.UI.Tests.Services;

/// <summary>
/// 토출조 평면·표고 배치 검증.
/// 엑셀 예시(좌우측 TA=9.4/H7=1000, 중앙부 TA=9.7/H7=2000, D=1500, N=4, B8+T5=5000)를 그대로 쓴다.
/// </summary>
public class DischargeBasinLayoutCalculatorTests
{
    private const int Precision = 6;

    private static readonly Dictionary<string, double> ExcelUnselectedConduit = new() { ["OT"] = 0, ["O1"] = 0 };

    private static DischargeBasinInputDto Input(
        DischargeOutletPosition position,
        DischargeStepType step = DischargeStepType.None,
        OutletConduitType conduit = OutletConduitType.Culvert)
        => new(
            Position: position, Step: step, ConduitType: conduit,
            TA: position != DischargeOutletPosition.Center ? 9.4 : 9.7,
            Separation: 3, ON: 2, OB: 2000, OH: 2000, OD: 2000,
            D: 1500, N: 4, PumpHwl: 5, H3: 1200,
            H7: position != DischargeOutletPosition.Center ? 1000 : 2000,
            B8PlusT5: 5000);

    private static (DischargeBasinInputDto Input, DischargeBasinGeometryDto Geometry) Case(
        DischargeOutletPosition position,
        DischargeStepType step = DischargeStepType.None)
    {
        var input = Input(position, step);
        var overrides = position == DischargeOutletPosition.Center ? ExcelUnselectedConduit : null;
        return (input, DischargeBasinGeometryCalculator.Calculate(input, overrides));
    }

    // ── 내공 외곽선 ────────────────────────────────────────────────

    [Fact]
    public void LeftRight_InnerOutline_IsTrapezoidWideningTowardOutlet()
    {
        var (input, g) = Case(DischargeOutletPosition.Right);

        var outline = DischargeBasinLayoutCalculator.CalculateInnerOutline(input, g);

        Assert.Equal(4, outline.Count);
        Assert.Equal(new Point2D(0, 0), outline[0]);
        Assert.Equal(new Point2D(19500, 0), outline[1]);      // TLT
        Assert.Equal(new Point2D(19500, 8000), outline[2]);   // TB2
        Assert.Equal(new Point2D(0, 3800), outline[3]);       // TB1
    }

    [Fact]
    public void Center_InnerOutline_IsSymmetricAndSpansConduit()
    {
        var (input, g) = Case(DischargeOutletPosition.Center);

        var outline = DischargeBasinLayoutCalculator.CalculateInnerOutline(input, g);

        // 평면 도해 하단 치수열 TL3 | TL4 | 관로 | TL4 | TL3 — 단부 TB1 코너에서 곧바로 경사가 시작된다.
        Assert.Equal(6, outline.Count);
        Assert.Equal(new Point2D(19500, 3800), outline[2]);
        Assert.Equal(19500 - 6750, outline[3].X, Precision);  // TLT − TL3
        Assert.Equal(6300, outline[3].Y, Precision);          // TB2
        Assert.Equal(6750, outline[4].X, Precision);          // TL3
        Assert.Equal(6300, outline[4].Y, Precision);
        Assert.Equal(new Point2D(0, 3800), outline[5]);

        // 가운데 평행 구간 = TL4 + 토출관로 폭(ON×OH, OT=0 재현 조건) + TL4
        Assert.Equal(1000 + 2 * 2000 + 1000, outline[3].X - outline[4].X, Precision);
    }

    /// <summary>
    /// 엑셀 TT4·TB4 의 θ1 = (90°+θ)/2 는 단부 (0, TB1) 코너 내각의 절반이다 —
    /// 경사가 단부 코너에서 곧바로 시작해야 성립한다. 경사→평행 절곡점 내각은 180° − θ.
    /// </summary>
    [Fact]
    public void Center_CornerAngles_MatchExcelTheta1()
    {
        var (input, g) = Case(DischargeOutletPosition.Center);
        var outline = DischargeBasinLayoutCalculator.CalculateInnerOutline(input, g);

        var atTb1 = DischargeBasinLayoutCalculator.InteriorAngle(outline, 5) / 2 * 180 / Math.PI;
        Assert.Equal(g.Theta1, atTb1, 6);
        Assert.Equal(g.TT4 / 2, g.TT1 / 2 / Math.Tan(atTb1 * Math.PI / 180), 6);

        var atBend = DischargeBasinLayoutCalculator.InteriorAngle(outline, 4) * 180 / Math.PI;
        Assert.Equal(180 - g.Theta, atBend, 6);
        // 펌프측 직각 코너는 그대로 90°.
        Assert.Equal(Math.PI / 2, DischargeBasinLayoutCalculator.InteriorAngle(outline, 0), Precision);
    }

    /// <summary>
    /// 엑셀 TL5·TL7 은 벽 외면 / 바닥슬래브 외면에서 경사→평행 절곡점까지의 길이다.
    /// 내공 외곽선을 TT1 · TT1+TB3 만큼 바깥으로 민 절곡점이 그 값과 맞아야 한다.
    /// </summary>
    [Fact]
    public void Center_OffsetBendPoints_MatchExcelTL5AndTL7()
    {
        var (input, g) = Case(DischargeOutletPosition.Center);
        var inner = DischargeBasinLayoutCalculator.CalculateInnerOutline(input, g);

        var wallOuter = DischargeBasinLayoutCalculator.OffsetOutward(inner, g.TT1);
        Assert.Equal(g.TL5!.Value, wallOuter[4].X + g.TT1, 6);           // 벽 외면(x = −TT1) 기준

        var slabOuter = DischargeBasinLayoutCalculator.OffsetOutward(inner, g.TT1 + g.TB3);
        Assert.Equal(g.TL7!.Value, slabOuter[4].X + g.TT1 + g.TB3, 6);   // 바닥슬래브 외면 기준
    }

    // ── 엑셀의 θ1·θ2 와 맞물리는지 ─────────────────────────────────

    /// <summary>
    /// 엑셀이 따로 뽑아 둔 TT4·TT5 는 사실 코너 마이터 보정량(×2)이다.
    /// (0,TB1) 코너 내각 = 2θ1, (TLT,TB2) 코너 내각 = 2θ2 인지 확인해 형상 해석이 맞는지 고정한다.
    /// </summary>
    [Fact]
    public void LeftRight_CornerAngles_MatchExcelTheta1AndTheta2()
    {
        var (input, g) = Case(DischargeOutletPosition.Right);
        var outline = DischargeBasinLayoutCalculator.CalculateInnerOutline(input, g);

        var atTb1 = DischargeBasinLayoutCalculator.InteriorAngle(outline, 3) / 2 * 180 / Math.PI;
        var atTb2 = DischargeBasinLayoutCalculator.InteriorAngle(outline, 2) / 2 * 180 / Math.PI;

        Assert.Equal(g.Theta1, atTb1, 6);
        Assert.Equal(g.Theta2!.Value, atTb2, 6);

        // 따라서 마이터 보정량은 TT4/2, TT5/2 가 된다.
        Assert.Equal(g.TT4 / 2, g.TT1 / 2 / Math.Tan(atTb1 * Math.PI / 180), 6);
        Assert.Equal(g.TT5!.Value / 2, g.TT1 / 2 / Math.Tan(atTb2 * Math.PI / 180), 6);
    }

    // ── 레벨 ──────────────────────────────────────────────────────

    [Fact]
    public void Levels_UseBasinFloorAndRoofElevations()
    {
        var (_, g) = Case(DischargeOutletPosition.Right);

        var levels = DischargeBasinLayoutCalculator.CalculateLevels(g);

        Assert.Equal(2, levels.Count);
        Assert.Equal(3200, levels[0].Elevation, Precision);    // TF = 3.2
        Assert.Equal(10800, levels[1].Elevation, Precision);   // TB = 10.8
        // 펌프장 레벨("상부슬래브" 등)과 이름이 겹치면 서로 덮어쓴다.
        Assert.All(levels, l => Assert.StartsWith("토출조_", l.Name));
    }

    // ── 슬래브 ────────────────────────────────────────────────────

    [Fact]
    public void Slabs_BaseIsThickerAndProjectsBeyondWall()
    {
        var (input, g) = Case(DischargeOutletPosition.Right);

        var slabs = DischargeBasinLayoutCalculator.CalculateSlabs(input, g);

        var baseSlab = slabs.Single(s => s.Part == "바닥슬래브");
        var topSlab = slabs.Single(s => s.Part == "상부슬래브");

        Assert.Equal(g.TT2, baseSlab.Thickness, Precision);
        Assert.Equal(3200, baseSlab.ElevationZ, Precision);
        Assert.Equal(g.TT3, topSlab.Thickness, Precision);
        Assert.Equal(10800, topSlab.ElevationZ, Precision);

        // 배치 변환 뒤 펌프측 벽은 −X 를 향하므로 슬래브의 최소 X 가 그 바깥면이다.
        // 바닥슬래브는 벽체 외면에서 TB3 만큼 더 나간다.
        var ox = DischargeBasinPlacement.For(input, g).OriginX;
        Assert.Equal(ox - (g.TT1 + g.TB3), baseSlab.Points.Min(p => p.X), Precision);
        Assert.Equal(ox - g.TT1, topSlab.Points.Min(p => p.X), Precision);
        // Area 는 부호 있는 면적이라, 이 비교가 성립하려면 변환 뒤에도 반시계여야 한다.
        Assert.True(baseSlab.Area > topSlab.Area);
    }

    // ── 벽체 ──────────────────────────────────────────────────────

    [Fact]
    public void Walls_OnePerEdgeWithRoofClearance()
    {
        var (input, g) = Case(DischargeOutletPosition.Right);

        var walls = DischargeBasinLayoutCalculator.CalculateWalls(input, g);

        Assert.Equal(4, walls.Count);
        Assert.All(walls, w =>
        {
            Assert.Equal(g.TT1, w.Thickness, Precision);
            Assert.Equal(DischargeBasinLayoutCalculator.BaseLevelName, w.LevelName);
            Assert.Equal(0, w.BaseOffset, Precision);
            Assert.True(w.IsExterior);
            // 벽체 상단이 상부슬래브 하면에 맞는다: (TB - TF)*1000 - TT3
            Assert.Equal(7200, w.Height, Precision);
        });
    }

    [Fact]
    public void Center_Walls_FollowSixSidedOutline()
    {
        var (input, g) = Case(DischargeOutletPosition.Center);

        var walls = DischargeBasinLayoutCalculator.CalculateWalls(input, g);

        Assert.Equal(6, walls.Count);
    }

    /// <summary>
    /// Revit 벽은 양 끝 조인이 끊겨 있어(RevitWallCommandRepo) 코너를 직접 메워야 한다.
    /// 직각 코너에서는 한쪽이 t/2 만큼 나가고 이웃이 t/2 만큼 물러나 겹침도 틈도 없어야 한다.
    /// </summary>
    [Fact]
    public void Walls_RightAngleCorner_TilesWithoutGapOrOverlap()
    {
        var (input, g) = Case(DischargeOutletPosition.Right);
        var half = g.TT1 / 2;
        var placement = DischargeBasinPlacement.For(input, g);

        var walls = DischargeBasinLayoutCalculator.CalculateWalls(input, g);

        // 펌프측 벽은 배치 변환 뒤 Y 방향으로 뻗고 중심선이 x = OriginX − t/2 에 선다.
        var pumpSide = Assert.Single(walls.Where(w =>
            Math.Abs(w.StartPoint.X - (placement.OriginX - half)) < 1e-6 &&
            Math.Abs(w.EndPoint.X - (placement.OriginX - half)) < 1e-6));

        // 양 끝을 코너까지 t/2 씩 늘린다(로컬 x = −t ~ TLT + t 구간이 Y 로 옮겨온 것).
        var yMin = Math.Min(pumpSide.StartPoint.Y, pumpSide.EndPoint.Y);
        var yMax = Math.Max(pumpSide.StartPoint.Y, pumpSide.EndPoint.Y);
        Assert.Equal(placement.OriginY - half - half, yMin, Precision);           // -t
        Assert.Equal(placement.OriginY + 19500 + half + half, yMax, Precision);   // TLT + t

        // 이웃한 TB2 단부 벽은 그만큼 물러나 겹침도 틈도 없다.
        // 양 끝 모두 같은 Y = 단부 벽. 사선 코너 벽이 꼭짓점(같은 Y)에서 끝나므로 한쪽 끝만으론 못 고른다(2026-11-12).
        var outletEnd = Assert.Single(walls.Where(w =>
            Math.Abs(w.StartPoint.Y - (placement.OriginY + 19500 + half)) < 1e-6 &&
            Math.Abs(w.EndPoint.Y - (placement.OriginY + 19500 + half)) < 1e-6));
        Assert.Equal(placement.OriginX, Math.Min(outletEnd.StartPoint.X, outletEnd.EndPoint.X), Precision);
    }

    /// <summary>
    /// 사선 코너는 길이 맞춤으로 못 메운다 → 두 벽이 같은 꼭짓점에서 끝나고 그 끝만 결합 허용.
    /// 직각 코너 끝은 결합 끊은 채(기존 동작).
    /// </summary>
    [Fact]
    public void Center_ObliqueCorners_MeetAtVertexWithJoinAllowed()
    {
        var (input, g) = Case(DischargeOutletPosition.Center);

        var walls = DischargeBasinLayoutCalculator.CalculateWalls(input, g);
        var n = walls.Count;

        var obliqueCount = 0;
        for (var i = 0; i < n; i++)
        {
            var prev = walls[(i - 1 + n) % n];
            var cur = walls[i];
            Assert.Equal(prev.JoinAtEnd, cur.JoinAtStart);
            if (!cur.JoinAtStart) continue;

            obliqueCount++;
            Assert.Equal(prev.EndPoint.X, cur.StartPoint.X, Precision);
            Assert.Equal(prev.EndPoint.Y, cur.StartPoint.Y, Precision);
        }
        Assert.True(obliqueCount > 0);
    }

    /// <summary>우측 4변: 펌프측 벽 양 끝은 직각이라 결합 끊은 채, 사선 코너 끝만 결합 허용.</summary>
    [Theory]
    [InlineData(DischargeOutletPosition.Right)]
    [InlineData(DischargeOutletPosition.Left)]
    [InlineData(DischargeOutletPosition.Center)]
    public void ObliqueCorners_MeetAtVertexWithJoinAllowed_AllCases(DischargeOutletPosition side)
    {
        var (input, g) = Case(side);
        var placement = DischargeBasinPlacement.For(input, g);
        var half = g.TT1 / 2;

        var walls = DischargeBasinLayoutCalculator.CalculateWalls(input, g);
        var n = walls.Count;

        for (var i = 0; i < n; i++)
        {
            var prev = walls[(i - 1 + n) % n];
            Assert.Equal(prev.JoinAtEnd, walls[i].JoinAtStart);
            if (!walls[i].JoinAtStart) continue;
            Assert.Equal(prev.EndPoint.X, walls[i].StartPoint.X, Precision);
            Assert.Equal(prev.EndPoint.Y, walls[i].StartPoint.Y, Precision);
        }

        if (side == DischargeOutletPosition.Center) return;
        // 펌프측 벽(직각 코너 2개)은 결합 끊은 채
        var pumpSide = Assert.Single(walls.Where(w =>
            Math.Abs(w.StartPoint.X - w.EndPoint.X) < 1e-6 &&
            Math.Abs(Math.Abs(w.StartPoint.X - placement.OriginX) - half) < 1e-6));
        Assert.False(pumpSide.JoinAtStart || pumpSide.JoinAtEnd);
    }

    // ── 상부 개구부 ───────────────────────────────────────────────

    [Fact]
    public void Openings_OnePerPumpWithOneAccessOpening()
    {
        var (input, g) = Case(DischargeOutletPosition.Right);

        var openings = DischargeBasinLayoutCalculator.CalculateSlabOpenings(input, g);

        // Width = 프로젝트 X, Length = 프로젝트 Y (패밀리 W/L 축, Revit 실검증).
        // 도해의 TOL(길이)은 펌프 배열 방향이고 배치 변환이 그 방향을 Y 로 보내므로 Length 에 들어간다.
        var placement = DischargeBasinPlacement.For(input, g);
        Assert.Equal(4, openings.Count);
        Assert.Equal(g.TOL2, openings[0].Length, Precision);   // 진입 동선용 1개소
        Assert.All(openings.Skip(1), o => Assert.Equal(g.TOL1, o.Length, Precision));
        Assert.All(openings, o =>
        {
            Assert.Equal(g.TOB1, o.Width, Precision);
            Assert.Equal(placement.OriginX + g.TOB1 / 2, o.Position.X, Precision);
            Assert.Equal(DischargeBasinLayoutCalculator.TopSlabCode, o.HostElementCode);
            Assert.Equal(DischargeBasinLayoutCalculator.TopLevelName, o.LevelName);
        });

        // 진입 동선용(TOL2)은 개구부 도해대로 TB1 단부 내벽면(로컬 x=0 → 프로젝트 OriginY)에 한 변을 붙인다.
        Assert.Equal(placement.OriginY + g.TOL2 / 2, openings[0].Position.Y, Precision);
        // 나머지는 펌프 중심.
        for (var i = 1; i < openings.Count; i++)
            Assert.Equal(placement.OriginY + DischargeBasinLayoutCalculator.PumpCenterX(input, g, i), openings[i].Position.Y, Precision);
    }

    /// <summary>개구부 X는 펌프 중심을 따른다. 펌프 배열은 TLT 산식과 같은 순서여야 한다.</summary>
    [Fact]
    public void PumpCenters_SpanMatchesTotalInnerLength()
    {
        var (input, g) = Case(DischargeOutletPosition.Right);

        var first = DischargeBasinLayoutCalculator.PumpCenterX(input, g, 0);
        var last = DischargeBasinLayoutCalculator.PumpCenterX(input, g, input.N - 1);

        Assert.Equal(g.TL1 + input.D / 2, first, Precision);                 // 1500 + 750
        Assert.Equal(g.TLT - g.TL1 - input.D / 2, last, Precision);          // 대칭
        Assert.Equal(input.D + g.TL2, (last - first) / (input.N - 1), Precision);
    }

    // ── 기둥 · 거더 (확장 지점) ───────────────────────────────────

    /// <summary>
    /// 기본값은 기둥·거더 없음. 기둥 유형 선택 화면이 없어 Frame 이 항상 null 이다(확인필요사항 C-4).
    /// </summary>
    [Fact]
    public void Frame_IsAbsentByDefault_SoNoColumnsOrGirders()
    {
        var (input, g) = Case(DischargeOutletPosition.Right);

        Assert.Null(input.Frame);
        Assert.Empty(DischargeBasinLayoutCalculator.CalculateColumns(input, g));
        Assert.Empty(DischargeBasinLayoutCalculator.CalculateGirders(input, g));
    }

    /// <summary>Frame 을 채우면 그대로 돌아야 한다 — 나중에 배치가 확정됐을 때의 동작을 미리 고정해 둔다.</summary>
    [Fact]
    public void Frame_WhenSupplied_PutsColumnsBetweenPumpsOnTheColumnLine()
    {
        var (baseInput, g) = Case(DischargeOutletPosition.Right);
        var input = baseInput with { Frame = new DischargeBasinFrameSpec(3800, "기둥류 : 기둥 A") };

        var columns = DischargeBasinLayoutCalculator.CalculateColumns(input, g);

        var placement = DischargeBasinPlacement.For(input, g);
        // 평면 도해: 펌프 사이(TL2 가운데)에 펌프 대수 − 1 개.
        Assert.Equal(input.N - 1, columns.Count);
        Assert.All(columns, c =>
        {
            // 기둥 열 위치는 로컬 Y → 프로젝트 X.
            Assert.Equal(placement.OriginX + 3800, c.Position.X, Precision);
            Assert.Equal("기둥류 : 기둥 A", c.TypeName);
            Assert.Equal(DischargeBasinLayoutCalculator.BaseLevelName, c.BaseLevelName);
            Assert.Equal(DischargeBasinLayoutCalculator.TopLevelName, c.TopLevelName);
            // 상단은 상부슬래브 하면에서 거더 돌출높이만큼 더 내려온다.
            Assert.Equal(-(g.TT3 + g.TGH1), c.TopOffset, Precision);
        });

        // 펌프 축(프로젝트 Y)은 이웃한 두 펌프 중심의 가운데 = 1번 펌프 중심 + (i + ½)·피치.
        for (var i = 0; i < columns.Count; i++)
            Assert.Equal(input.FirstPumpCenterY + (i + 0.5) * input.B8PlusT5, columns[i].Position.Y, Precision);
    }

    [Fact]
    public void Frame_WhenSupplied_GirderSpansInnerLengthUnderRoof()
    {
        var (baseInput, g) = Case(DischargeOutletPosition.Right);
        var input = baseInput with { Frame = new DischargeBasinFrameSpec(3800, "기둥류 : 기둥 A") };

        var girder = Assert.Single(DischargeBasinLayoutCalculator.CalculateGirders(input, g));

        var placement = DischargeBasinPlacement.For(input, g);
        Assert.Equal(placement.OriginY, girder.StartPoint.Y, Precision);
        Assert.Equal(placement.OriginY + g.TLT, girder.EndPoint.Y, Precision);
        Assert.Equal(placement.OriginX + 3800, girder.StartPoint.X, Precision);
        // 상부슬래브 하면에 상단을 맞춘다.
        Assert.Equal(g.TB * 1000 - g.TT3, girder.StartPoint.Z, Precision);
        Assert.Equal(0, girder.ZJustification);
        Assert.Equal(g.TGB1, girder.Width, Precision);
        Assert.Equal(g.TGH1, girder.Height, Precision);
    }

    [Fact]
    public void Frame_WithoutGirder_KeepsColumnsButDropsGirder()
    {
        var (baseInput, g) = Case(DischargeOutletPosition.Right);
        var input = baseInput with { Frame = new DischargeBasinFrameSpec(3800, "기둥류 : 기둥 A", CreateGirder: false) };

        var columns = DischargeBasinLayoutCalculator.CalculateColumns(input, g);

        Assert.Equal(input.N - 1, columns.Count);
        Assert.Empty(DischargeBasinLayoutCalculator.CalculateGirders(input, g));
        // 거더가 없으면 기둥 상단은 슬래브 하면까지 올라온다.
        Assert.All(columns, c => Assert.Equal(-g.TT3, c.TopOffset, Precision));
    }

    // ── 펌프측 벽 토출관 관통구 ───────────────────────────────────

    /// <summary>
    /// 관통구는 펌프 대수만큼, 펌프측(서쪽) 벽 중심선 위, 각 펌프 중심에 뚫린다.
    /// 지름은 펌프장 W2 개구부와 같은 관경 D 그대로다(여유 없음).
    /// </summary>
    [Fact]
    public void WallOpenings_OnePerPumpOnPumpSideWall()
    {
        var (input, g) = PlacedCase();
        var placement = DischargeBasinPlacement.For(input, g);

        var openings = DischargeBasinLayoutCalculator.CalculateWallOpenings(input, g);

        Assert.Equal(input.N, openings.Count);
        for (var i = 0; i < input.N; i++)
        {
            Assert.Equal(input.D, openings[i].Diameter, Precision);
            // 펌프측 벽 중심선 = 내면에서 벽두께 절반만큼 바깥(−X).
            Assert.Equal(placement.OriginX - g.TT1 / 2, openings[i].Position.X, Precision);
            // 상부 개구부와 같은 펌프 중심 Y.
            Assert.Equal(input.FirstPumpCenterY + i * input.B8PlusT5, openings[i].Position.Y, Precision);
            Assert.Equal(DischargeBasinLayoutCalculator.WallCode, openings[i].HostElementCode);
            Assert.Equal(DischargeBasinLayoutCalculator.BaseLevelName, openings[i].LevelName);
        }
    }

    /// <summary>
    /// ★ 관이 이어지는 조건 — 관통구 절대 표고가 펌프장 W2 개구부와 같아야 한다.
    /// 펌프장: 밸브실레벨 + H6 = (펌프장HWL×1000 + H3) − H7 − D
    /// 토출조: 바닥레벨(TF) + OffsetZ = TE×1000
    /// 두 값이 같다는 것이 "서쪽에서 펌프와 이어진다"의 수치적 정의다.
    /// </summary>
    [Fact]
    public void WallOpenings_MatchPumpingStationW2OpeningElevation()
    {
        var (input, g) = PlacedCase();

        var opening = DischargeBasinLayoutCalculator.CalculateWallOpenings(input, g)[0];

        var basinElevation = g.TF * 1000 + opening.OffsetZ;
        var stationElevation = input.PumpHwl * 1000 + input.H3 - input.H7 - input.D;

        Assert.Equal(stationElevation, basinElevation, Precision);
        // 기본 제원에서는 토출관 하부 두께와 같은 값이 된다.
        Assert.Equal(g.TH3, opening.OffsetZ, Precision);
    }

    // ── 좌측 토출 미러(docs/320 § A) ─────────────────────────────

    [Fact]
    public void Left_GeometryMatchesRight()
    {
        var (_, right) = PlacedCase(DischargeOutletPosition.Right);
        var (_, left) = PlacedCase(DischargeOutletPosition.Left);
        Assert.Equal(right.TB2, left.TB2, Precision);
        Assert.Equal(right.TLT, left.TLT, Precision);
        Assert.Equal(right.TT5, left.TT5);
    }

    [Fact]
    public void Left_OutlineStaysCounterClockwiseWithWideEndAtLowY()
    {
        var (input, g) = PlacedCase(DischargeOutletPosition.Left);
        var outline = DischargeBasinLayoutCalculator.CalculateProjectOutline(input, g);

        var area = 0.0;
        for (var i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        Assert.True(area > 0);

        // 가장 먼 +X 점 = TB2 단부 → 낮은 Y(OriginY)
        var placement = DischargeBasinPlacement.For(input, g);
        var far = outline.OrderByDescending(p => p.X).First();
        Assert.Equal(placement.OriginX + g.TB2, far.X, Precision);
        Assert.Equal(placement.OriginY, far.Y, Precision);
    }

    [Fact]
    public void Left_PumpOpeningsAlignWithRightPositions()
    {
        var (rightInput, rightGeometry) = PlacedCase(DischargeOutletPosition.Right);
        var (leftInput, leftGeometry) = PlacedCase(DischargeOutletPosition.Left);
        var right = DischargeBasinLayoutCalculator.CalculateWallOpenings(rightInput, rightGeometry).Select(o => o.Position.Y).OrderBy(y => y).ToList();
        var left = DischargeBasinLayoutCalculator.CalculateWallOpenings(leftInput, leftGeometry).Select(o => o.Position.Y).OrderBy(y => y).ToList();
        Assert.Equal(right.Count, left.Count);
        for (var i = 0; i < right.Count; i++)
            Assert.Equal(right[i], left[i], Precision);
    }

    [Fact]
    public void Left_EntryOpeningSitsAtTb1EndWall()
    {
        var (input, g) = PlacedCase(DischargeOutletPosition.Left);
        var placement = DischargeBasinPlacement.For(input, g);
        var entry = DischargeBasinLayoutCalculator.CalculateSlabOpenings(input, g)[0];
        Assert.Equal(placement.OriginY + g.TLT - g.TOL2 / 2, entry.Position.Y, Precision);
    }

    [Fact]
    public void Center_IsNotMirrored()
    {
        var (input, g) = PlacedCase(DischargeOutletPosition.Center);
        Assert.Null(DischargeBasinPlacement.For(input, g).MirrorLength);
    }

    // ── 펌프장 기준 배치 ──────────────────────────────────────────

    /// <summary>펌프장 우측 예시 — 전체길이 30,000 / 1번 펌프 중심 Y 2,500 / 이격 3 m.</summary>
    private static (DischargeBasinInputDto Input, DischargeBasinGeometryDto Geometry) PlacedCase(
        DischargeOutletPosition position = DischargeOutletPosition.Right)
    {
        var input = Input(position) with { StationRightEdgeX = 30000, FirstPumpCenterY = 2500 };
        var overrides = position == DischargeOutletPosition.Center ? ExcelUnselectedConduit : null;
        return (input, DischargeBasinGeometryCalculator.Calculate(input, overrides));
    }

    /// <summary>
    /// 펌프측 벽 <b>외면</b>이 펌프장 최우측 벽 외면에서 이격거리만큼 떨어져야 한다(사용자 결정: 기준면 = 벽체 외면).
    /// 이게 어긋나면 토출조가 펌프장 위에 겹쳐 생성된다 — 배치 변환을 넣은 이유 자체다.
    /// </summary>
    [Fact]
    public void Placement_PutsPumpSideWallOutsideFaceAtSeparationFromStation()
    {
        var (input, g) = PlacedCase();

        var topSlab = DischargeBasinLayoutCalculator.CalculateSlabs(input, g)
            .Single(s => s.Part == "상부슬래브");

        // 상부슬래브 외곽 = 벽체 외면. 30,000 + 3 m = 33,000
        Assert.Equal(33000, topSlab.Points.Min(p => p.X), Precision);
        // 토출조 본체는 펌프장 반대쪽(+X)으로 뻗는다.
        Assert.True(topSlab.Points.Min(p => p.X) >= input.StationRightEdgeX);
    }

    /// <summary>
    /// 펌프 중심이 펌프장 펌프 중심과 하나씩 맞아야 토출관이 직선으로 들어온다.
    /// 피치는 D + TL2 = B8 + T5 로 이미 같으므로 1번만 맞추면 전부 맞는다.
    /// </summary>
    [Fact]
    public void Placement_AlignsEveryPumpCenterWithStationPumpCenters()
    {
        var (input, g) = PlacedCase();

        // 펌프측 벽 관통구는 펌프마다 펌프 중심에 뚫린다(상부 개구부 1번은 단부 내벽면 정렬이라 기준으로 못 쓴다).
        var openings = DischargeBasinLayoutCalculator.CalculateWallOpenings(input, g);

        for (var i = 0; i < input.N; i++)
            Assert.Equal(input.FirstPumpCenterY + i * input.B8PlusT5, openings[i].Position.Y, Precision);
    }

    /// <summary>
    /// 넓은 단부(TB2 · 토출관로 출구)가 +Y 를 향해야 한다 — 평면 도해를 반시계 90° 돌린 배치.
    /// 축 교환이라 진행 방향이 뒤집히므로, 반시계(부호 있는 면적 &gt; 0)가 유지되는지도 함께 고정한다.
    /// 반시계가 깨지면 바깥 오프셋이 안쪽으로 파고들어 슬래브·벽체가 통째로 뒤집힌다.
    /// </summary>
    [Theory]
    [InlineData(DischargeOutletPosition.Right)]
    [InlineData(DischargeOutletPosition.Center)]
    public void Placement_KeepsOutlineCounterClockwiseAndWideEndTowardPlusY(DischargeOutletPosition position)
    {
        var (input, g) = PlacedCase(position);

        var outline = DischargeBasinLayoutCalculator.CalculateProjectOutline(input, g);

        Assert.True(SignedArea(outline) > 0, "배치 변환 뒤에도 외곽선은 반시계여야 한다.");

        // 가장 먼 +X(토출측)에 있는 점들 중 최대 Y 쪽이 TB2(넓은 단부)다.
        var maxX = outline.Max(p => p.X);
        Assert.Equal(input.FirstPumpCenterY - (g.TL1 + input.D / 2) + g.TLT,
                     outline.Max(p => p.Y), Precision);
        Assert.Equal(DischargeBasinPlacement.For(input, g).OriginX + g.TB2, maxX, Precision);
    }

    private static double SignedArea(IReadOnlyList<Point2D> points)
    {
        var sum = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2;
    }

    // ── 퇴화한 제원 차단 ──────────────────────────────────────────

    /// <summary>
    /// 중앙부 토출은 토출관로가 내공 총길이를 넘으면 TL3 가 음수가 되고 평면이 자기교차한다.
    /// 그대로 두면 Floor.Create 에서 예외로 터지므로 Transaction 전에 막아야 한다.
    /// 예: D=800 · N=2 · (B8+T5)=2500 → TLT=4,900, 암거 2련(OH 2,000 · OT 300) → TL3 = −700.
    /// </summary>
    [Fact]
    public void Center_WhenConduitOutgrowsInnerLength_IsBlockedWithReason()
    {
        var input = Input(DischargeOutletPosition.Center) with { D = 800, N = 2, B8PlusT5 = 2500 };
        var g = DischargeBasinGeometryCalculator.Calculate(input);

        Assert.Equal(4900, g.TLT, Precision);
        Assert.Equal(-700, g.TL3!.Value, Precision);

        var reason = DischargeBasinLayoutCalculator.GetUnsupportedReason(input, g);

        Assert.NotNull(reason);
        Assert.Contains("TL3", reason!);
    }

    /// <summary>경계 — TL3 가 양수면 통과해야 한다(과잉 차단 방지).</summary>
    [Fact]
    public void Center_WithPositiveSlopeLength_IsNotBlocked()
    {
        var (input, g) = Case(DischargeOutletPosition.Center);

        Assert.True(g.TL3!.Value > 0);
        Assert.Null(DischargeBasinLayoutCalculator.GetUnsupportedReason(input, g));
    }

    // ── 단차 적용 차단 ────────────────────────────────────────────

    /// <summary>
    /// 단차 적용은 아직 만들지 않는다. 수령자료의 평면 도해가 단차 미적용과 동일해
    /// 단차 구간의 길이 방향 범위가 원본에 없기 때문이다(docs/164_토출조_확인필요사항.md C-5).
    /// </summary>
    [Fact]
    public void Step_IsNotBuildableYet()
    {
        Assert.Null(Reason(DischargeOutletPosition.Right));
        Assert.Null(Reason(DischargeOutletPosition.Center));

        Assert.Contains("단차", Reason(DischargeOutletPosition.Right, DischargeStepType.Applied)!);
        Assert.Contains("단차", Reason(DischargeOutletPosition.Center, DischargeStepType.Applied)!);

        static string? Reason(DischargeOutletPosition position, DischargeStepType step = DischargeStepType.None)
        {
            var (input, g) = Case(position, step);
            return DischargeBasinLayoutCalculator.GetUnsupportedReason(input, g);
        }
    }

    /// <summary>
    /// 차단 사유의 근거 — 좁은 단부에서는 단차 벽체가 내공 폭을 넘는다.
    /// 이 관계가 깨지면(예: TB6 정의 변경) 차단을 재검토해야 한다.
    /// </summary>
    [Fact]
    public void Step_RaisedWidthPlusWallExceedsNarrowEnd()
    {
        var input = Input(DischargeOutletPosition.Right, DischargeStepType.Applied);
        var g = DischargeBasinGeometryCalculator.Calculate(input);

        Assert.Equal(3700, g.TB6!.Value, Precision);
        Assert.True(g.TB6!.Value + g.TT1 > g.TB1);   // 4100 > 3800
    }
}
