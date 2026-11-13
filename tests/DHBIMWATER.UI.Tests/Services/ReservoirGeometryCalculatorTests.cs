using DHBIMWATER.Application.DTOs.Revit.Reservoir;
using DHBIMWATER.Application.Services;
using DHBIMWATER.Core.Structures;
using Xunit;

namespace DHBIMWATER.UI.Tests.Services;

public class ReservoirGeometryCalculatorTests
{
    [Fact]
    public void CalculateGenericModels_ReturnsFaultingConcreteAndPitDefinitions()
    {
        var dto = CreateReservoirDto();

        var defs = ReservoirGeometryCalculator.CalculateGenericModels(dto);

        Assert.Equal(2, defs.Count);

        var faulting = defs.Single(d => d.ElementCode == "L3");
        Assert.Equal("DH_단차버림콘크리트", faulting.SymbolName);
        Assert.Equal("Body", faulting.Class);
        Assert.Equal("슬래브", faulting.Category);
        Assert.Equal("배관실", faulting.Zone);
        Assert.Equal("단차_버림콘크리트", faulting.Part);
        Assert.Equal(25175, faulting.Origin.X);
        Assert.Equal(-50, faulting.Origin.Y);
        // 레거시처럼 Z=0 비구조 배치 후 레벨로부터 높이로 내림(docs/289 § G)
        Assert.Equal(0, faulting.Origin.Z);
        Assert.True(faulting.IsNonStructural);
        Assert.Equal(-600, faulting.ElevationFromLevel);
        Assert.Equal(ReservoirGeometryCalculator.TankFoundLevelName, faulting.LevelName);
        Assert.Equal(12325, Assert.IsType<double>(faulting.Parameters["L1"]));
        Assert.Equal(2800, Assert.IsType<double>(faulting.Parameters["L2"]));
        Assert.Equal(5950, Assert.IsType<double>(faulting.Parameters["L3"]));
        Assert.Equal(2100, Assert.IsType<double>(faulting.Parameters["W1"]));
        Assert.Equal(100, Assert.IsType<double>(faulting.Parameters["W2"]));
        Assert.Equal(2000, Assert.IsType<double>(faulting.Parameters["H"]));
        Assert.Equal(new Dimensionless(1.0), Assert.IsType<Dimensionless>(faulting.Parameters["사면비"]));

        var pit = defs.Single(d => d.ElementCode == "P1");
        Assert.Equal("DH_배수지피트", pit.SymbolName);
        Assert.Equal("Body", pit.Class);
        Assert.Equal("PIT", pit.Category);
        Assert.Equal("배관실", pit.Zone);
        Assert.Equal("PIT", pit.Part);
        Assert.Equal(10675, pit.Origin.X);
        Assert.Equal(-4850, pit.Origin.Y);
        Assert.Equal(0, pit.Origin.Z);
        Assert.Equal(ReservoirGeometryCalculator.ValveFoundLevelName, pit.LevelName);
        Assert.Equal(1000, Assert.IsType<double>(pit.Parameters["드레인피트_내부_가로폭"]));
        Assert.Equal(1000, Assert.IsType<double>(pit.Parameters["드레인피트_내부_세로폭"]));
        Assert.Equal(1000, Assert.IsType<double>(pit.Parameters["드레인피트_내부_깊이"]));
        Assert.Equal(300, Assert.IsType<double>(pit.Parameters["드레인피트_벽체두께"]));
        Assert.Equal(300, Assert.IsType<double>(pit.Parameters["드레인피트_바닥두께"]));
        Assert.Equal(100, Assert.IsType<double>(pit.Parameters["드레인피트_버림콘크리트_두께"]));
        Assert.Equal(500, Assert.IsType<double>(pit.Parameters["밸브실_바닥슬라브두께"]));
    }

