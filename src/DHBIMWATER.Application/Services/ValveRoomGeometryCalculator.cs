using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.Services;

public class ValveRoomGeometryCalculator
{
    public const string BaseLevelName = "밸브실 기초 상부";
    public const string IntermediateLevelName = "밸브실 중간슬래브";
    public const string TopLevelName = "밸브실 상부슬래브";
    public const string SectionAName = "A-A";
    public const string SectionBName = "B-B";
    private const double SectionMargin = 500; // 뷰 여유치(mm) — 펌프장 CalculateSectionViews의 offset과 같은 값

    /// <summary>기초 상부 EL(m)을 프로젝트 내부 단위(mm)로 환산한 기준 레벨 표고.</summary>
    public static double BaseElevation(ValveRoomGeometryRequestDto dto) => dto.DesignConditionDto.FoundationTopEl * 1000;

    public static double TopElevation(ValveRoomGeometryRequestDto dto) => BaseElevation(dto) + RoomHeight(dto);

    public static IReadOnlyList<LevelDefinition> CalculateLevels(ValveRoomGeometryRequestDto dto)
    {
        var levels = new List<LevelDefinition>
        {
            new LevelDefinition { Name = BaseLevelName, Elevation = BaseElevation(dto) },
        };
        // 이토밸브실 - 중간슬래브가 있으면 중간슬래브 상단(2F 바닥) 참고 레벨을 생성한다.
        if (dto.DesignConditionDto.GeometryType == "이토밸브실" && dto.MudSpec is { HasIntermediateSlab: true } mud)
            levels.Add(new LevelDefinition
            {
                Name = IntermediateLevelName,
                Elevation = BaseElevation(dto) + mud.Floor1InnerHeight + mud.IntermediateSlabThickness
            });
        levels.Add(new LevelDefinition { Name = TopLevelName, Elevation = TopElevation(dto) });
        return levels;
    }

    public static IReadOnlyList<SlabDefinition> CalculateSlabs(ValveRoomGeometryRequestDto dto)
    {
        var d = dto.DesignConditionDto;
        var pl = dto.PlanSpecDto;
        var pr = dto.ProfileSpecDto;
        // X축=길이(InnerLength), Y축=폭(InnerWidth). Slab(centerX, centerY, xExtent, yExtent, ...) 규약에 맞춰 X에 길이, Y에 폭을 넣는다.
        var outerX = pl.InnerLength + pr.OuterWallThickness * 2;   // X(길이) 방향 외벽 외곽
        var outerY = pl.InnerWidth + pr.OuterWallThickness * 2;    // Y(폭) 방향 외벽 외곽
        var foundationX = outerX + pr.FoundationToe * 2;
        var foundationY = outerY + pr.FoundationToe * 2;
        // 버림콘크리트는 기초보다 사방으로 버림 두께만큼 바깥으로 확장한다.
        var plainX = foundationX + pr.PlainConcreteThickness * 2;
        var plainY = foundationY + pr.PlainConcreteThickness * 2;
        var centerX = pl.InnerLength / 2;
        var centerY = pl.InnerWidth / 2;
        var baseZ = BaseElevation(dto);
        var slabs = new List<SlabDefinition>();
        if (d.GeometryType != "공기밸브실")
            slabs.Add(Slab(dto, centerX, centerY, plainX, plainY, pr.PlainConcreteThickness, baseZ - pr.FoundationThickness, "버림콘크리트"));
        if (d.GeometryType != "공기밸브실")
            slabs.Add(Slab(dto, centerX, centerY, foundationX, foundationY, pr.FoundationThickness, baseZ, "기초"));
        if (d.GeometryType == "이토밸브실" && dto.MudSpec is { HasIntermediateSlab: true } mud)
            slabs.Add(Slab(dto, centerX, centerY, outerX, outerY, mud.IntermediateSlabThickness, baseZ + mud.Floor1InnerHeight + mud.IntermediateSlabThickness, "중간슬래브"));
        slabs.Add(Slab(dto, centerX, centerY, outerX, outerY, pr.UpperSlabThickness, TopElevation(dto), "상부슬래브"));
        return slabs;
    }

    public static IReadOnlyList<FoundationDefinition> CalculateFoundations(ValveRoomGeometryRequestDto dto)
    {
        if (dto.DesignConditionDto.GeometryType != "공기밸브실") return Array.Empty<FoundationDefinition>();

        var plan = dto.PlanSpecDto;
        return new[]
        {
            new FoundationDefinition
            {
                Position = new Point3D(plan.InnerLength / 2, plan.InnerWidth / 2, BaseElevation(dto)),
                Length = plan.InnerLength + dto.ProfileSpecDto.OuterWallThickness * 2 + dto.ProfileSpecDto.FoundationToe * 2,
                Width = plan.InnerWidth + dto.ProfileSpecDto.OuterWallThickness * 2 + dto.ProfileSpecDto.FoundationToe * 2,
                Thickness = dto.ProfileSpecDto.FoundationThickness,
                ElementCode = "VR-F",
                Zone = dto.DesignConditionDto.RoomType,
                Part = "기초"
            }
        };
    }

