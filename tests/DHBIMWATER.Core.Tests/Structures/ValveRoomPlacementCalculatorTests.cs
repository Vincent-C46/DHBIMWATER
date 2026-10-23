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
    public void 중심에서_좌하단_변환은_도북각만큼_회전한다()
    {
        // 도북각 −90 → 프로젝트 X = 공유 (0,1), 프로젝트 Y = 공유 (−1,0). L 10000·W 4000.
        var (x, y) = ValveRoomPlacementCalculator.LowerLeftFromCenter(100, 200, -90, 10000, 4000);

        Assert.Equal(102d, x, 6);
        Assert.Equal(195d, y, 6);
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
    public void 기초_상면은_관저고_아래_H1_상부슬래브_상면은_원지반_아래_250()
    {
        var result = ValveRoomPlacementCalculator.Calculate(Input(Point(1, 0)));

        Assert.Equal(10d, result.PipeCenterEl, 9);
        Assert.Equal(14.75d, result.TopSlabTopEl, 9);
        Assert.Equal(9.25d, result.FoundationTopEl, 9);
        Assert.Equal(5100d, result.InnerHeight);
        Assert.Null(result.Floor2InnerHeight);
    }

    [Fact]
    public void 공기밸브실은_관중심에_깊이를_더한다()
    {
        var result = ValveRoomPlacementCalculator.Calculate(Input(Point(1, 0)) with
        {
            FoundationTopReference = FoundationTopReference.AbovePipeCenter,
            FoundationTopOffsetMm = 250,
        });

        Assert.Equal(10.25d, result.FoundationTopEl, 9);
        Assert.Equal(4100d, result.InnerHeight);
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
        Assert.Equal(3300d, result.Floor2InnerHeight);
    }

    [Fact]
    public void 높이가_0_이하면_VR505()
    {
        var ex = Assert.Throws<DhbimwaterException>(() => ValveRoomPlacementCalculator.Calculate(Input(Point(1, 0)) with { GroundEl = 9 }));
        Assert.Equal(ErrorCodes.VR_PlacementHeightInvalid, ex.Code);
    }

    private static AlignmentStationPoint Point(double dirX, double dirY)
        => new(50, 100, 200, 10, dirX, dirY, 0);

    // 관저 9.85, DN300(관중심 10.0), 원지반 15, H1 600, 상부슬래브 400, L 4000, W 2000.
    private static ValveRoomPlacementInput Input(AlignmentStationPoint point) => new()
    {
        Point = point,
        PipeInvertEl = 9.85,
        GroundEl = 15,
        PipeDiameterMm = 300,
        FoundationTopReference = FoundationTopReference.BelowPipeInvert,
        FoundationTopOffsetMm = ValveRoomPlacementRules.InvertToFoundationTopMm(300),
        InnerLengthMm = 4000,
        InnerWidthMm = 2000,
        UpperSlabThicknessMm = 400,
    };
}