    [Fact]
    public void CalculateSlabs_UsesValveExteriorWallThicknessForB4AndL4YExtent()
    {
        var dto = CreateReservoirDto();

        var slabs = ReservoirGeometryCalculator.CalculateSlabs(dto);

        var b4 = slabs.Single(s => s.ElementCode == "B4");
        Assert.Equal(-5600, b4.Points.Min(p => p.Y));
        Assert.Equal(0, b4.Points.Max(p => p.Y));

        var l4 = slabs.Single(s => s.ElementCode == "L4");
        Assert.Equal(-5700, l4.Points.Min(p => p.Y));
        Assert.Equal(0, l4.Points.Max(p => p.Y));
    }
    // docs/289 § B: 레거시 RH:2156-2310 상부 구속·기준 레벨
    [Fact]
    public void CalculateLinearWalls_ConstrainsTopsLikeLegacy()
    {
        var dto = CreateReservoirDto();

        var walls = ReservoirGeometryCalculator.CalculateLinearWalls(dto);

        Assert.All(walls.Where(w => w.ElementCode is "W1" or "W2" or "W3" or "W4" or "W5" or "W6"), w =>
        {
            Assert.Equal(ReservoirGeometryCalculator.TankUpperLevelName, w.TopLevelName);
            Assert.Equal(-300, w.TopOffset);
        });
        Assert.All(walls.Where(w => w.ElementCode is "W7" or "W8" or "W9" or "W10"), w =>
        {
            Assert.Equal(ReservoirGeometryCalculator.TankUpperLevelName, w.TopLevelName);
            Assert.Equal(-dto.ThicknessDto.SvuThk, w.TopOffset);
        });
        Assert.All(walls.Where(w => w.ElementCode == "B3"), w =>
        {
            Assert.Equal(ReservoirGeometryCalculator.TankFoundLevelName, w.TopLevelName);
            Assert.Equal(-dto.ThicknessDto.StbThk, w.TopOffset);
        });

        // W5_2: 수조 외벽 두께, Hopper 바닥(밸브실 바닥슬래브)부터
        var w52 = walls.Where(w => w.ElementCode == "W5").ElementAt(1);
        Assert.Equal(dto.ThicknessDto.WteThk, w52.Thickness);
        Assert.Equal(ReservoirGeometryCalculator.ValveFoundLevelName, w52.LevelName);

        // W6_2: 0..Lh 구간은 밸브실 바닥슬래브부터, Lh→0 방향
        var w62 = walls.Single(w => w.ElementCode == "W6" && w.EndPoint.Y == 0);
        Assert.Equal(ReservoirGeometryCalculator.ValveFoundLevelName, w62.LevelName);
        Assert.Equal(dto.TankDto.Lh, w62.StartPoint.Y);
    }

    // docs/289 § A: 2번째 지는 내벽 기준 미러 — M3 여백이 외벽 쪽
    [Fact]
    public void CalculateColumns_MirrorsSecondTank()
    {
        var dto = CreateReservoirDto(m3: 1500, m4: 2500);

        var columns = ReservoirGeometryCalculator.CalculateColumns(dto);

        double w = dto.TankDto.W, wti = dto.ThicknessDto.WtiThk;
        Assert.Equal(1500, columns.Min(c => c.Position.X));
        double secondTankOuter = 2 * w + wti;
        Assert.Equal(secondTankOuter - 1500, columns.Max(c => c.Position.X));
    }

    // docs/289 § C: H1 닫힌 루프, H3/H4 추가, 헌치 Z 설정
    [Fact]
    public void CalculateBeams_BuildsLegacyHaunches()
    {
        var dto = CreateReservoirDto();
        var sizes = new ReservoirMemberSizes(ColumnB: 500, ColumnH: 600, BeamB: 400);

        var beams = ReservoirGeometryCalculator.CalculateBeams(dto, sizes);

        // 25000/30000, M=4000, 최대 5000 → colNum 5, rowNum 6 → 점 2·6+2·5+4 = 26 → 구간 26
        var h1 = beams.Where(b => b.ElementCode == "H1").ToList();
        Assert.Equal(26 * 2, h1.Count);
        // 헌치는 밸브실과 같은 규칙: Z맞춤 원점(2), 상부 90°(docs/325 § A)
        Assert.All(h1, b =>
        {
            Assert.Equal(2, b.ZJustification);
            Assert.Equal(90, b.CrossSectionRotationDeg);
            Assert.Equal("헌치 300 x 300", b.TypeName);
        });
        // 반시계로 뒤집음: 마지막 구간 = 첫 보 앞(보 반폭 공제) → 좌하단 구석
        Assert.Equal(4000 - 200, h1[25].StartPoint.Y);
        Assert.Equal(0, h1[25].EndPoint.Y);

        Assert.Equal(4, beams.Count(b => b.ElementCode == "H3"));
        Assert.Equal(8, beams.Count(b => b.ElementCode == "H4"));

        // G1 가로보는 기둥 면에서 시작
        var g1 = beams.First(b => b.ElementCode == "G1");
        Assert.Equal(4000 + 250, g1.StartPoint.X);
    }

