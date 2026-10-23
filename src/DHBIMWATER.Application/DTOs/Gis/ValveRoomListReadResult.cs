namespace DHBIMWATER.Application.DTOs.Gis;

/// <summary>선형 시트의 밸브실 행 1개(docs/304). 표고 m, 관경 mm. 측점은 원문 그대로 — 선형 매핑의 측점 방식으로 해석한다.
/// 엑셀 표고는 관중심(docs/310). 누가거리는 목록 표시용.</summary>
public sealed record ValveRoomListRow(int RowNumber, string RoomType, string StationText, double DiameterMm, double PipeCenterEl, double GroundEl,
    string? CumulativeDistanceText = null)
{
    /// <summary>관저고(m) = 관중심 − 호칭지름/2.</summary>
    public double PipeInvertEl => PipeCenterEl - DiameterMm / 2000d;

    /// <summary>창 목록 표시.</summary>
    public string Display => $"{RoomType} · {StationText} · DN{DiameterMm:0}";

    public override string ToString() => Display;
}

/// <summary>밸브실 목록 읽기 결과. 읽지 못한 행은 빠지고 경고에 남는다.</summary>
public sealed record ValveRoomListReadResult(string SheetName, IReadOnlyList<ValveRoomListRow> Rows, IReadOnlyList<string> Warnings);
