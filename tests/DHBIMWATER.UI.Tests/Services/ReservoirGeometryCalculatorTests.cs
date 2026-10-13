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
        // 헌치 높이·방향은 기존 신규 방식(슬래브 하단, 상단 맞춤, 회전 없음) — 사용자 확인 2026-10-13
        Assert.All(h1, b =>
        {
            Assert.Equal(0, b.ZJustification);
            Assert.Equal(0, b.CrossSectionRotationDeg);
        });
        // 첫 구간: 좌하단 구석 → 첫 보 앞(보 반폭 공제)
        Assert.Equal(4000 - 200, h1[0].EndPoint.Y);

        Assert.Equal(4, beams.Count(b => b.ElementCode == "H3"));
        Assert.Equal(8, beams.Count(b => b.ElementCode == "H4"));

        // G1 가로보는 기둥 면에서 시작
        var g1 = beams.First(b => b.ElementCode == "G1");
        Assert.Equal(4000 + 250, g1.StartPoint.X);
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

    private static ReservoirCreationRequestDto CreateReservoirDto(double m3 = 4000, double m4 = 4000)
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
            ColumnTypeName: "Column",
            BeamTypeName: "Beam");

        return new ReservoirCreationRequestDto(design, tank, valve, thickness);
    }
}



