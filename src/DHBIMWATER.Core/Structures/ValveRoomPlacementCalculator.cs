using DHBIMWATER.Core.Gis;
using DHBIMWATER.Shared.Errors;
namespace DHBIMWATER.Core.Structures;

/// <summary>기초 상면 기준. 공기밸브실은 관이 기초 안이라 관 중심 위(Above), 나머지는 관저고 아래(Below).</summary>
public enum FoundationTopReference
{
    /// <summary>기초 상면 = 관저고 − 값(H1). 이토·타이·제수(docs/304).</summary>
    BelowPipeInvert,
    /// <summary>기초 상면 = 관 중심 + 값. 공기(본관 중심 깊이).</summary>
    AbovePipeCenter
}

/// <summary>밸브실 위치 자동지정 입력(docs/300 § A-2, docs/304). 치수 mm, 표고·좌표 m.</summary>
public sealed record ValveRoomPlacementInput
{
    /// <summary>선형 위 측점 점. XY·방향만 쓴다. 표고는 목록 시트 값(<see cref="PipeInvertEl"/>·<see cref="GroundEl"/>).</summary>
    public required AlignmentStationPoint Point { get; init; }
    /// <summary>관저고(m). 관 바닥 표고.</summary>
    public double PipeInvertEl { get; init; }
    /// <summary>원지반 표고(m).</summary>
    public double GroundEl { get; init; }
    /// <summary>관경(mm). 관 중심 = 관저고 + 관경/2.</summary>
    public double PipeDiameterMm { get; init; }
    /// <summary>원지반 → 상부슬래브 상면 거리(mm).</summary>
    public double GroundToTopSlabTopMm { get; init; } = ValveRoomPlacementRules.GroundToTopSlabTopMm;
    public FoundationTopReference FoundationTopReference { get; init; }
    /// <summary>기준점 ↔ 기초 상면 거리(mm). 기준점·방향은 <see cref="FoundationTopReference"/>.</summary>
    public double FoundationTopOffsetMm { get; init; }
    public double InnerLengthMm { get; init; }
    public double InnerWidthMm { get; init; }
    public double UpperSlabThicknessMm { get; init; }
    /// <summary>true면 중간슬래브 켠 이토. 전체 높이 차이를 2F 높이로 흡수한다(docs/300 D7).</summary>
    public bool HasIntermediateSlab { get; init; }
    public double IntermediateSlabThicknessMm { get; init; }
    public double Floor1InnerHeightMm { get; init; }
    /// <summary>false = 관 방향이 L(프로젝트 X), true = 관 방향이 W(프로젝트 Y).</summary>
    public bool RotateQuarterTurn { get; init; }
}

/// <summary>공유좌표는 내부공간 좌하단. InnerHeight/Floor2InnerHeight 중 하나만 값.</summary>
public sealed record ValveRoomPlacementResult(
    double SharedX,
    double SharedY,
    double TrueNorthToProjectNorthClockwiseDegrees,
    double FoundationTopEl,
    double PipeCenterEl,
    double TopSlabTopEl,
    double? InnerHeight,
    double? Floor2InnerHeight);

/// <summary>측점 점 + 목록 값 → 공유좌표·도북각·기초상부EL·내부높이(docs/300 § 3-2~3-5, docs/304). Revit 의존 없음.</summary>
public static class ValveRoomPlacementCalculator
{
    public static ValveRoomPlacementResult Calculate(ValveRoomPlacementInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var point = input.Point;
        var pipeCenterEl = input.PipeInvertEl + input.PipeDiameterMm / 2000d;

        // 모델 각 = 공유 각 + 도북각(BendOrientation.ApplyProjectNorth). 관 방향이 모델 +X(토글이면 +Y)가 되게 한다.
        var pipeAngleDeg = Math.Atan2(point.DirY, point.DirX) * 180d / Math.PI;
        var trueNorth = Math.Round(NormalizeDegrees((input.RotateQuarterTurn ? 90d : 0d) - pipeAngleDeg), 6);

        // 측점 점 = 내부공간 중심 → 좌하단으로 되돌린다.
        var (cornerX, cornerY) = LowerLeftFromCenter(point.X, point.Y, trueNorth, input.InnerLengthMm, input.InnerWidthMm);
        var sharedX = Math.Round(cornerX, 4);
        var sharedY = Math.Round(cornerY, 4);

        var topSlabTopEl = input.GroundEl - input.GroundToTopSlabTopMm / 1000d;
        var foundationTopEl = Math.Round(input.FoundationTopReference == FoundationTopReference.AbovePipeCenter
            ? pipeCenterEl + input.FoundationTopOffsetMm / 1000d
            : input.PipeInvertEl - input.FoundationTopOffsetMm / 1000d, 3);
        var totalHeight = Math.Round((topSlabTopEl - foundationTopEl) * 1000d - input.UpperSlabThicknessMm);

        double? innerHeight = null;
        double? floor2Height = null;
        if (input.HasIntermediateSlab)
        {
            floor2Height = totalHeight - input.Floor1InnerHeightMm - input.IntermediateSlabThicknessMm;
            if (floor2Height <= 0)
                throw new DhbimwaterException(ErrorCodes.VR_PlacementHeightInvalid,
                    $"계산된 2F 내부 높이가 {floor2Height:0}mm입니다. 관저고·원지반·1F 높이를 확인하세요.");
        }
        else
        {
            innerHeight = totalHeight;
            if (innerHeight <= 0)
                throw new DhbimwaterException(ErrorCodes.VR_PlacementHeightInvalid,
                    $"계산된 내부 높이가 {innerHeight:0}mm입니다. 관저고·원지반을 확인하세요.");
        }

        return new ValveRoomPlacementResult(sharedX, sharedY, trueNorth, foundationTopEl, pipeCenterEl, topSlabTopEl, innerHeight, floor2Height);
    }

    /// <summary>내부공간 중심(공유좌표 m) → 좌하단(공유좌표 m). L·W mm. 자동지정 뒤 L·W만 바뀔 때 중심 유지용.</summary>
    public static (double X, double Y) LowerLeftFromCenter(double centerX, double centerY, double trueNorthClockwiseDegrees, double innerLengthMm, double innerWidthMm)
    {
        // 프로젝트 X·Y축의 공유좌표 단위벡터.
        var phi = -trueNorthClockwiseDegrees * Math.PI / 180d;
        var ux = (X: Math.Cos(phi), Y: Math.Sin(phi));
        var uy = (X: -Math.Sin(phi), Y: Math.Cos(phi));
        var halfL = innerLengthMm / 2000d;
        var halfW = innerWidthMm / 2000d;
        return (centerX - halfL * ux.X - halfW * uy.X, centerY - halfL * ux.Y - halfW * uy.Y);
    }

    /// <summary>(−180°, 180°]로 접는다.</summary>
    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360d;
        if (normalized <= -180d) normalized += 360d;
        else if (normalized > 180d) normalized -= 360d;
        return normalized;
    }
}