    public static AirValveVoidPlacementDefinition CalculateAirValveVoid(ValveRoomGeometryRequestDto dto, AirValveRoomSpecDto air)
    {
        var plan = dto.PlanSpecDto;
        var isYAxis = air.VoidAxis.Equals("Y", StringComparison.OrdinalIgnoreCase);
        return new AirValveVoidPlacementDefinition(
            new Point3D(isYAxis ? plan.InnerLength / 2 : 0, isYAxis ? 0 : plan.InnerWidth / 2,
                BaseElevation(dto) - air.FoundationTopToPipeCenterDepth),
            air.MainPipeDiameter / 2,
            isYAxis ? "Y" : "X");
    }

    public static IReadOnlyList<LinearWallDefinition> CalculateWalls(ValveRoomGeometryRequestDto dto)
    {
        var d = dto.DesignConditionDto; var pl = dto.PlanSpecDto; var pr = dto.ProfileSpecDto;
        var t = pr.OuterWallThickness;
        var x0 = -t / 2; var x1 = pl.InnerLength + t / 2;   // 세로(좌우) 벽체 중심선 X (길이 방향)
        var y0 = -t / 2; var y1 = pl.InnerWidth + t / 2;    // 가로(상하) 벽체 중심선 Y (폭 방향)
        var hx0 = x0 - t / 2; var hx1 = x1 + t / 2;         // 가로 벽체 끝점 X (바깥으로)
        var vy0 = y0 + t / 2; var vy1 = y1 - t / 2;         // 세로 벽체 끝점 Y (안쪽으로)
        var walls = new List<LinearWallDefinition>();
        var outerCorners = new[]
        {
            (new Point3D(hx0, y0, 0), new Point3D(hx1, y0, 0)), (new Point3D(x1, vy0, 0), new Point3D(x1, vy1, 0)),
            (new Point3D(hx1, y1, 0), new Point3D(hx0, y1, 0)), (new Point3D(x0, vy1, 0), new Point3D(x0, vy0, 0))
        };
        if (d.GeometryType == "이토밸브실" && dto.MudSpec is { HasIntermediateSlab: true } slabMud)
        {
            foreach (var (start, end) in outerCorners)
            {
                walls.Add(Wall(dto, start, end, pr.OuterWallThickness, slabMud.Floor1InnerHeight, "외벽", isExterior: true));
                walls.Add(Wall(dto, start, end, pr.OuterWallThickness, slabMud.Floor2InnerHeight, "외벽", isExterior: true,
                    baseOffset: slabMud.Floor1InnerHeight + slabMud.IntermediateSlabThickness));
            }
        }
        else
        {
            foreach (var (start, end) in outerCorners) walls.Add(Wall(dto, start, end, pr.OuterWallThickness, RoomHeight(dto) - pr.UpperSlabThickness, "외벽", isExterior: true));
        }

        if (d.GeometryType != "이토밸브실" || dto.MudSpec is not { HasIntermediateWall: true } mud) return walls;
        // 중간벽 1개. 입력 offset은 좌측 외벽 내측(X=0)에서 중간벽 좌측면까지의 안목거리이므로 중심선으로 환산
        var mx = mud.IntermediateWallOffset + mud.IntermediateWallThickness / 2;
        var mStart = new Point3D(mx, 0, 0);
        var mEnd = new Point3D(mx, pl.InnerWidth, 0);
        if (dto.MudSpec is { HasIntermediateSlab: true } intermediateSlab)
        {
            walls.Add(Wall(dto, mStart, mEnd, mud.IntermediateWallThickness, intermediateSlab.Floor1InnerHeight, "중간벽", isExterior: false));
            walls.Add(Wall(dto, mStart, mEnd, mud.IntermediateWallThickness, intermediateSlab.Floor2InnerHeight, "중간벽", isExterior: false,
                baseOffset: intermediateSlab.Floor1InnerHeight + intermediateSlab.IntermediateSlabThickness));
        }
        else
        {
            walls.Add(Wall(dto, mStart, mEnd, mud.IntermediateWallThickness, RoomHeight(dto) - pr.UpperSlabThickness, "중간벽", isExterior: false));
        }
        return walls;
    }

