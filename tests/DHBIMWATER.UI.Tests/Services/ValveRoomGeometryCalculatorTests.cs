using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Application.Services;
using DHBIMWATER.Core.Geometry;
using Xunit;

namespace DHBIMWATER.UI.Tests.Services;

public class ValveRoomGeometryCalculatorTests
{
    [Fact]
    public void CalculateWalls_UsesClearDistanceToIntermediateWallLeftFace()
    {
        var dto = CreateMudValveRoomDto(intermediateWallOffset: 1200, intermediateWallThickness: 300);

        var wall = Assert.Single(ValveRoomGeometryCalculator.CalculateWalls(dto).Where(w => w.Part == "중간벽"));

        Assert.Equal(1350, wall.StartPoint.X);
        Assert.Equal(1350, wall.EndPoint.X);
    }

    [Fact]
    public void CalculateColumns_OffsetsTopBelowUpperSlabThickness()
    {
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250);

        var column = Assert.Single(ValveRoomGeometryCalculator.CalculateColumns(dto));

        Assert.Equal(ValveRoomGeometryCalculator.TopLevelName, column.TopLevelName);
        Assert.Equal(-250, column.TopOffset);
    }

    [Fact]
    public void AirValveRoom_UsesIndependentFoundationInsteadOfFoundationFloor()
    {
        var dto = new ValveRoomGeometryRequestDto(
            new ValveRoomDesignConditionDto("공기밸브실", 123.45),
            new ValveRoomPlanSpecDto(1500, 1800),
            new ValveRoomProfileSpecDto(2000, 100, 650, 300, 300, 400),
            null,
            null);

        var foundation = Assert.Single(ValveRoomGeometryCalculator.CalculateFoundations(dto));

        Assert.DoesNotContain(ValveRoomGeometryCalculator.CalculateSlabs(dto), slab => slab.Part == "기초");
        Assert.Equal(900, foundation.Position.X);
        Assert.Equal(750, foundation.Position.Y);
        Assert.Equal(123450, foundation.Position.Z);
        // 독립기초 외곽 = 내부치수 + 외벽 두께 ×2 + 내밀기 ×2 (기초 슬래브 산출식과 동일 규약, docs/135 § 3-1)
        Assert.Equal(3000, foundation.Length);
        Assert.Equal(2700, foundation.Width);
        Assert.Equal(650, foundation.Thickness);
    }

    [Fact]
    public void AirValveRoom_ExcludesPlainConcreteAndPlacesXAxisVoidAtPipeCenterDepth()
    {
        var dto = new ValveRoomGeometryRequestDto(
            new ValveRoomDesignConditionDto("공기밸브실", 123.45),
            new ValveRoomPlanSpecDto(1500, 1800),
            new ValveRoomProfileSpecDto(2000, 100, 650, 100, 300, 400),
            null,
            null);

        var voidDefinition = ValveRoomGeometryCalculator.CalculateAirValveVoid(dto, new AirValveRoomSpecDto(300, 250, "X"));

        Assert.DoesNotContain(ValveRoomGeometryCalculator.CalculateSlabs(dto), slab => slab.Part == "버림콘크리트");
        Assert.Equal(0, voidDefinition.PipeCenter.X);
        Assert.Equal(750, voidDefinition.PipeCenter.Y);
        Assert.Equal(123200, voidDefinition.PipeCenter.Z);
        Assert.Equal(150, voidDefinition.Radius);
        Assert.Equal("X", voidDefinition.Axis);
    }

    [Fact]
    public void AirValveRoom_VoidRadiusUsesVoidDiameter_WhenGiven()
    {
        // Void = 외경 + 여유(2026-10-29). DN300 주철관 외경 326 → 반지름 163.
        var dto = new ValveRoomGeometryRequestDto(
            new ValveRoomDesignConditionDto("공기밸브실", 0),
            new ValveRoomPlanSpecDto(1500, 1800),
            new ValveRoomProfileSpecDto(2000, 100, 650, 100, 300, 400),
            null,
            null);

        var voidDefinition = ValveRoomGeometryCalculator.CalculateAirValveVoid(dto, new AirValveRoomSpecDto(300, 250, "X", 326));

        Assert.Equal(163, voidDefinition.Radius);
    }

    [Fact]
    public void AirValveRoom_PlacesYAxisVoidAtPlanLengthCenter()
    {
        var dto = new ValveRoomGeometryRequestDto(
            new ValveRoomDesignConditionDto("공기밸브실", 0),
            new ValveRoomPlanSpecDto(1500, 1800),
            new ValveRoomProfileSpecDto(2000, 100, 650, 100, 300, 400),
            null,
            null);

        var voidDefinition = ValveRoomGeometryCalculator.CalculateAirValveVoid(dto, new AirValveRoomSpecDto(200, 325, "Y"));

        Assert.Equal(900, voidDefinition.PipeCenter.X);
        Assert.Equal(0, voidDefinition.PipeCenter.Y);
        Assert.Equal(-325, voidDefinition.PipeCenter.Z);
        Assert.Equal(100, voidDefinition.Radius);
        Assert.Equal("Y", voidDefinition.Axis);
    }

    [Theory]
    [InlineData("이토밸브실")]
    [InlineData("제수밸브실")]
    [InlineData("공기밸브실")]
    public void CalculateGeometry_UsesInnerHeightFromFoundationTopToUpperSlabUnderside(string roomType)
    {
        const double foundationTopEl = 123.45;
        const double innerHeight = 2500;
        const double upperSlabThickness = 400;
        var dto = new ValveRoomGeometryRequestDto(
            new ValveRoomDesignConditionDto(roomType, foundationTopEl),
            new ValveRoomPlanSpecDto(2000, 3000),
            new ValveRoomProfileSpecDto(innerHeight, 100, 500, 300, 300, upperSlabThickness),
            roomType == "이토밸브실" ? new MudValveRoomSpecDto(false, 0, 0, false, 0, 0, 0) : null,
            null);

        var topLevel = Assert.Single(ValveRoomGeometryCalculator.CalculateLevels(dto).Where(l => l.Name == ValveRoomGeometryCalculator.TopLevelName));
        var upperSlab = Assert.Single(ValveRoomGeometryCalculator.CalculateSlabs(dto).Where(s => s.Part == "상부슬래브"));
        // 외벽은 ㅁ자 코너 정합(2026-07-28 확정)으로 4변이 각각 생성된다. 4변 모두 안목높이를 지켜야 한다(docs/135 § 3-2).
        var walls = ValveRoomGeometryCalculator.CalculateWalls(dto).Where(w => w.Part == "외벽").ToList();
        Assert.Equal(4, walls.Count);

        Assert.Equal(126350, topLevel.Elevation);
        Assert.Equal(topLevel.Elevation, upperSlab.ElevationZ);
        Assert.All(walls, w => Assert.Equal(innerHeight, w.Height));
    }

    // 타이밸브실은 이토밸브실 형상을 그대로 쓴다(docs/142 § 1). CreateValveRoomUseCase.NormalizeGeometryType이
    // GeometryType만 "이토밸브실"로 바꿔 넘기고, RoomType은 DH_Zone에 실제 이름으로 남는다.
    [Fact]
    public void TieValveRoom_BuildsIntermediateWallLikeMudValveRoom()
    {
        var dto = CreateTieValveRoomDto();

        var wall = Assert.Single(ValveRoomGeometryCalculator.CalculateWalls(dto).Where(w => w.Part == "중간벽"));

        Assert.Equal(1350, wall.StartPoint.X);
        Assert.Equal("타이밸브실", wall.Zone);
    }

    [Fact]
    public void TieValveRoom_KeepsRoomTypeInSlabZone()
    {
        var dto = CreateTieValveRoomDto();

        var slabs = ValveRoomGeometryCalculator.CalculateSlabs(dto);

        Assert.All(slabs, slab => Assert.Equal("타이밸브실", slab.Zone));
    }

    // ── 헌치 (docs/153 § A) ───────────────────────────────────────────────
    // 내부공간은 InnerWidth 2500(Y) × InnerLength 4000(X), 기초 상부 EL 0, 상부슬래브 두께 250.

    [Fact]
    public void CalculateHaunches_LowerOnly_PlacesFourSegmentsAtFoundationTop()
    {
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250) with { HasLowerHaunch = true };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        Assert.Equal(4, haunches.Count);
        Assert.All(haunches, h =>
        {
            Assert.Equal("HAUNCH", h.Part);
            Assert.Equal("VR-H2", h.ElementCode);
            Assert.Equal(ValveRoomGeometryCalculator.BaseLevelName, h.LevelName);
            Assert.Equal(ValveRoomGeometryCalculator.BaseElevation(dto), h.StartPoint.Z);
            Assert.Equal(ValveRoomGeometryCalculator.BaseElevation(dto), h.EndPoint.Z);
        });
        // 반시계 폐합: 첫 구간은 원점에서 +X, 마지막 구간은 원점으로 되돌아온다.
        Assert.Equal(0, haunches[0].StartPoint.X);
        Assert.Equal(0, haunches[0].StartPoint.Y);
        Assert.Equal(3000, haunches[0].EndPoint.X);   // InnerLength
        Assert.Equal(0, haunches[3].EndPoint.X);
        Assert.Equal(0, haunches[3].EndPoint.Y);
    }

    [Fact]
    public void CalculateHaunches_UpperSitsUnderUpperSlab()
    {
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250) with { HasUpperHaunch = true };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        var expectedZ = ValveRoomGeometryCalculator.TopElevation(dto) - 250;
        Assert.Equal(4, haunches.Count);
        Assert.All(haunches, h =>
        {
            Assert.Equal("VR-H1", h.ElementCode);
            Assert.Equal(ValveRoomGeometryCalculator.TopLevelName, h.LevelName);
            Assert.Equal(expectedZ, h.StartPoint.Z);
        });
    }

    [Fact]
    public void CalculateHaunches_UpperHaunch_RotatesCrossSection90Degrees()
    {
        // 상단 헌치만 단면 회전 90도, 하단 헌치는 회전 없음(docs/160).
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { HasLowerHaunch = true, HasUpperHaunch = true };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        var upper = haunches.Where(h => h.ElementCode == "VR-H1").ToList();
        var lower = haunches.Where(h => h.ElementCode == "VR-H2").ToList();
        Assert.NotEmpty(upper);
        Assert.NotEmpty(lower);
        Assert.All(upper, h => Assert.Equal(90, h.CrossSectionRotationDeg));
        Assert.All(lower, h => Assert.Equal(0, h.CrossSectionRotationDeg));
    }

    [Fact]
    public void CalculateHaunches_MudRoomWithIntermediateWall_AddsTwoSegmentsPerLevel()
    {
        var dto = CreateMudValveRoomDto(intermediateWallOffset: 1200, intermediateWallThickness: 300)
            with { HasLowerHaunch = true, HasUpperHaunch = true };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        // 레벨당 외벽 4면 중 중간벽(x∈[1200,1500])이 가로지르는 y=0·y=2500 두 면이 2토막씩 나뉘어
        // 외벽 6 + 중간벽 2 = 8, 두 레벨이므로 16이다(docs/162 § B-1).
        Assert.Equal(16, haunches.Count);
        // 중간벽 중심 1350 기준 양측면 1200 / 1500. 두 레벨이라 각각 2개씩.
        var left = haunches.Where(h => h.StartPoint.X == 1200 && h.EndPoint.X == 1200).ToList();
        var right = haunches.Where(h => h.StartPoint.X == 1500 && h.EndPoint.X == 1500).ToList();
        Assert.Equal(2, left.Count);
        Assert.Equal(2, right.Count);
        // 좌측면은 +Y, 우측면은 -Y로 진행해야 각각의 왼쪽이 그 면이 접한 실내가 된다.
        Assert.All(left, h => Assert.True(h.EndPoint.Y > h.StartPoint.Y));
        Assert.All(right, h => Assert.True(h.EndPoint.Y < h.StartPoint.Y));
    }

    [Fact]
    public void CalculateHaunches_IntermediateSlab_AddsThirdLevelUnderIntermediateSlab()
    {
        var dto = new ValveRoomGeometryRequestDto(
            new ValveRoomDesignConditionDto("이토밸브실", 0),
            new ValveRoomPlanSpecDto(2500, 4000),
            new ValveRoomProfileSpecDto(2500, 100, 500, 300, 300, 250),
            new MudValveRoomSpecDto(true, 1200, 300, true, 300, 2000, 2000),
            null)
        { HasLowerHaunch = true, HasUpperHaunch = true };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        // 기초 상단 · 중간슬래브 하단 · 상부슬래브 하단 3개 레벨(docs/162 § B-2).
        Assert.Equal(3, haunches.Select(h => h.StartPoint.Z).Distinct().Count());
    }

    [Fact]
    public void CalculateHaunches_BothUnchecked_ReturnsEmpty()
    {
        var dto = CreateMudValveRoomDto(intermediateWallOffset: 1200, intermediateWallThickness: 300);

        Assert.Empty(ValveRoomGeometryCalculator.CalculateHaunches(dto));
    }

    [Fact]
    public void CalculateBeams_SluiceRoom_UnaffectedByHaunch()
    {
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { HasLowerHaunch = true, HasUpperHaunch = true };

        var beams = ValveRoomGeometryCalculator.CalculateBeams(dto);

        // X방향 1 + Y방향 1. 헌치는 CalculateHaunches가 따로 반환하므로 여기에 섞이지 않는다.
        Assert.Equal(2, beams.Count);
        Assert.All(beams, b => Assert.Equal("VR-B", b.ElementCode));
    }

    [Fact]
    public void CalculateBeams_SplitsAtColumns()
    {
        // 내부 3000(X) × 2000(Y), X방향 보 1개(y=500) · Y방향 보 1개(x=500) → 기둥 1개(500,500), 단면 400×400.
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { SluiceColumnWidthX = 400, SluiceColumnDepthY = 400 };

        var beams = ValveRoomGeometryCalculator.CalculateBeams(dto);

        // 보 2개가 각각 기둥에 끊겨 2토막 → 4개.
        Assert.Equal(4, beams.Count);
        Assert.All(beams, b => Assert.Equal("VR-B", b.ElementCode));
        var z = ValveRoomGeometryCalculator.TopElevation(dto);
        Assert.All(beams, b => Assert.Equal((z, z), (b.StartPoint.Z, b.EndPoint.Z)));

        // X방향 보(y=500)는 기둥 X범위 300~700을 비운다.
        var alongX = beams.Where(b => b.Part == "X방향 보").OrderBy(b => b.StartPoint.X).ToList();
        Assert.Equal(2, alongX.Count);
        Assert.All(alongX, b => Assert.Equal((500d, 500d), (b.StartPoint.Y, b.EndPoint.Y)));
        Assert.Equal((0d, 300d), (alongX[0].StartPoint.X, alongX[0].EndPoint.X));
        Assert.Equal((700d, 3000d), (alongX[1].StartPoint.X, alongX[1].EndPoint.X));

        // Y방향 보(x=500)는 기둥 Y범위 300~700을 비운다.
        var alongY = beams.Where(b => b.Part == "Y방향 보").OrderBy(b => b.StartPoint.Y).ToList();
        Assert.Equal(2, alongY.Count);
        Assert.All(alongY, b => Assert.Equal((500d, 500d), (b.StartPoint.X, b.EndPoint.X)));
        Assert.Equal((0d, 300d), (alongY[0].StartPoint.Y, alongY[0].EndPoint.Y));
        Assert.Equal((700d, 2000d), (alongY[1].StartPoint.Y, alongY[1].EndPoint.Y));
    }

    [Fact]
    public void CalculateBeams_WithoutColumnSection_KeepsWholeBeams()
    {
        // 기둥 단면 실측에 실패하면(0) 종전처럼 통짜 보가 나온다.
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250);

        var beams = ValveRoomGeometryCalculator.CalculateBeams(dto);

        Assert.Equal(2, beams.Count);
        var alongX = Assert.Single(beams.Where(b => b.Part == "X방향 보"));
        Assert.Equal((0d, 3000d), (alongX.StartPoint.X, alongX.EndPoint.X));
        var alongY = Assert.Single(beams.Where(b => b.Part == "Y방향 보"));
        Assert.Equal((0d, 2000d), (alongY.StartPoint.Y, alongY.EndPoint.Y));
    }

    [Fact]
    public void CalculateBeams_ColumnCoversWholeBeam_DropsThatBeam()
    {
        // 기둥 중심이 x=500이므로 폭 5000(= -2000~3000)이면 X방향 보(0~3000)를 통째로 덮어 남는 토막이 없다.
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { SluiceColumnWidthX = 5000, SluiceColumnDepthY = 400 };

        var beams = ValveRoomGeometryCalculator.CalculateBeams(dto);

        Assert.Empty(beams.Where(b => b.Part == "X방향 보"));
        var alongY = beams.Where(b => b.Part == "Y방향 보").OrderBy(b => b.StartPoint.Y).ToList();
        Assert.Equal(2, alongY.Count);
        Assert.Equal((0d, 300d), (alongY[0].StartPoint.Y, alongY[0].EndPoint.Y));
        Assert.Equal((700d, 2000d), (alongY[1].StartPoint.Y, alongY[1].EndPoint.Y));
    }

    [Fact]
    public void CalculateHaunches_CornerDrainPit_SkipsHaunchOverPitOnBothTouchingWalls()
    {
        // 내부 X 0~3000 / Y 0~2000. 좌하단(원점쪽) 코너에 1000(X) × 800(Y) 피트.
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { HasLowerHaunch = true, DrainPits = new[] { Pit(centerX: 500, centerY: 400, lengthX: 1000, widthY: 800) } };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        // 피트가 문 두 면은 남은 토막 1개씩, 나머지 두 면은 통짜 — 합 4개.
        Assert.Equal(4, haunches.Count);
        // Y=0 면: 피트 길이(1000)만큼 비우고 1000~3000만 남는다.
        var bottom = Assert.Single(haunches.Where(h => h.StartPoint.Y == 0 && h.EndPoint.Y == 0));
        Assert.Equal(1000, bottom.StartPoint.X);
        Assert.Equal(3000, bottom.EndPoint.X);
        // X=0 면: 피트 폭(800)만큼 비우고 2000~800만 남는다(진행 방향 -Y 유지).
        var left = Assert.Single(haunches.Where(h => h.StartPoint.X == 0 && h.EndPoint.X == 0));
        Assert.Equal(2000, left.StartPoint.Y);
        Assert.Equal(800, left.EndPoint.Y);
        // 피트가 닿지 않는 두 면은 온전하다.
        Assert.Contains(haunches, h => h.StartPoint.X == 3000 && h.EndPoint.X == 3000 && h.StartPoint.Y == 0 && h.EndPoint.Y == 2000);
        Assert.Contains(haunches, h => h.StartPoint.Y == 2000 && h.EndPoint.Y == 2000 && h.StartPoint.X == 3000 && h.EndPoint.X == 0);
    }

    [Fact]
    public void CalculateHaunches_DrainPitInMiddleOfWall_SplitsSegmentIntoTwo()
    {
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { HasLowerHaunch = true, DrainPits = new[] { Pit(centerX: 1500, centerY: 400, lengthX: 1000, widthY: 800) } };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        // Y=0 면이 1000~2000 구간에서 끊겨 앞·뒤 두 토막이 된다 → 3 + 2 = 5.
        Assert.Equal(5, haunches.Count);
        var bottom = haunches.Where(h => h.StartPoint.Y == 0 && h.EndPoint.Y == 0).OrderBy(h => h.StartPoint.X).ToList();
        Assert.Equal(2, bottom.Count);
        Assert.Equal((0d, 1000d), (bottom[0].StartPoint.X, bottom[0].EndPoint.X));
        Assert.Equal((2000d, 3000d), (bottom[1].StartPoint.X, bottom[1].EndPoint.X));
    }

    [Fact]
    public void CalculateHaunches_DrainPitAwayFromWalls_KeepsAllSegmentsWhole()
    {
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { HasLowerHaunch = true, DrainPits = new[] { Pit(centerX: 1500, centerY: 1000, lengthX: 1000, widthY: 800) } };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        Assert.Equal(4, haunches.Count);
        Assert.Equal(3000, haunches[0].EndPoint.X);
    }

    [Fact]
    public void CalculateHaunches_UpperHaunchIgnoresDrainPit()
    {
        // 피트는 기초만 파므로 상부슬래브 하단 헌치는 끊기지 않는다.
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { HasUpperHaunch = true, DrainPits = new[] { Pit(centerX: 500, centerY: 400, lengthX: 1000, widthY: 800) } };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        Assert.Equal(4, haunches.Count);
        Assert.Equal(0, haunches[0].StartPoint.X);
        Assert.Equal(3000, haunches[0].EndPoint.X);
    }

    [Fact]
    public void CalculateHaunches_DrainPitCoveringWholeWall_DropsThatSegment()
    {
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { HasLowerHaunch = true, DrainPits = new[] { Pit(centerX: 1500, centerY: 400, lengthX: 3000, widthY: 800) } };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        // Y=0 면은 통째로 사라지고 나머지 세 면만 남는다.
        Assert.Equal(3, haunches.Count);
        Assert.DoesNotContain(haunches, h => h.StartPoint.Y == 0 && h.EndPoint.Y == 0);
    }

    // ── 보·중간벽 절단 + 중간슬래브 하단 헌치 (docs/162) ─────────────────

    [Fact]
    public void CalculateHaunches_UpperHaunch_SplitAtBeams()
    {
        // 제수밸브실 내부공간 3000(X) × 2000(Y), 보는 X방향 1개(y=500) · Y방향 1개(x=500), 폭 400.
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { HasUpperHaunch = true, SluiceBeamWidth = 400 };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        // 네 면 모두 보 1개씩에 끊겨 2토막 → 8개.
        Assert.Equal(8, haunches.Count);
        // x=0 면(진행 -Y)은 X방향 보가 끝나는 y 300~700을 비우고, 토막은 진행 방향(-Y)을 유지한다.
        var leftFace = haunches.Where(h => h.StartPoint.X == 0 && h.EndPoint.X == 0)
            .OrderBy(h => Math.Min(h.StartPoint.Y, h.EndPoint.Y)).ToList();
        Assert.Equal(2, leftFace.Count);
        Assert.All(leftFace, h => Assert.True(h.EndPoint.Y < h.StartPoint.Y));
        Assert.Equal((300, 0), (leftFace[0].StartPoint.Y, leftFace[0].EndPoint.Y));
        Assert.Equal((2000, 700), (leftFace[1].StartPoint.Y, leftFace[1].EndPoint.Y));
        // y=0 면(진행 +X)은 Y방향 보가 끝나는 x 300~700을 비운다.
        var bottomFace = haunches.Where(h => h.StartPoint.Y == 0 && h.EndPoint.Y == 0)
            .OrderBy(h => h.StartPoint.X).ToList();
        Assert.Equal(2, bottomFace.Count);
        Assert.Equal(0, bottomFace[0].StartPoint.X);
        Assert.Equal(300, bottomFace[0].EndPoint.X);
        Assert.Equal(700, bottomFace[1].StartPoint.X);
        Assert.Equal(3000, bottomFace[1].EndPoint.X);
    }

    [Fact]
    public void CalculateHaunches_LowerHaunch_NotSplitAtBeams()
    {
        // 보는 상부슬래브 하단에만 있다. 기초 상단 헌치는 끊기지 않는다(docs/162 § B-1).
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250)
            with { HasLowerHaunch = true, SluiceBeamWidth = 400 };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        Assert.Equal(4, haunches.Count);
        Assert.All(haunches, h => Assert.Equal("VR-H2", h.ElementCode));
    }

    [Fact]
    public void CalculateHaunches_IntermediateWall_DoesNotCutItsOwnFaceHaunches()
    {
        // 중간벽이 자기 양측면 헌치를 지워 버리면 안 된다 — 방향 판정 회귀 방지(docs/162 § C-2).
        var dto = CreateMudValveRoomDto(intermediateWallOffset: 1200, intermediateWallThickness: 300)
            with { HasLowerHaunch = true };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        var left = Assert.Single(haunches.Where(h => h.StartPoint.X == 1200 && h.EndPoint.X == 1200));
        var right = Assert.Single(haunches.Where(h => h.StartPoint.X == 1500 && h.EndPoint.X == 1500));
        // 폭(InnerWidth 2500) 전 구간이 통짜로 남는다.
        Assert.Equal(0, left.StartPoint.Y);
        Assert.Equal(2500, left.EndPoint.Y);
        Assert.Equal(2500, right.StartPoint.Y);
        Assert.Equal(0, right.EndPoint.Y);
    }

    [Fact]
    public void CalculateHaunches_IntermediateSlab_PlacesVrH3UnderSlab()
    {
        var dto = CreateMudValveRoomWithSlabDto() with { HasUpperHaunch = true };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        var intermediate = haunches.Where(h => h.ElementCode == "VR-H3").ToList();
        Assert.NotEmpty(intermediate);
        Assert.All(intermediate, h =>
        {
            // 레벨 표고는 슬래브 상면이므로 헌치는 두께만큼 아래, 곧 1층 안목 상단에 앉는다.
            Assert.Equal(ValveRoomGeometryCalculator.BaseElevation(dto) + 2000, h.StartPoint.Z);
            Assert.Equal(ValveRoomGeometryCalculator.IntermediateLevelName, h.LevelName);
            Assert.Equal(90, h.CrossSectionRotationDeg);
            Assert.Equal("HAUNCH", h.Part);
        });
    }

    [Fact]
    public void CalculateHaunches_IntermediateSlab_WithoutUpperHaunch_NoVrH3()
    {
        // 중간슬래브 하단 헌치는 "상부 헌치" 체크에 연동된다(docs/162 확정 4).
        var dto = CreateMudValveRoomWithSlabDto() with { HasLowerHaunch = true };

        var haunches = ValveRoomGeometryCalculator.CalculateHaunches(dto);

        Assert.DoesNotContain(haunches, h => h.ElementCode == "VR-H3");
    }

    /// <summary>버림콘크리트를 만드는 유형은 두께 0을 막는다 — 두께 0 바닥 유형 생성에서 Revit이 던졌다(docs/238 § D).</summary>
    [Fact]
    public void Validate_버림두께_0이면_VR101()
    {
        var dto = new ValveRoomGeometryRequestDto(
            new ValveRoomDesignConditionDto("이토밸브실", 0),
            new ValveRoomPlanSpecDto(2500, 4000),
            new ValveRoomProfileSpecDto(2500, 0, 500, 300, 300, 250),
            new MudValveRoomSpecDto(false, 0, 0, false, 0, 0, 0),
            null);

        var ex = Assert.Throws<DHBIMWATER.Shared.Errors.DhbimwaterException>(() => ValveRoomGeometryCalculator.Validate(dto));
        Assert.Equal("VR-101", ex.Code);
    }

    /// <summary>보가 실내 밖에 놓이는 오프셋·간격과 상한 초과 개수를 막는다(docs/238 § D).</summary>
    [Theory]
    [InlineData(1, 0, 0, 1, 500, 0)]       // X방향 오프셋 0
    [InlineData(1, 2000, 0, 1, 500, 0)]    // X방향 보가 내부 폭(2000) 경계
    [InlineData(1, 500, 0, 2, 1000, 2500)] // Y방향 두 번째 보가 내부 길이(3000) 밖
    [InlineData(21, 50, 50, 1, 500, 0)]    // 개수 상한 초과
    public void Validate_보배치가_실내밖이면_VR111(int countX, double offsetX, double spacingX, int countY, double offsetY, double spacingY)
    {
        var dto = new ValveRoomGeometryRequestDto(
            new ValveRoomDesignConditionDto("제수밸브실", 0),
            new ValveRoomPlanSpecDto(2000, 3000),
            new ValveRoomProfileSpecDto(2500, 100, 500, 300, 300, 250),
            null,
            new SluiceValveRoomSpecDto(countX, offsetX, spacingX, countY, offsetY, spacingY, "Beam", "Column"));

        var ex = Assert.Throws<DHBIMWATER.Shared.Errors.DhbimwaterException>(() => ValveRoomGeometryCalculator.Validate(dto));
        Assert.Equal("VR-111", ex.Code);
    }

    [Fact]
    public void CalculateSections_ReturnsAaAlongXAndBbAlongY()
    {
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250);

        var sections = ValveRoomGeometryCalculator.CalculateSections(dto);

        Assert.Equal(2, sections.Count);
        var a = sections[0];
        var b = sections[1];
        Assert.Equal("A-A", a.Name);
        Assert.Equal("B-B", b.Name);
        Assert.Equal(new Vector3D(1, 0, 0), a.Right);
        Assert.Equal(new Vector3D(0, -1, 0), b.Right);
        // 기초 중앙 = (내부 길이/2, 내부 폭/2) — PlanSpec(폭 2000, 길이 3000)
        Assert.Equal(new Point3D(1500, 1000, 0), a.CutOrigin);
        Assert.Equal(new Point3D(1500, 1000, 0), b.CutOrigin);
    }

    [Fact]
    public void CalculateSections_FarClipReachesPlainConcreteEdgePlusMargin()
    {
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250);

        var sections = ValveRoomGeometryCalculator.CalculateSections(dto);

        // halfX = 1500 + 외벽 300 + 기초확장 300 + 버림 100 = 2200, halfY = 1000 + 700 = 1700, 여유치 500
        Assert.Equal(2700, sections[0].HalfWidth);
        Assert.Equal(2200, sections[0].FarClipDepth);
        Assert.Equal(2200, sections[1].HalfWidth);
        Assert.Equal(2700, sections[1].FarClipDepth);
    }

    [Fact]
    public void CalculateSections_VerticalRangeCoversPlainConcreteToTopSlab()
    {
        var dto = CreateSluiceValveRoomDto(upperSlabThickness: 250);

        var sections = ValveRoomGeometryCalculator.CalculateSections(dto);

        // ZMin = 0 - 기초 500 - 버림 100 - 500, ZMax = 내부높이 2500 + 상부슬래브 250 + 500
        Assert.All(sections, s =>
        {
            Assert.Equal(-1100, s.ZMin);
            Assert.Equal(3250, s.ZMax);
        });
    }

    [Fact]
    public void CalculateSections_AirValveRoomIgnoresPlainConcrete()
    {
        var dto = new ValveRoomGeometryRequestDto(
            new ValveRoomDesignConditionDto("공기밸브실", 123.45),
            new ValveRoomPlanSpecDto(1500, 1800),
            new ValveRoomProfileSpecDto(2000, 100, 650, 300, 300, 400),
            null,
            null);

        var sections = ValveRoomGeometryCalculator.CalculateSections(dto);

        // halfX = 900 + 300 + 300 = 1500, halfY = 750 + 600 = 1350 (버림 확장 없음)
        Assert.Equal(new Point3D(900, 750, 0), sections[0].CutOrigin);
        Assert.Equal(2000, sections[0].HalfWidth);
        Assert.Equal(1850, sections[0].FarClipDepth);
        Assert.Equal(123450 - 650 - 500, sections[0].ZMin);
        Assert.Equal(123450 + 2400 + 500, sections[0].ZMax);
    }

    /// <summary>중간벽 + 중간슬래브를 모두 가진 이토밸브실. 1층 안목 2000, 중간슬래브 두께 300.</summary>
    private static ValveRoomGeometryRequestDto CreateMudValveRoomWithSlabDto() => new(
        new ValveRoomDesignConditionDto("이토밸브실", 0),
        new ValveRoomPlanSpecDto(2500, 4000),
        new ValveRoomProfileSpecDto(2500, 100, 500, 300, 300, 250),
        new MudValveRoomSpecDto(true, 1200, 300, true, 300, 2000, 2000),
        null);

    private static DrainPitDefinition Pit(double centerX, double centerY, double lengthX, double widthY) => new()
    {
        TopCenter = new Point3D(centerX, centerY, 0),
        LengthX = lengthX,
        WidthY = widthY,
        Depth = 1000
    };

    private static ValveRoomGeometryRequestDto CreateTieValveRoomDto() => new(
        new ValveRoomDesignConditionDto("타이밸브실", 0) { GeometryType = "이토밸브실" },
        new ValveRoomPlanSpecDto(2500, 4000),
        new ValveRoomProfileSpecDto(2500, 100, 500, 300, 300, 250),
        new MudValveRoomSpecDto(true, 1200, 300, false, 0, 0, 0),
        null);

    private static ValveRoomGeometryRequestDto CreateMudValveRoomDto(double intermediateWallOffset, double intermediateWallThickness) => new(
        new ValveRoomDesignConditionDto("이토밸브실", 0),
        new ValveRoomPlanSpecDto(2500, 4000),
        new ValveRoomProfileSpecDto(2500, 100, 500, 300, 300, 250),
        new MudValveRoomSpecDto(true, intermediateWallOffset, intermediateWallThickness, false, 0, 0, 0),
        null);

    private static ValveRoomGeometryRequestDto CreateSluiceValveRoomDto(double upperSlabThickness) => new(
        new ValveRoomDesignConditionDto("제수밸브실", 0),
        new ValveRoomPlanSpecDto(2000, 3000),
        new ValveRoomProfileSpecDto(2500, 100, 500, 300, 300, upperSlabThickness),
        null,
        new SluiceValveRoomSpecDto(1, 500, 0, 1, 500, 0, "Beam", "Column"));
}