    // docs/325 § A: 진행 방향 왼쪽 = 실내. 상부·중간 90°, 하부 0°
    [Fact]
    public void CalculateBeams_HaunchesFaceInteriorLikeValveRoom()
    {
        var dto = CreateReservoirDto();
        var beams = ReservoirGeometryCalculator.CalculateBeams(dto, new ReservoirMemberSizes(500, 600, 400));
        double w = dto.TankDto.W, l = dto.TankDto.L, wti = dto.ThicknessDto.WtiThk;

        // 수조 헌치: 지마다 [x0, x0+w] × [0, l] 사각형 안쪽을 왼쪽에 둠
        foreach (var b in beams.Where(b => b.ElementCode is "H1" or "H2"))
        {
            var (mx, my) = ((b.StartPoint.X + b.EndPoint.X) / 2, (b.StartPoint.Y + b.EndPoint.Y) / 2);
            double dx = b.EndPoint.X - b.StartPoint.X, dy = b.EndPoint.Y - b.StartPoint.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            // 왼쪽 법선으로 10mm 들어간 점이 해당 지 안쪽이어야 함
            double px = mx - dy / len * 10, py = my + dx / len * 10;
            double x0 = px < w + wti / 2 ? 0 : w + wti;
            Assert.True(px > x0 && px < x0 + w && py > 0 && py < l, $"{b.ElementCode} {b.StartPoint}→{b.EndPoint} 왼쪽이 실 밖");
        }

        // 배관실 헌치: 배관실(y < 0) 안쪽을 왼쪽에 둠
        foreach (var b in beams.Where(b => b.ElementCode is "H3" or "H4"))
        {
            var (mx, my) = ((b.StartPoint.X + b.EndPoint.X) / 2, (b.StartPoint.Y + b.EndPoint.Y) / 2);
            double dx = b.EndPoint.X - b.StartPoint.X, dy = b.EndPoint.Y - b.StartPoint.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double py = my + dx / len * 10;
            double vTop = -dto.ThicknessDto.WteThk, vBot = vTop - dto.ValveDto.Wv;
            Assert.True(py < vTop && py > vBot, $"{b.ElementCode} {b.StartPoint}→{b.EndPoint} 왼쪽이 실 밖");
        }

        Assert.All(beams.Where(b => b.ElementCode is "H1" or "H3" or "H4"), b => Assert.Equal(90, b.CrossSectionRotationDeg));
        Assert.All(beams.Where(b => b.ElementCode == "H2"), b => Assert.Equal(0, b.CrossSectionRotationDeg));
        Assert.All(beams.Where(b => b.Part == "HAUNCH"), b => Assert.Equal(2, b.ZJustification));
    }

    // docs/325 § B: 헌치 체크박스 — 상부 = H1+H4, 중간 = H3, 하부 = H2
    [Theory]
    [InlineData(false, true, true, new[] { "H1", "H4" })]
    [InlineData(true, false, true, new[] { "H3" })]
    [InlineData(true, true, false, new[] { "H2" })]
    public void CalculateBeams_SkipsUncheckedHaunches(bool upper, bool middle, bool lower, string[] missing)
    {
        var dto = CreateReservoirDto(upper: upper, middle: middle, lower: lower);
        var codes = ReservoirGeometryCalculator.CalculateBeams(dto, new ReservoirMemberSizes(500, 600, 400))
            .Where(b => b.Part == "HAUNCH").Select(b => b.ElementCode).Distinct().ToList();

        foreach (var code in new[] { "H1", "H2", "H3", "H4" })
            Assert.Equal(!missing.Contains(code), codes.Contains(code));
    }