    public static IReadOnlyList<BeamDefinition> CalculateBeams(ValveRoomGeometryRequestDto dto)
    {
        var d = dto.DesignConditionDto; var pl = dto.PlanSpecDto;
        if (d.GeometryType != "제수밸브실" || dto.SluiceSpec is not { } sluice) return Array.Empty<BeamDefinition>();
        var z = TopElevation(dto); var x0 = 0d; var x1 = pl.InnerLength;   // X방향 보는 길이(X) 전 구간을 가로지른다.
        var y0 = 0d; var y1 = pl.InnerWidth;                              // Y방향 보는 폭(Y) 전 구간을 가로지른다.
        // 기둥 자리는 비운다. 기둥 단면을 못 구하면 목록이 비어 종전처럼 통짜 보가 나온다(docs/202 § D-2).
        // 보는 헌치와 달리 진행 방향에 의미가 없어(Beam()은 CrossSectionRotationDeg를 쓰지 않는다) 토막은 항상 오름차순이다.
        var columns = ColumnBlockers(dto).ToList();
        var beams = new List<BeamDefinition>();
        for (var i = 0; i < sluice.BeamCountX; i++)
        {
            var y = y0 + sluice.BeamOffsetX + sluice.BeamSpacingX * i;
            // 이 보의 중심선(y)을 물고 있는 기둥만 X구간으로 바꿔 뺀다.
            var blocks = columns.Where(c => c.MinY <= y && c.MaxY >= y).Select(c => (c.MinX, c.MaxX));
            foreach (var (min, max) in SubtractBlocks(x0, x1, blocks))
            {
                if (max - min < MinBeamSegmentLengthMm) continue;
                beams.Add(Beam(dto, new Point3D(min, y, z), new Point3D(max, y, z), "X방향 보"));
            }
        }
        for (var i = 0; i < sluice.BeamCountY; i++)
        {
            var x = x0 + sluice.BeamOffsetY + sluice.BeamSpacingY * i;
            var blocks = columns.Where(c => c.MinX <= x && c.MaxX >= x).Select(c => (c.MinY, c.MaxY));
            foreach (var (min, max) in SubtractBlocks(y0, y1, blocks))
            {
                if (max - min < MinBeamSegmentLengthMm) continue;
                beams.Add(Beam(dto, new Point3D(x, min, z), new Point3D(x, max, z), "Y방향 보"));
            }
        }
        return beams;
    }

    /// <summary>장애물이 벽면에 닿았다고 볼 허용오차(mm). 코너 배치 피트·중간벽은 벽면과 정확히 맞물리지만
    /// 좌표 반올림으로 미세하게 벌어져도 같은 자리로 본다.</summary>
    private const double HaunchPitTouchToleranceMm = 1;

    /// <summary>장애물에 먹히고 남은 헌치 토막의 최소 길이(mm). 이보다 짧으면 배치하지 않는다.</summary>
    private const double MinHaunchSegmentLengthMm = 10;

    /// <summary>기둥에 먹히고 남은 보 토막의 최소 길이(mm). 이보다 짧으면 배치하지 않는다.</summary>
    private const double MinBeamSegmentLengthMm = 10;

    /// <summary>헌치를 끊는 XY 평면 축정렬 사각형. 드레인 피트·제수밸브실 보·이토실 중간벽을 같은 규칙으로 다룬다(docs/162 § C-1).</summary>
    private readonly record struct HaunchBlocker(double MinX, double MaxX, double MinY, double MaxY);

    /// <summary>벽-슬래브 접합부 헌치. 외벽 4면 내측과 이토실 중간벽 양측을 따라 배치한다(docs/153 § A).
    /// 레벨은 기초 상단(VR-H2)·중간슬래브 하단(VR-H3, 중간슬래브가 있을 때만)·상부슬래브 하단(VR-H1)이다.
    /// 중간슬래브 <b>상면</b>(2F 바닥)에는 넣지 않는다.
    /// 단면 유형은 UseCase가 해석한다 — TypeName이 비면 이름 기반 자동 탐색(FindHaunchBeamSymbol)을 쓴다.
    /// 레벨마다 그 자리를 차지하는 장애물 구간을 비운다(<see cref="AddHaunchSegment"/>, docs/162 § B-1).</summary>
    public static IReadOnlyList<BeamDefinition> CalculateHaunches(ValveRoomGeometryRequestDto dto)
    {
        var haunches = new List<BeamDefinition>();
        // 중간벽은 기초까지 내려오므로 모든 레벨의 외벽 헌치를 끊는다.
        var wall = IntermediateWallBlockers(dto).ToList();
        if (dto.HasLowerHaunch)
            AddHaunchRun(dto, haunches, BaseElevation(dto), BaseLevelName, "VR-H2",
                PitBlockers(dto).Concat(wall).ToList(), rotationDeg: 0);
        if (!dto.HasUpperHaunch) return haunches;
        // 중간슬래브 하단 헌치. 레벨 표고는 슬래브 상면이라 두께를 뺀 1층 안목 상단이 헌치 자리다(docs/162 § B-2).
        if (dto.DesignConditionDto.GeometryType == "이토밸브실" && dto.MudSpec is { HasIntermediateSlab: true } mud)
            AddHaunchRun(dto, haunches, BaseElevation(dto) + mud.Floor1InnerHeight, IntermediateLevelName, "VR-H3", wall, rotationDeg: 90);
        // 피트는 기초만 파고들므로 상부슬래브 하단 헌치는 피트로 끊지 않는다. 대신 제수밸브실 보가 끊는다.
        // 상단 헌치는 상부슬래브 하단에 매달리므로 단면을 90도 돌려 세운다(docs/160).
        AddHaunchRun(dto, haunches, TopElevation(dto) - dto.ProfileSpecDto.UpperSlabThickness, TopLevelName, "VR-H1",
            BeamBlockers(dto).Concat(wall).ToList(), rotationDeg: 90);
        return haunches;
    }

    /// <summary>기초 상면을 파는 드레인 피트를 장애물로 바꾼다.</summary>
    private static IEnumerable<HaunchBlocker> PitBlockers(ValveRoomGeometryRequestDto dto) =>
        dto.DrainPits.Select(pit => new HaunchBlocker(
            pit.TopCenter.X - pit.LengthX / 2, pit.TopCenter.X + pit.LengthX / 2,
            pit.TopCenter.Y - pit.WidthY / 2, pit.TopCenter.Y + pit.WidthY / 2));

