using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.Shared.Errors;
using Xunit;

namespace DHBIMWATER.Core.Tests.Structures;

public sealed class ValveRoomPlacementCalculatorTests
{
    [Fact]
    public void 관이_동쪽이면_도북각_0이고_좌하단은_중심에서_L2_W2_뺀다()
    {
        var result = ValveRoomPlacementCalculator.Calculate(Input(Point(1, 0)));

        Assert.Equal(0d, result.TrueNorthToProjectNorthClockwiseDegrees, 6);
        Assert.Equal(98d, result.SharedX, 4);
        Assert.Equal(199d, result.SharedY, 4);
    }

    [Fact]
    public void 관이_북쪽이면_도북각_마이너스90이고_좌하단도_회전한다()
    {
        // 프로젝트 X = 공유 (0,1), 프로젝트 Y = 공유 (−1,0).
        var result = ValveRoomPlacementCalculator.Calculate(Input(Point(0, 1)));

        Assert.Equal(-90d, result.TrueNorthToProjectNorthClockwiseDegrees, 6);
        Assert.Equal(101d, result.SharedX, 4);
        Assert.Equal(198d, result.SharedY, 4);
    }

    [Fact]
    public void 회전_토글이면_관_방향이_프로젝트_Y가_된다()
    {
        var result = ValveRoomPlacementCalculator.Calculate(Input(Point(0, 1)) with { RotateQuarterTurn = true });

        Assert.Equal(0d, result.TrueNorthToProjectNorthClockwiseDegrees, 6);
    }

    [Fact]
    public void 도북각은_마이너스180_초과_180_이하로_접는다()
    {
        // 관 방향 서쪽(β = 180°) → −180 → 180.
        var result = ValveRoomPlacementCalculator.Calculate(Input(Point(-1, 0)));

        Assert.Equal(180d, result.TrueNorthToProjectNorthClockwiseDegrees, 6);
    }

    [Fact]
    public void 기초_상면이_관중심_아래면_이격을_빼고_내부높이를_계산한다()
    {
        var result = ValveRoomPlacementCalculator.Calculate(Input(Point(1, 0)));

        Assert.Equal(13.8d, result.TopSlabTopEl, 9);
        Assert.Equal(9.7d, result.FoundationTopEl, 9);
        Assert.Equal(3700d, result.InnerHeight);
        Assert.Null(result.Floor2InnerHeight);
    }

    [Fact]
    public void 공기밸브실은_관중심에_깊이를_더한다()
    {
        var result = ValveRoomPlacementCalculator.Calculate(Input(Point(1, 0)) with
        {
            FoundationTopReference = FoundationTopReference.AbovePipeCenter,
            PipeCenterToFoundationTopMm = 250,
        });

        Assert.Equal(10.25d, result.FoundationTopEl, 9);
        Assert.Equal(3150d, result.InnerHeight);
    }

    [Fact]
    public void 중간슬래브면_2F_높이로_흡수한다()
    {
        var result = ValveRoomPlacementCalculator.Calculate(Input(Point(1, 0)) with
        {
            HasIntermediateSlab = true,
            Floor1InnerHeightMm = 1500,
            IntermediateSlabThicknessMm = 300,
        });

        Assert.Null(result.InnerHeight);
        Assert.Equal(1900d, result.Floor2InnerHeight);
    }

    [Fact]
    public void 높이가_0_이하면_VR505()
    {
        var ex = Assert.Throws<DhbimwaterException>(() => ValveRoomPlacementCalculator.Calculate(Input(Point(1, 0)) with { CoverDepthMm = 5000 }));
        Assert.Equal(ErrorCodes.VR_PlacementHeightInvalid, ex.Code);
    }

    [Fact]
    public void 관중심_외_ZDatum은_VR504()
    {
        var ex = Assert.Throws<DhbimwaterException>(() => ValveRoomPlacementCalculator.Calculate(Input(Point(1, 0)) with { ZDatum = ZDatum.Invert }));
        Assert.Equal(ErrorCodes.VR_PlacementZDatumUnsupported, ex.Code);
    }

    private static AlignmentStationPoint Point(double dirX, double dirY)
        => new(50, 100, 200, 10, 15, dirX, dirY, 0);

    // 지표 15, 토피 1200, 관중심 10, 이격 300, 상부슬래브 400, L 4000, W 2000.
    private static ValveRoomPlacementInput Input(AlignmentStationPoint point) => new()
    {
        Point = point,
        CoverDepthMm = 1200,
        FoundationTopReference = FoundationTopReference.BelowPipeCenter,
        PipeCenterToFoundationTopMm = 300,
        InnerLengthMm = 4000,
        InnerWidthMm = 2000,
        UpperSlabThicknessMm = 400,
    };
}
