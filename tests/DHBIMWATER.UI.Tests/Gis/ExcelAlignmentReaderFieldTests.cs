using ClosedXML.Excel;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Infrastructure.Repositories.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

/// <summary>엑셀 헤더행이 직경·관종 필드 후보로 노출되는지 실제 xlsx를 만들어 검증한다(docs/44).</summary>
public sealed class ExcelAlignmentReaderFieldTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dhbim_excel_{Guid.NewGuid():N}.xlsx");

    // 열 배치(1-based): 1=측점, 2=X, 3=Y, 4=Z, 5=직경, 6=관종, 7=직경(중복 헤더)
    public ExcelAlignmentReaderFieldTests()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("관로1");
        string[] header = ["측점", "X", "Y", "Z", "직경", "관종", "직경"];
        for (var c = 0; c < header.Length; c++) sheet.Cell(1, c + 1).Value = header[c];

        (string Station, double X, double Y, double Z, string Diameter, string Kind, string Duplicate)[] rows =
        [
            ("0+000", 100.0, 200.0, 10.0, "D300", "덕타일주철관", "예비1"),
            ("0+020", 120.0, 200.0, 10.5, "D300", "덕타일주철관", "예비2"),
            ("0+040", 140.0, 200.0, 11.0, "D400", "덕타일주철관", "예비3"),
        ];
        for (var i = 0; i < rows.Length; i++)
        {
            var r = i + 2;
            sheet.Cell(r, 1).Value = rows[i].Station;
            sheet.Cell(r, 2).Value = rows[i].X;
            sheet.Cell(r, 3).Value = rows[i].Y;
            sheet.Cell(r, 4).Value = rows[i].Z;
            sheet.Cell(r, 5).Value = rows[i].Diameter;
            sheet.Cell(r, 6).Value = rows[i].Kind;
            sheet.Cell(r, 7).Value = rows[i].Duplicate;
        }
        workbook.SaveAs(_path);
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    private ShapefileReadResult Read() => new ExcelAlignmentReader().Read(_path,
        new ExcelAlignmentMapping("관로1", HeaderRow: 1, DataStartRow: 2, XColumnIndex: 1, YColumnIndex: 2, ZColumnIndex: 3, StationColumnIndex: 0));

    [Fact]
    public void 헤더행의_열_이름이_필드로_노출되고_XYZ열은_제외된다()
    {
        var names = Read().Fields.Select(x => x.Name).ToList();

        Assert.Contains("직경", names);
        Assert.Contains("관종", names);
        Assert.Contains("측점", names);
        Assert.DoesNotContain("X", names);
        Assert.DoesNotContain("Y", names);
        Assert.DoesNotContain("Z", names);
    }

    [Fact]
    public void 헤더_이름이_중복되면_뒤에_오는_열에_접미사를_붙인다()
    {
        var result = Read();

        Assert.Contains("직경_2", result.Fields.Select(x => x.Name));
        Assert.Equal("예비1", result.Features.Single().Attributes["직경_2"]);
    }

    [Fact]
    public void 열마다_처음_만난_공백아닌_값이_대표값으로_실린다()
    {
        var attributes = Read().Features.Single().Attributes;

        Assert.Equal("D300", attributes["직경"]);
        Assert.Equal("덕타일주철관", attributes["관종"]);
    }

    [Fact]
    public void 값이_섞인_열은_경고하고_측점열은_경고하지_않는다()
    {
        var warnings = Read().Warnings;

        Assert.Contains(warnings, x => x.Contains("'직경' 열에 서로 다른 값이 2종"));
        Assert.DoesNotContain(warnings, x => x.Contains("'측점' 열에"));
        Assert.DoesNotContain(warnings, x => x.Contains("'관종' 열에"));
    }

    [Fact]
    public void 노출된_필드로_직경_관종_열을_자동_추정할_수_있다()
    {
        var names = Read().Fields.Select(x => x.Name).ToList();

        Assert.Equal("직경", AlignmentAttributeParser.GuessDiameterField(names));
        Assert.Equal("관종", AlignmentAttributeParser.GuessKindField(names));
    }

    [Fact]
    public void Station방식과_NumberChain방식으로_누가거리를_다르게_해석한다()
    {
        Assert.Equal(1030d, StationTextParser.Parse("1+030.0", StationParseMode.Station, 20d));
        Assert.Equal(270d, StationTextParser.Parse("NO.12+030", StationParseMode.NumberChain, 20d));
        Assert.Equal(270d, StationTextParser.Parse("12+030", StationParseMode.NumberChain, 20d));
    }

    [Fact]
    public void 측점_숫자와_원본_전체경로를_함께_보존한다()
    {
        var alignment = Read().Features.Single();

        Assert.Equal([0d, 20d, 40d], alignment.VertexStationM);
        Assert.Equal(Path.GetFullPath(_path), alignment.SourceFile);
    }

    [Fact]
    public void 정점별_곡관종류와_DN을_행순서대로_읽는다()
    {
        var result = new ExcelAlignmentReader().Read(_path,
            new ExcelAlignmentMapping("관로1", 1, 2, 1, 2, 3, 0, BendKindColumnIndex: 6, VertexDiameterColumnIndex: 4));
        var alignment = result.Features.Single();

        Assert.Equal(["예비1", "예비2", "예비3"], alignment.VertexBendKinds);
        Assert.Equal([300d, 300d, 400d], alignment.VertexDiameterMm);
        Assert.Equal(["D300", "D300", "D400"], alignment.VertexDiameterRawValues);
    }

    [Fact]
    public void 희소_DN은_원본표기와_mm를_함께_전방채움한다()
    {
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("희소");
            string[] header = ["측점", "X", "Y", "Z", "호칭지름\n(DN)", "곡관유형"];
            for (var c = 0; c < header.Length; c++) sheet.Cell(1, c + 1).Value = header[c];
            for (var i = 0; i < 6; i++)
            {
                sheet.Cell(i + 2, 1).Value = $"0+0{i}0";
                sheet.Cell(i + 2, 2).Value = i * 10;
                sheet.Cell(i + 2, 3).Value = 0;
                sheet.Cell(i + 2, 4).Value = 0;
            }
            sheet.Cell(2, 5).Value = "DN300";
            sheet.Cell(5, 5).Value = "DN500";
            workbook.SaveAs(_path);
        }

        var result = new ExcelAlignmentReader().Read(_path,
            new ExcelAlignmentMapping("희소", 1, 2, 1, 2, 3, 0, BendKindColumnIndex: 5, VertexDiameterColumnIndex: 4));
        var alignment = result.Features.Single();

        Assert.Equal([300d, 300d, 300d, 500d, 500d, 500d], alignment.VertexDiameterMm);
        Assert.Equal(["DN300", "DN300", "DN300", "DN500", "DN500", "DN500"], alignment.VertexDiameterRawValues);
        Assert.Equal(["0+000", "0+010", "0+020", "0+030", "0+040", "0+050"], alignment.VertexStationTexts);
    }

    [Fact]
    public void 선택열을_사용안함으로_두면_정점목록이_null이다()
    {
        var alignment = Read().Features.Single();

        Assert.Null(alignment.VertexBendKinds);
        Assert.Null(alignment.VertexDiameterMm);
        Assert.Null(alignment.VertexDiameterRawValues);
    }
}