    /// <summary>제수밸브실 보를 장애물로 바꾼다. 보는 <see cref="CalculateBeams"/>와 같은 위치·범위를 쓰며
    /// 폭만 Revit 유형에서 실측한 값(<see cref="ValveRoomGeometryRequestDto.SluiceBeamWidth"/>)이다.
    /// 폭을 못 구하면 빈 목록 — 종전처럼 통짜 헌치가 된다(docs/162 § C-3).
    /// <para>보가 기둥 자리에서 토막나도(docs/202) 여기는 <b>전 구간</b>을 그대로 쓴다 —
    /// 벽에 닿는 끝 토막이 여전히 벽까지 닿으므로 헌치를 끊는 자리는 바뀌지 않는다.</para></summary>
    private static IEnumerable<HaunchBlocker> BeamBlockers(ValveRoomGeometryRequestDto dto)
    {
        if (dto.DesignConditionDto.GeometryType != "제수밸브실" || dto.SluiceSpec is not { } sluice) yield break;
        if (dto.SluiceBeamWidth <= 0) yield break;
        var pl = dto.PlanSpecDto;
        var half = dto.SluiceBeamWidth / 2;
        for (var i = 0; i < sluice.BeamCountX; i++)
        {
            var y = sluice.BeamOffsetX + sluice.BeamSpacingX * i;
            yield return new HaunchBlocker(0, pl.InnerLength, y - half, y + half);
        }
        for (var i = 0; i < sluice.BeamCountY; i++)
        {
            var x = sluice.BeamOffsetY + sluice.BeamSpacingY * i;
            yield return new HaunchBlocker(x - half, x + half, 0, pl.InnerWidth);
        }
    }

    /// <summary>제수밸브실 기둥을 장애물로 바꾼다. 위치는 <see cref="CalculateColumns"/>와 같은 식이며
    /// 단면 치수만 Revit 유형에서 실측한 값(<see cref="ValveRoomGeometryRequestDto.SluiceColumnWidthX"/>·
    /// <see cref="ValveRoomGeometryRequestDto.SluiceColumnDepthY"/>)이다.
    /// 치수를 못 구하면 빈 목록 — 종전처럼 통짜 보가 된다(docs/202 § D-2).
    /// <para>헌치는 이 장애물을 쓰지 않는다 — 기둥은 실내부에 서므로 벽면 헌치와 무관하다(docs/202 § D-5).</para></summary>
    private static IEnumerable<HaunchBlocker> ColumnBlockers(ValveRoomGeometryRequestDto dto)
    {
        if (dto.DesignConditionDto.GeometryType != "제수밸브실" || dto.SluiceSpec is not { } sluice) yield break;
        if (dto.SluiceColumnWidthX <= 0 || dto.SluiceColumnDepthY <= 0) yield break;
        var halfX = dto.SluiceColumnWidthX / 2;
        var halfY = dto.SluiceColumnDepthY / 2;
        for (var i = 0; i < sluice.BeamCountY; i++)
        {
            var x = sluice.BeamOffsetY + sluice.BeamSpacingY * i;
            for (var j = 0; j < sluice.BeamCountX; j++)
            {
                var y = sluice.BeamOffsetX + sluice.BeamSpacingX * j;
                yield return new HaunchBlocker(x - halfX, x + halfX, y - halfY, y + halfY);
            }
        }
    }

    /// <summary>이토실 중간벽을 장애물로 바꾼다. 중간벽 자신의 양측면 헌치는 <see cref="AddHaunchSegment"/>의
    /// 방향 판정 덕에 잘리지 않는다 — 벽 몸통이 그 면의 실내 쪽이 아니기 때문이다(docs/162 § C-2).</summary>
    private static IEnumerable<HaunchBlocker> IntermediateWallBlockers(ValveRoomGeometryRequestDto dto)
    {
        if (dto.DesignConditionDto.GeometryType != "이토밸브실" || dto.MudSpec is not { HasIntermediateWall: true } mud)
            return Array.Empty<HaunchBlocker>();
        var mx = mud.IntermediateWallOffset + mud.IntermediateWallThickness / 2;
        var half = mud.IntermediateWallThickness / 2;
        return new[] { new HaunchBlocker(mx - half, mx + half, 0, dto.PlanSpecDto.InnerWidth) };
    }

