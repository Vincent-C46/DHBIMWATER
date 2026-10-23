namespace DHBIMWATER.Core.Structures;

/// <summary>밸브실 위치 자동지정 고정값(docs/304). 사용자 결정 임시값 — 바뀔 수 있음, 여기만 고친다.</summary>
public static class ValveRoomPlacementRules
{
    /// <summary>관저고 → 기초 상면 아래 거리(H1) 기본값(mm).</summary>
    public const double DefaultInvertToFoundationTopMm = 600;

    /// <summary>원지반 → 상부슬래브 상면 아래 거리(mm).</summary>
    public const double GroundToTopSlabTopMm = 250;

    /// <summary>관경별 H1(mm). 지금은 전 관경 600.</summary>
    // TODO(docs/304): 관경별 H1 표 받으면 여기서 관경으로 분기.
    public static double InvertToFoundationTopMm(double diameterMm) => DefaultInvertToFoundationTopMm;

    /// <summary>창 기본값 기준 관경(mm). 제수·공기 1650, 이토 1200(docs/311).</summary>
    public const double DefaultPipeDiameterMm = 1650;
    public const double DefaultMudPipeDiameterMm = 1200;

    public static double DefaultPipeDiameter(string? roomKind)
        => roomKind == ValveRoomStandardDimensionCatalog.Mud ? DefaultMudPipeDiameterMm : DefaultPipeDiameterMm;

    /// <summary>공기밸브실 본관 위 기초 덮개(mm). 기초 상면 ~ 관 상단.</summary>
    public const double AirValvePipeTopCoverMm = 200;

    /// <summary>공기밸브실 본관 아래 기초 두께(mm). 관 하단 ~ 기초 하면.</summary>
    public const double AirValvePipeBottomCoverMm = 300;

    /// <summary>공기밸브실 기초두께 = 관경 + 상부 200 + 하부 300(docs/311).</summary>
    public static double AirValveFoundationThicknessMm(double diameterMm)
        => diameterMm + AirValvePipeTopCoverMm + AirValvePipeBottomCoverMm;

    /// <summary>공기밸브실 Hp(기초 상면 → 관 중심) = 상부 200 + 관경/2(docs/311).</summary>
    public static double AirValvePipeCenterDepthMm(double diameterMm) => AirValvePipeTopCoverMm + diameterMm / 2;
}
