using ClosedXML.Excel;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Infrastructure.Repositories.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

/// <summary>Excel에 열려 있는 .xlsx도 읽히는지, Excel과 같은 잠금 핸들을 잡은 채 검증한다(docs/252).</summary>
public sealed class ExcelAlignmentReaderLockedFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dhbim_excel_locked_{Guid.NewGuid():N}.xlsx");

    public ExcelAlignmentReaderLockedFileTests()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("관로1");
        sheet.Cell(1, 1).Value = "측점"; sheet.Cell(1, 2).Value = "X"; sheet.Cell(1, 3).Value = "Y"; sheet.Cell(1, 4).Value = "Z";
        sheet.Cell(2, 1).Value = "0+000"; sheet.Cell(2, 2).Value = 100.0; sheet.Cell(2, 3).Value = 200.0; sheet.Cell(2, 4).Value = 10.0;
        sheet.Cell(3, 1).Value = "0+020"; sheet.Cell(3, 2).Value = 120.0; sheet.Cell(3, 3).Value = 200.0; sheet.Cell(3, 4).Value = 10.5;
        workbook.SaveAs(_path);
    }

    /// <summary>Excel이 파일을 열 때 거는 잠금: 쓰기 핸들 + 다른 프로세스의 쓰기 거부.</summary>
    private FileStream LockLikeExcel() => File.Open(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

    [Fact]
    public void 엑셀에_열린_파일도_시트_이름을_읽는다()
    {
        using var excel = LockLikeExcel();

        Assert.Equal(["관로1"], new ExcelAlignmentReader().GetSheetNames(_path));
    }

    [Fact]
    public void 엑셀에_열린_파일도_좌표를_읽는다()
    {
        using var excel = LockLikeExcel();

        var result = new ExcelAlignmentReader().Read(_path,
            new ExcelAlignmentMapping("관로1", HeaderRow: 1, DataStartRow: 2, XColumnIndex: 1, YColumnIndex: 2, ZColumnIndex: 3, StationColumnIndex: 0));

        var vertices = result.Features.Single().Vertices;
        Assert.Equal(2, vertices.Count);
        Assert.Equal(120.0, vertices[1].X);
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }
}
