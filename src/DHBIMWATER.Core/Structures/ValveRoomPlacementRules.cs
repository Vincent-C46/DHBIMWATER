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
}
