using DHBIMWATER.Core.Gis;
using DHBIMWATER.Shared.Errors;
namespace DHBIMWATER.Core.Structures;

/// <summary>기초 상면이 관 중심의 어느 쪽인가. 공기밸브실은 관이 기초 안이라 위(Above), 나머지는 아래(Below).</summary>
public enum FoundationTopReference
{
    /// <summary>기초 상면 = 관 중심 − 값. 이토·타이·제수.</summary>
    BelowPipeCenter,
    /// <summary>기초 상면 = 관 중심 + 값. 공기(본관 중심 깊이).</summary>
    AbovePipeCenter
}

/// <summary>밸브실 위치 자동지정 입력(docs/300 § A-2). 치수 mm, 표고·좌표 m.</summary>
public sealed record ValveRoomPlacementInput
{
    public required AlignmentStationPoint Point { get; init; }
    public double CoverDepthMm { get; init; }
    public FoundationTopReference FoundationTopReference { get; init; }
    /// <summary>관 중심 ↔ 기초 상면 거리(mm). 방향은 <see cref="FoundationTopReference"/>.</summary>
    public double PipeCenterToFoundationTopMm { get; init; }
    public double InnerLengthMm { get; init; }
    public double InnerWidthMm { get; init; }
    public double UpperSlabThicknessMm { get; init; }
    /// <summary>true면 중간슬래브 켠 이토. 전체 높이 차이를 2F 높이로 흡수한다(docs/300 D7).</summary>
    public bool HasIntermediateSlab { get; init; }
    public double IntermediateSlabThicknessMm { get; init; }
    public double Floor1InnerHeightMm { get; init; }
    /// <summary>false = 관 방향이 L(프로젝트 X), true = 관 방향이 W(프로젝트 Y).</summary>
    public bool RotateQuarterTurn { get; init; }
    /// <summary>엑셀 Z 기준. 지금은 Centerline만 지원 — 확장점(docs/300 § 3-2).</summary>
    public ZDatum ZDatum { get; init; } = ZDatum.Centerline;
    /// <summary>ZDatum 환산용 관 외경(mm). Centerline이면 안 씀.</summary>
    public double? PipeOuterDiameterMm { get; init; }
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

/// <summary>측점 점 + 사용자 입력 → 공유좌표·도북각·기초상부EL·내부높이(docs/300 § 3-2~3-5). Revit 의존 없음.</summary>
public static class ValveRoomPlacementCalculator
{
    public static ValveRoomPlacementResult Calculate(ValveRoomPlacementInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var point = input.Point;
        var pipeCenterEl = ResolvePipeCenterEl(point.Z, input.ZDatum);

        // 모델 각 = 공유 각 + 도북각(BendOrientation.ApplyProjectNorth). 관 방향이 모델 +X(토글이면 +Y)가 되게 한다.
        var pipeAngleDeg = Math.Atan2(point.DirY, point.DirX) * 180d / Math.PI;
        var trueNorth = Math.Round(NormalizeDegrees((input.RotateQuarterTurn ? 90d : 0d) - pipeAngleDeg), 6);

        // 프로젝트 X·Y축의 공유좌표 단위벡터. 측점 점 = 내부공간 중심 → 좌하단으로 되돌린다.
        var phi = -trueNorth * Math.PI / 180d;
        var ux = (X: Math.Cos(phi), Y: Math.Sin(phi));
        var uy = (X: -Math.Sin(phi), Y: Math.Cos(phi));
        var halfL = input.InnerLengthMm / 2000d;
        var halfW = input.InnerWidthMm / 2000d;
        var sharedX = Math.Round(point.X - halfL * ux.X - halfW * uy.X, 4);
        var sharedY = Math.Round(point.Y - halfL * ux.Y - halfW * uy.Y, 4);

        var topSlabTopEl = point.GroundEl - input.CoverDepthMm / 1000d;
        var foundationTopEl = Math.Round(input.FoundationTopReference == FoundationTopReference.AbovePipeCenter
            ? pipeCenterEl + input.PipeCenterToFoundationTopMm / 1000d
            : pipeCenterEl - input.PipeCenterToFoundationTopMm / 1000d, 3);
        var totalHeight = Math.Round((topSlabTopEl - foundationTopEl) * 1000d - input.UpperSlabThicknessMm);

        double? innerHeight = null;
        double? floor2Height = null;
        if (input.HasIntermediateSlab)
        {
            floor2Height = totalHeight - input.Floor1InnerHeightMm - input.IntermediateSlabThicknessMm;
            if (floor2Height <= 0)
                throw new DhbimwaterException(ErrorCodes.VR_PlacementHeightInvalid,
                    $"계산된 2F 내부 높이가 {floor2Height:0}mm입니다. 토피고·이격·1F 높이를 확인하세요.");
        }
        else
        {
            innerHeight = totalHeight;
            if (innerHeight <= 0)
                throw new DhbimwaterException(ErrorCodes.VR_PlacementHeightInvalid,
                    $"계산된 내부 높이가 {innerHeight:0}mm입니다. 토피고·이격을 확인하세요.");
        }

        return new ValveRoomPlacementResult(sharedX, sharedY, trueNorth, foundationTopEl, pipeCenterEl, topSlabTopEl, innerHeight, floor2Height);
    }

    private static double ResolvePipeCenterEl(double z, ZDatum datum)
    {
        if (datum == ZDatum.Centerline) return z;
        // TODO(docs/300 § 3-2): 관저고 등 환산. AlignmentPlacementOrigin.GetZOffsetM 재사용 검토.
        throw new DhbimwaterException(ErrorCodes.VR_PlacementZDatumUnsupported, $"엑셀 Z 기준 '{datum}'은 아직 지원하지 않습니다. 관 중심 표고를 쓰세요.");
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