    /// <summary>레벨 하나분 헌치를 점열에서 구간별로 만든다. 진행 방향 왼쪽이 항상 실내 공간이 되도록 점 순서를 잡는다 —
    /// Revit에서 헌치 단면이 반대로 누우면 해당 구간의 시작·끝점을 뒤집으면 된다(docs/153 § A-3).</summary>
    /// <param name="blockers">이 레벨에서 헌치를 끊을 장애물. 비우면 구간을 통짜로 만든다.</param>
    /// <param name="rotationDeg">이 레벨 헌치의 단면 회전(도). Revit 기본 자세 그대로면 0.</param>
    private static void AddHaunchRun(ValveRoomGeometryRequestDto dto, ICollection<BeamDefinition> haunches,
        double z, string levelName, string elementCode, IReadOnlyList<HaunchBlocker> blockers, double rotationDeg)
    {
        var pl = dto.PlanSpecDto;
        // 외벽 4면 내측. 반시계 방향 폐합 사각형이라 각 변의 왼쪽이 실내다.
        var outer = new[]
        {
            new Point3D(0, 0, z),
            new Point3D(pl.InnerLength, 0, z),
            new Point3D(pl.InnerLength, pl.InnerWidth, z),
            new Point3D(0, pl.InnerWidth, z),
            new Point3D(0, 0, z)
        };
        for (var i = 0; i < outer.Length - 1; i++)
            AddHaunchSegment(dto, haunches, outer[i], outer[i + 1], levelName, elementCode, blockers, rotationDeg);

        if (dto.DesignConditionDto.GeometryType != "이토밸브실" || dto.MudSpec is not { HasIntermediateWall: true } mud) return;
        // 중간벽 양측면. 좌측면은 +Y, 우측면은 -Y로 진행해야 각각의 왼쪽이 그 면이 접한 실내가 된다.
        var mx = mud.IntermediateWallOffset + mud.IntermediateWallThickness / 2;
        var half = mud.IntermediateWallThickness / 2;
        AddHaunchSegment(dto, haunches, new Point3D(mx - half, 0, z), new Point3D(mx - half, pl.InnerWidth, z), levelName, elementCode, blockers, rotationDeg);
        AddHaunchSegment(dto, haunches, new Point3D(mx + half, pl.InnerWidth, z), new Point3D(mx + half, 0, z), levelName, elementCode, blockers, rotationDeg);
    }

    /// <summary>헌치 1구간을 만들되, 그 벽면을 실내 쪽으로 파고드는 장애물이 물고 있는 구간은 비운다.
    /// 드레인 피트는 기초 상면을, 제수밸브실 보와 이토실 중간벽은 헌치가 앉을 자리를 차지해 버리기 때문이다.
    /// 벽면에서 안쪽으로 떨어진 장애물은 헌치를 끊지 않는다. 남는 토막은 원래 구간의 진행 방향(왼쪽이 실내)을 그대로 유지한다.</summary>
    private static void AddHaunchSegment(ValveRoomGeometryRequestDto dto, ICollection<BeamDefinition> haunches,
        Point3D start, Point3D end, string levelName, string elementCode, IReadOnlyList<HaunchBlocker> blockers, double rotationDeg)
    {
        // 헌치 구간은 항상 축에 나란하다. 변화가 큰 축을 진행축으로 본다.
        var alongX = Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y);
        var from = alongX ? start.X : start.Y;        // 진행축 시작 좌표
        var to = alongX ? end.X : end.Y;              // 진행축 끝 좌표
        var faceCoord = alongX ? start.Y : start.X;   // 이 구간이 놓인 벽면의 직교축 좌표
        var forward = to >= from;
        // 헌치는 진행 방향 왼쪽(= 실내)에만 살을 붙인다. +X 진행이면 왼쪽은 +Y, -X면 -Y, +Y면 -X, -Y면 +X다.
        // 이 부호를 보지 않고 "벽면에 닿기만 하면 끊는" 대칭 판정을 쓰면 중간벽이 자기 양측면 헌치를
        // 통째로 지워 버린다 — 벽 몸통은 그 면의 실내 쪽이 아니다(docs/162 § C-2).
        var interiorSign = alongX ? (forward ? 1 : -1) : (forward ? -1 : 1);

        // 벽면에 닿고 실내 쪽으로 파고드는 장애물만 진행축 구간(Min~Max)으로 바꿔 모은다.
        var blocks = new List<(double Min, double Max)>();
        foreach (var blocker in blockers)
        {
            var alongMin = alongX ? blocker.MinX : blocker.MinY;
            var alongMax = alongX ? blocker.MaxX : blocker.MaxY;
            if (alongMax - alongMin <= 0) continue;   // 폭 0짜리(조회 실패 등)는 끊지 않는다.
            var acrossMin = alongX ? blocker.MinY : blocker.MinX;
            var acrossMax = alongX ? blocker.MaxY : blocker.MaxX;
            var reaches = interiorSign > 0
                ? acrossMin <= faceCoord + HaunchPitTouchToleranceMm && acrossMax > faceCoord + HaunchPitTouchToleranceMm
                : acrossMax >= faceCoord - HaunchPitTouchToleranceMm && acrossMin < faceCoord - HaunchPitTouchToleranceMm;
            if (!reaches) continue;
            blocks.Add((alongMin, alongMax));
        }

        // 오름차순 좌표계에서 장애물 구간을 빼고 남은 토막만 추린다.
        var kept = SubtractBlocks(Math.Min(from, to), Math.Max(from, to), blocks);

