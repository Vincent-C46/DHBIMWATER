using ClosedXML.Excel;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Infrastructure.Repositories.Gis;
using DHBIMWATER.Shared.Errors;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

/// <summary>선형 시트 1장에 끼운 밸브실 행(유형 칸 채움)을 목록으로 읽는다(docs/304).</summary>
public sealed class ExcelValveRoomListReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dhbim_valveroom_list_{Guid.NewGuid():N}.xlsx");

    // 측점 · X · Y · Z(관중심, docs/310) · 호칭지름(DN) · 밸브실유형 · 원지반. 헤더 4행.
    private static readonly ExcelAlignmentMapping Mapping = new("선형", HeaderRow: 4, DataStartRow: 5, XColumnIndex: 1, YColumnIndex: 2, ZColumnIndex: 3,
        StationColumnIndex: 0, VertexDiameterColumnIndex: 4, ValveRoomTypeColumnIndex: 5, GroundElColumnIndex: 6);

    private void Save(params object[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("선형");
        string[] header = ["측점", "Easting", "Northing", "표고", "호칭지름(DN)", "밸브실유형", "원지반"];
        for (var c = 0; c < header.Length; c++) sheet.Cell(4, c + 1).Value = header[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                sheet.Cell(5 + r, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
        workbook.SaveAs(_path);
    }

    [Fact]
    public void 유형_칸이_채워진_행만_밸브실로_읽는다()
    {
        Save(
            ["0+000.00", 100.0, 200.0, 54.7, "DN1650", "", ""],
            ["0+250.00", 120.0, 200.0, 57.075, "DN1650", "제수", 59.0],
            ["0+300.00", 130.0, 200.0, 57.1, "DN1650", "", ""],
            ["1+250.00", 140.0, 200.0, 57.075, 1650.0, "이토밸브실", 59.0],
            ["1+300.00", 150.0, 200.0, 57.2, "DN1650", "공기밸브실", ""]);

        // 누가거리 대신 측점 열(0)을 넘겨 표시값이 실리는지만 본다.
        var result = new ExcelAlignmentReader().ReadValveRoomList(_path, Mapping, cumulativeDistanceColumn: 0);

        Assert.Equal(2, result.Rows.Count);
        var first = result.Rows[0];
        Assert.Equal(("제수밸브실", "0+250.00", 1650d, 57.075, 59d), (first.RoomType, first.StationText, first.DiameterMm, first.PipeCenterEl, first.GroundEl));
        // 관저고 = 표고(관중심) − DN/2 = 57.075 − 0.825.
        Assert.Equal(56.25, first.PipeInvertEl, 6);
        Assert.Equal("0+250.00", first.CumulativeDistanceText);
        Assert.Equal("이토밸브실", result.Rows[1].RoomType);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void 밸브실_행은_XY가_비어도_선형_보간용_행에서_조용히_빠진다()
    {
        Save(
            ["0+000.00", 100.0, 200.0, 54.7, "DN1650", "", ""],
            ["0+050.00", "", "", 57.075, "DN1650", "제수밸브실", 59.0],
            ["0+100.00", 200.0, 200.0, 54.8, "DN1650", "", ""]);

        var reader = new ExcelAlignmentReader();
        var profile = reader.ReadProfile(_path, Mapping);
        var list = reader.ReadValveRoomList(_path, Mapping);

        Assert.Equal(2, profile.Rows.Count);
        Assert.Empty(profile.Warnings);
        Assert.Single(list.Rows);
    }

    [Fact]
    public void 밸브실_행이_없으면_VR506()
    {
        Save(["0+000.00", 100.0, 200.0, 54.7, "DN1650", "", ""]);

        var ex = Assert.Throws<DhbimwaterException>(() => new ExcelAlignmentReader().ReadValveRoomList(_path, Mapping));

        Assert.Equal(ErrorCodes.VR_PlacementListInvalid, ex.Code);
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }
}