    // docs/325 § C: 보·기둥 유형 이름 = 입력 치수
    [Fact]
    public void BeamsAndColumns_UseSizeTypeNames()
    {
        var dto = CreateReservoirDto();
        var g1 = ReservoirGeometryCalculator.CalculateBeams(dto, new ReservoirMemberSizes(500, 600, 400)).Where(b => b.ElementCode == "G1").ToList();
        Assert.All(g1, b =>
        {
            Assert.Equal("400 x 700", b.TypeName);
            Assert.Equal(400, b.Width);
            Assert.Equal(700, b.Height);
        });
        Assert.All(ReservoirGeometryCalculator.CalculateColumns(dto), c => Assert.Equal("500 x 600", c.TypeName));
    }

    // docs/325 § F: 외벽만 실내 면 고정
    [Fact]
    public void CalculateLinearWalls_HoldsInteriorFaceOfExteriorWalls()
    {
        var walls = ReservoirGeometryCalculator.CalculateLinearWalls(CreateReservoirDto());

        Assert.Equal(WallHoldFace.Right, walls.Single(w => w.ElementCode == "W1").HoldFace);
        Assert.Equal(WallHoldFace.Left, walls.Single(w => w.ElementCode == "W2").HoldFace);
        Assert.Equal(WallHoldFace.Right, walls.Single(w => w.ElementCode == "W3").HoldFace);
        Assert.All(walls.Where(w => w.ElementCode == "W4"), w => Assert.Equal(WallHoldFace.Left, w.HoldFace));
        Assert.Equal(WallHoldFace.Left, walls.Single(w => w.ElementCode == "W7").HoldFace);
        Assert.Equal(WallHoldFace.Right, walls.Single(w => w.ElementCode == "W8").HoldFace);
        Assert.Equal(WallHoldFace.Left, walls.Single(w => w.ElementCode == "W9").HoldFace);
        Assert.All(walls.Where(w => w.ElementCode is "W5" or "W6" or "W10" or "B3"), w => Assert.Equal(WallHoldFace.Center, w.HoldFace));
    }

    // docs/289 § E-3, § F
    [Fact]
    public void ToppingIs100mm_AndSectionViewsAToD()
    {
        var dto = CreateReservoirDto();

        var toppings = ReservoirGeometryCalculator.CalculateSlabs(dto).Where(s => s.ElementCode.StartsWith("TC")).ToList();
        Assert.Equal(3, toppings.Count);
        Assert.All(toppings, s => Assert.Equal(100, s.Thickness));

        var sections = ReservoirGeometryCalculator.CalculateSectionViews(dto);
        Assert.Equal(new[] { "A", "B", "C", "D" }, sections.Select(s => s.Name));
        Assert.All(sections, s => Assert.True(s.HalfWidth > 0 && s.ZMax > s.ZMin && s.FarClipDepth > 0));
    }

    private static ReservoirCreationRequestDto CreateReservoirDto(double m3 = 4000, double m4 = 4000,
        bool upper = true, bool middle = true, bool lower = true)
    {
        var design = new ReservoirDesignConditionDto(Q: 5000, RT: 12, N: 2, LWL: 0);
        var tank = new ReservoirTankDto(
            He: 4.5,
            Hf: 300,
            Hm: 150,
            W: 25000,
            L: 30000,
            M1: 4000,
            M2: 4000,
            M3: m3,
            M4: m4,
            Wh: 2500,
            Lh: 2500,
            Hh: 2000,
            Ltt: 0);
        var valve = new ReservoirValveDto(
            H1F: 4000,
            Lv: 30000,
            Wv: 5000,
            Lvt: 0,
            We: 2500,
            TrOff: 100,
            Wp: 1000,
            Hp: 1000,
            WpThk: 300,
            SpThk: 300,
            SLp: 1000,
            SLv: 1.0);
        var thickness = new ReservoirTypeThicknessDto(
            StuThk: 300,
            StbThk: 500,
            SvuThk: 300,
            SvmThk: 300,
            SvbThk: 500,
            WteThk: 350,
            WtiThk: 350,
            WhThk: 300,
            WveThk: 300,
            WviThk: 300,
            LcThk: 100,
            ColumnB: 500,
            ColumnH: 600,
            BeamW: 400,
            BeamH: 700,
            HaunchB: 300,
            HaunchH: 300,
            HasUpperHaunch: upper,
            HasMiddleHaunch: middle,
            HasLowerHaunch: lower);

        return new ReservoirCreationRequestDto(design, tank, valve, thickness);
    }
}