        if (!forward) kept.Reverse();
        foreach (var (min, max) in kept)
        {
            if (max - min < MinHaunchSegmentLengthMm) continue;
            var s = forward ? min : max;
            var e = forward ? max : min;
            haunches.Add(Haunch(dto,
                alongX ? new Point3D(s, faceCoord, start.Z) : new Point3D(faceCoord, s, start.Z),
                alongX ? new Point3D(e, faceCoord, start.Z) : new Point3D(faceCoord, e, start.Z),
                levelName, elementCode, rotationDeg));
        }
    }

    /// <summary>[lo, hi] 구간에서 장애물 구간들을 빼고 남은 토막을 오름차순으로 돌려준다.
    /// 헌치(장애물 = 피트·보·중간벽)와 제수밸브실 보(장애물 = 기둥)가 공용한다(docs/202 § D-1).</summary>
    private static List<(double Min, double Max)> SubtractBlocks(
        double lo, double hi, IEnumerable<(double Min, double Max)> blocks)
    {
        var kept = new List<(double Min, double Max)>();
        var cursor = lo;
        foreach (var (min, max) in blocks.OrderBy(b => b.Min))
        {
            if (max <= lo || min >= hi) continue;   // 이 구간을 벗어난 장애물
            if (min > cursor) kept.Add((cursor, min));
            cursor = Math.Max(cursor, max);
            if (cursor >= hi) break;
        }
        if (cursor < hi) kept.Add((cursor, hi));
        return kept;
    }

    public static IReadOnlyList<ColumnDefinition> CalculateColumns(ValveRoomGeometryRequestDto dto)
    {
        var d = dto.DesignConditionDto;
        if (d.GeometryType != "제수밸브실" || dto.SluiceSpec is not { } sluice) return Array.Empty<ColumnDefinition>();
        var columns = new List<ColumnDefinition>();
        for (var x = 0; x < sluice.BeamCountY; x++)
            for (var y = 0; y < sluice.BeamCountX; y++)
                columns.Add(new ColumnDefinition
                {
                    Position = new Point3D(sluice.BeamOffsetY + sluice.BeamSpacingY * x, sluice.BeamOffsetX + sluice.BeamSpacingX * y, BaseElevation(dto)),
                    TypeName = sluice.ColumnTypeName,
                    BaseLevelName = BaseLevelName,
                    TopLevelName = TopLevelName,
                    // 상단은 상부슬래브 레벨에 구속하되, 슬래브 두께만큼 내려 슬래브 하부에 맞춘다.
                    TopOffset = -dto.ProfileSpecDto.UpperSlabThickness,
                    ElementCode = "VR-C",
                    Zone = d.RoomType,
                    Part = "보 교차부 기둥"
                });
        return columns;
    }

    /// <summary>기초 중앙을 지나는 단면 A-A(X축 평행, +Y를 바라봄) · B-B(Y축 평행, +X를 바라봄)(docs/246).
    /// <para>깊이는 중앙에서 버림콘크리트 바깥 + 여유치까지, 높이는 버림 하단 - 여유치 ~ 상부슬래브 상단 + 여유치.
    /// 공기밸브실은 버림 슬래브가 없으므로(<see cref="CalculateSlabs"/>) 버림 확장을 빼고 계산한다.</para>
    /// <para>공기밸브 관통 Void 깊이는 AirSpec이 이 DTO 밖에 있어 반영하지 않는다(docs/246 § 5).</para></summary>
    public static IReadOnlyList<SectionCutDefinition> CalculateSections(ValveRoomGeometryRequestDto dto)
    {
        var pl = dto.PlanSpecDto;
        var pr = dto.ProfileSpecDto;
        var plainExt = dto.DesignConditionDto.GeometryType == "공기밸브실" ? 0 : pr.PlainConcreteThickness;
        var halfX = pl.InnerLength / 2 + pr.OuterWallThickness + pr.FoundationToe + plainExt;
        var halfY = pl.InnerWidth / 2 + pr.OuterWallThickness + pr.FoundationToe + plainExt;
        var origin = new Point3D(pl.InnerLength / 2, pl.InnerWidth / 2, 0);
        var zMin = BaseElevation(dto) - pr.FoundationThickness - plainExt - SectionMargin;
        var zMax = TopElevation(dto) + SectionMargin;

        return new[]
        {
            // 화면 오른쪽 +X → 바라보는 방향 (+Z) × (+X) = +Y.
            new SectionCutDefinition
            {
                Name = SectionAName, CutOrigin = origin, Right = new Vector3D(1, 0, 0),
                HalfWidth = halfX + SectionMargin, ZMin = zMin, ZMax = zMax, FarClipDepth = halfY + SectionMargin
            },
            // 화면 오른쪽 -Y → 바라보는 방향 (+Z) × (-Y) = +X.
            new SectionCutDefinition
            {
                Name = SectionBName, CutOrigin = origin, Right = new Vector3D(0, -1, 0),
                HalfWidth = halfY + SectionMargin, ZMin = zMin, ZMax = zMax, FarClipDepth = halfX + SectionMargin
            },
        };
    }

    /// <summary>제수밸브실 보 개수 상한(방향별). 큰 값이 들어가면 보 N² 배치·Z맞춤 재생성이 반복돼 멈춘 것처럼 보인다(docs/238 § D).</summary>
    public const int MaxBeamCountPerDirection = 20;

    /// <summary>보 배치가 실내에 들어오는지(docs/238 § D). 문제가 없으면 null.
    /// <para>X방향 보(개수 X)는 y = OffsetX + SpacingX×i 에 놓여 (0, 내부 폭) 안이어야 하고,
    /// Y방향 보(개수 Y)는 x = OffsetY + SpacingY×i 에 놓여 (0, 내부 길이) 안이어야 한다 — <see cref="CalculateBeams"/>와 같은 식이다.
    /// 밖이면 보와 교차 기둥이 외벽 밖에 생긴다. ViewModel도 같은 검사를 쓴다.</para></summary>
    public static string? BeamLayoutError(int countX, double offsetX, double spacingX, int countY, double offsetY, double spacingY,
        double innerWidth, double innerLength)
    {
        if (countX > MaxBeamCountPerDirection || countY > MaxBeamCountPerDirection)
            return $"보 개수는 방향별 {MaxBeamCountPerDirection}개 이하여야 합니다.";
        if (countX > 0 && (offsetX <= 0 || offsetX + spacingX * (countX - 1) >= innerWidth))
            return "X방향 보(Ox·Sx)가 내부 폭 범위를 벗어납니다.";
        if (countY > 0 && (offsetY <= 0 || offsetY + spacingY * (countY - 1) >= innerLength))
            return "Y방향 보(Oy·Sy)가 내부 길이 범위를 벗어납니다.";
        return null;
    }

    public static void Validate(ValveRoomGeometryRequestDto dto)
    {
        var d = dto.DesignConditionDto; var pl = dto.PlanSpecDto; var pr = dto.ProfileSpecDto;
        // RoomHeight(dto)가 MudSpec에 의존하므로 스펙 존재 검사를 치수 검사보다 먼저 수행한다.
        if (d.GeometryType == "이토밸브실" && dto.MudSpec is null)
            throw new DhbimwaterException(ErrorCodes.VR_MudSpecRequired, "이토밸브실은 중간벽·중간슬래브 입력이 필요합니다.");
        if (pl.InnerWidth <= 0 || pl.InnerLength <= 0 || RoomHeight(dto) <= 0 || pr.FoundationThickness <= 0 || pr.OuterWallThickness <= 0)
            throw new DhbimwaterException(ErrorCodes.VR_InvalidDimensions, "밸브실 치수와 두께는 0보다 커야 합니다.");
        // 중간슬래브가 없으면 RoomHeight = InnerHeight + 상부슬래브라 InnerHeight가 0이어도 통과했다.
        // 벽 높이는 RoomHeight − UpperSlabThickness이므로 이 값이 0이면 Wall.Create가 던진다(docs/207 § 0-1).
        if (RoomHeight(dto) - pr.UpperSlabThickness <= 0)
            throw new DhbimwaterException(ErrorCodes.VR_InvalidWallHeight, "내부 높이(벽 높이)는 0보다 커야 합니다.");
        if (pr.UpperSlabThickness <= 0 || pr.PlainConcreteThickness < 0 || pr.FoundationToe < 0)
            throw new DhbimwaterException(ErrorCodes.VR_InvalidDimensions, "밸브실 치수와 두께는 0보다 커야 합니다.");
        // 버림콘크리트를 만드는 유형(공기밸브실 외)은 두께 0이면 두께 0 바닥 유형을 만들다 Revit이 던진다(docs/238 § D).
        if (d.GeometryType != "공기밸브실" && pr.PlainConcreteThickness <= 0)
            throw new DhbimwaterException(ErrorCodes.VR_InvalidDimensions, "버림콘크리트 두께는 0보다 커야 합니다.");
        if (dto.MudSpec is { } mudSpec)
        {
            if (mudSpec.HasIntermediateWall && (mudSpec.IntermediateWallThickness <= 0 || mudSpec.IntermediateWallOffset < 0
                || mudSpec.IntermediateWallOffset + mudSpec.IntermediateWallThickness > pl.InnerLength))
                throw new DhbimwaterException(ErrorCodes.VR_IntermediateWallOutside, "중간벽 위치와 두께가 내부 길이 범위를 벗어납니다.");
            if (mudSpec.HasIntermediateSlab && mudSpec.IntermediateSlabThickness <= 0)
                throw new DhbimwaterException(ErrorCodes.VR_InvalidDimensions, "밸브실 치수와 두께는 0보다 커야 합니다.");
        }
        if (dto.SluiceSpec is { } sluice
            && ((sluice.BeamCountX >= 2 && sluice.BeamSpacingX <= 0) || (sluice.BeamCountY >= 2 && sluice.BeamSpacingY <= 0)))
            throw new DhbimwaterException(ErrorCodes.VR_InvalidBeamLayout, "보 개수가 2 이상이면 보 간격은 0보다 커야 합니다.");
        if (d.GeometryType == "제수밸브실" && dto.SluiceSpec is { } layout
            && BeamLayoutError(layout.BeamCountX, layout.BeamOffsetX, layout.BeamSpacingX,
                layout.BeamCountY, layout.BeamOffsetY, layout.BeamSpacingY, pl.InnerWidth, pl.InnerLength) is { } beamError)
            throw new DhbimwaterException(ErrorCodes.VR_InvalidBeamLayout, beamError);
        if (d.GeometryType == "제수밸브실" && (dto.SluiceSpec is null || string.IsNullOrWhiteSpace(dto.SluiceSpec.BeamTypeName) || string.IsNullOrWhiteSpace(dto.SluiceSpec.ColumnTypeName)))
            throw new DhbimwaterException(ErrorCodes.VR_SluiceTypesRequired, "제수밸브실은 보와 기둥 유형을 선택해야 합니다.");
        foreach (var pit in dto.DrainPits)
        {
            if (pit.LengthX <= 0 || pit.WidthY <= 0 || pit.Depth <= 0)
                throw new DhbimwaterException(ErrorCodes.VR_InvalidDrainPit, "드레인 피트 길이·폭·깊이는 0보다 커야 합니다.");
            if (!DrainPitGeometry.IsInsideBounds(pit, 0, 0, pl.InnerLength, pl.InnerWidth))
                throw new DhbimwaterException(ErrorCodes.VR_DrainPitOutside, "드레인 피트는 내부공간 범위 안에 완전히 들어와야 합니다.");
        }
        for (var i = 0; i < dto.DrainPits.Count; i++)
            for (var j = i + 1; j < dto.DrainPits.Count; j++)
                if (Overlaps(dto.DrainPits[i], dto.DrainPits[j]))
                    throw new DhbimwaterException(ErrorCodes.VR_DrainPitOverlap, "드레인 피트끼리 XY 평면에서 겹칠 수 없습니다.");
    }

    private static SlabDefinition Slab(ValveRoomGeometryRequestDto dto, double centerX, double centerY, double width, double length, double thickness, double z, string part) => new()
    {
        Points = Rectangle(centerX, centerY, width, length),
        SubPoints = Array.Empty<Point2D>(),
        Thickness = thickness,
        ElevationZ = z,
        LevelName = BaseLevelName,
        Category = "슬래브",
        ElementCode = "VR-S",
        Zone = dto.DesignConditionDto.RoomType,
        Part = part
    };

    private static LinearWallDefinition Wall(ValveRoomGeometryRequestDto dto, Point3D start, Point3D end, double thickness, double height, string part, bool isExterior, double? baseOffset = null) => new()
    {
        StartPoint = start,
        EndPoint = end,
        Thickness = thickness,
        Height = height,
        BaseOffset = baseOffset ?? 0,
        LevelName = BaseLevelName,
        Category = "벽",
        ElementCode = "VR-W",
        Zone = dto.DesignConditionDto.RoomType,
        Part = part,
        IsExterior = isExterior
    };

    private static BeamDefinition Beam(ValveRoomGeometryRequestDto dto, Point3D start, Point3D end, string part) => new()
    {
        StartPoint = start,
        EndPoint = end,
        TypeName = dto.SluiceSpec!.BeamTypeName,
        LevelName = BaseLevelName,
        Category = "보",
        ElementCode = "VR-B",
        Zone = dto.DesignConditionDto.RoomType,
        Part = part
    };

    /// <summary>헌치 보 1구간. Beam()과 달리 SluiceSpec에 의존하지 않는다 — 제수밸브실이 아니어도 만들어야 한다.
    /// ElementCode를 "VR-B"로 두면 RevitBeamCommandRepo가 보 전용 Z오프셋·슬래브 결합을 걸어버리므로 VR-H1/VR-H2/VR-H3을 쓴다.</summary>
    private static BeamDefinition Haunch(ValveRoomGeometryRequestDto dto, Point3D start, Point3D end, string levelName, string elementCode, double rotationDeg) => new()
    {
        StartPoint = start,
        EndPoint = end,
        TypeName = dto.HaunchTypeName,
        LevelName = levelName,
        Category = "보",
        ElementCode = elementCode,
        Zone = dto.DesignConditionDto.RoomType,
        Part = "HAUNCH",
        CrossSectionRotationDeg = rotationDeg
    };

    private static IReadOnlyList<Point2D> Rectangle(double x, double y, double width, double length) =>
            new[] { new Point2D(x - width / 2, y - length / 2), new Point2D(x + width / 2, y - length / 2), new Point2D(x + width / 2, y + length / 2), new Point2D(x - width / 2, y + length / 2) };

    private static bool Overlaps(DrainPitDefinition left, DrainPitDefinition right) =>
        left.TopCenter.X - left.LengthX / 2 < right.TopCenter.X + right.LengthX / 2 &&
        left.TopCenter.X + left.LengthX / 2 > right.TopCenter.X - right.LengthX / 2 &&
        left.TopCenter.Y - left.WidthY / 2 < right.TopCenter.Y + right.WidthY / 2 &&
        left.TopCenter.Y + left.WidthY / 2 > right.TopCenter.Y - right.WidthY / 2;

    // 기초 상단~상부슬래브 상단 높이. 입력 내부 높이는 상부슬래브 하단까지이며, 중간슬래브가 있으면 1F 안목 + 중간슬래브 두께 + 2F 안목 + 상부슬래브 두께.
    private static double RoomHeight(ValveRoomGeometryRequestDto dto) =>
        dto.MudSpec is { HasIntermediateSlab: true } mud
            ? mud.Floor1InnerHeight + mud.IntermediateSlabThickness + mud.Floor2InnerHeight + dto.ProfileSpecDto.UpperSlabThickness
            : dto.ProfileSpecDto.InnerHeight + dto.ProfileSpecDto.UpperSlabThickness;
}
