namespace DHBIMWATER.Application.DTOs.Gis;

/// <summary>선형 시트의 밸브실 행 1개(docs/304). 표고 m, 관경 mm. 측점은 원문 그대로 — 선형 매핑의 측점 방식으로 해석한다.</summary>
public sealed record ValveRoomListRow(int RowNumber, string RoomType, string StationText, double DiameterMm, double PipeInvertEl, double GroundEl)
{
    /// <summary>창 목록 표시.</summary>
    public string Display => $"{RoomType} · {StationText} · DN{DiameterMm:0}";

    public override string ToString() => Display;
}

/// <summary>밸브실 목록 읽기 결과. 읽지 못한 행은 빠지고 경고에 남는다.</summary>
public sealed record ValveRoomListReadResult(string SheetName, IReadOnlyList<ValveRoomListRow> Rows, IReadOnlyList<string> Warnings);
