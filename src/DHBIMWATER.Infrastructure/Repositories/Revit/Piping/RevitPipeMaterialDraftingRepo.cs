using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 배관재료표를 드래프팅뷰에 선(<see cref="DetailCurve"/>)과 문자(<see cref="TextNote"/>)로 직접 그린다(docs/133).
/// <para>참고한 방식은 <c>docs/01_철근상세도_..._rev260408.dyn</c>이다 — 격자선을 그리고 셀마다 문자를 넣는다.
/// 다만 그 스크립트는 범례를 복제해 쓰는데, 여기서는 <see cref="ViewDrafting.Create"/>로 새로 만든다.</para>
/// <para>뷰 축척은 1:1로 두고 모든 치수를 mm로 잡는다. 그래야 여기 적은 mm 값이 도면에서 그대로 mm가 된다.</para>
/// <para>트랜잭션은 열지 않는다. UseCase가 연다(CLAUDE.md).</para>
/// </summary>
internal sealed class RevitPipeMaterialDraftingRepo : IPipeMaterialDraftingRepo
{
    /// <summary>열 정의 — 제목, 폭(mm), 가운데 정렬 여부. 순서가 곧 도면 열 순서다(docs/131 § 0 표).</summary>
    private static readonly (string Heading, double WidthMm, bool Centered)[] Columns =
    [
        ("번호", 14, true),
        ("명칭", 40, false),
        ("규격", 42, false),
        ("형상", 30, true),   // docs/133: 이번에는 칸만 그리고 심볼은 넣지 않는다.
        ("수량", 14, true),
        ("비고", 30, false),  // 값을 쓰지 않는다. 사용자가 도면에서 직접 채운다.
    ];

    private const double RowHeightMm = 9;
    private const double TitleGapMm = 6;      // 표 상단선과 제목 문자 사이 간격
    private const double TextPaddingMm = 2;   // 좌측 정렬 칸의 안쪽 여백

    private readonly Func<Document?> _document;
    public RevitPipeMaterialDraftingRepo(Func<Document?> document) => _document = document;

    public IReadOnlyList<PipeMaterialPart> GetParts()
        => PipeMaterialPartCollector.Read(Document());

    public bool ViewExists(string viewName)
        => new FilteredElementCollector(Document())
            .OfClass(typeof(View))
            .Cast<View>()
            .Any(x => !x.IsTemplate && string.Equals(x.Name, viewName, StringComparison.Ordinal));

    public void CreateTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows)
    {
        var document = Document();
        var view = CreateView(document, viewName);

        // 머리행 1줄 + 자료행 n줄. 좌표 원점은 표 좌상단이고, 아래로 갈수록 Y가 작아진다.
        var lineCount = rows.Count + 1;
        var totalWidthMm = Columns.Sum(x => x.WidthMm);
        var totalHeightMm = lineCount * RowHeightMm;

        DrawGrid(document, view, totalWidthMm, totalHeightMm, lineCount);
        DrawTexts(document, view, title, rows);
    }

    private Document Document()
        => _document() ?? throw new InvalidOperationException("활성 Revit 문서가 없습니다.");

    private static ViewDrafting CreateView(Document document, string viewName)
    {
        var draftingType = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting)
            ?? throw new InvalidOperationException(
                "드래프팅뷰 유형(ViewFamilyType)이 프로젝트에 없습니다. 템플릿에 드래프팅뷰 유형을 추가하세요.");

        var view = ViewDrafting.Create(document, draftingType.Id);
        view.Name = viewName;
        // 축척 1:1. 이 클래스의 mm 값이 곧 도면 mm가 되고, 문자 크기(용지 mm)와도 눈금이 맞는다.
        view.Scale = 1;
        return view;
    }

    /// <summary>표 격자. 가로선은 머리행 포함 각 행 경계에, 세로선은 각 열 경계에 긋는다.</summary>
    private static void DrawGrid(Document document, ViewDrafting view,
        double totalWidthMm, double totalHeightMm, int lineCount)
    {
        for (var row = 0; row <= lineCount; row++)
        {
            var y = -row * RowHeightMm;
            DrawLine(document, view, 0, y, totalWidthMm, y);
        }

        var x = 0d;
        DrawLine(document, view, x, 0, x, -totalHeightMm);
        foreach (var column in Columns)
        {
            x += column.WidthMm;
            DrawLine(document, view, x, 0, x, -totalHeightMm);
        }
    }

    private static void DrawTexts(Document document, ViewDrafting view, string title,
        IReadOnlyList<PipeMaterialRow> rows)
    {
        var textTypeId = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        if (textTypeId == ElementId.InvalidElementId)
            throw new InvalidOperationException(
                "기본 문자 유형(TextNoteType)이 프로젝트에 없습니다. 템플릿에 문자 유형을 추가하세요.");

        // 제목은 표 좌상단 위에 왼쪽 정렬로 놓는다.
        PlaceText(document, view, textTypeId, title, 0, TitleGapMm, centered: false);

        // 머리행
        for (var col = 0; col < Columns.Length; col++)
            PlaceCellText(document, view, textTypeId, Columns[col].Heading, col, row: 0, centered: true);

        // 자료행. 형상·비고는 값을 쓰지 않으므로 칸만 남는다(docs/133 § 0).
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var values = new[]
            {
                row.ItemNumber.ToString(),
                row.Name,
                row.Spec,
                string.Empty,               // 형상
                row.Count.ToString(),
                string.Empty,               // 비고
            };
            for (var col = 0; col < Columns.Length; col++)
                PlaceCellText(document, view, textTypeId, values[col], col, i + 1, Columns[col].Centered);
        }
    }

    /// <summary>칸(<paramref name="col"/>, <paramref name="row"/>) 안에 문자를 놓는다. row 0이 머리행이다.</summary>
    private static void PlaceCellText(Document document, ViewDrafting view, ElementId textTypeId,
        string text, int col, int row, bool centered)
    {
        if (string.IsNullOrEmpty(text)) return;   // 빈 문자열로 TextNote를 만들면 Revit이 예외를 낸다.

        var left = Columns.Take(col).Sum(x => x.WidthMm);
        var x = centered ? left + Columns[col].WidthMm / 2 : left + TextPaddingMm;
        var y = -(row * RowHeightMm + RowHeightMm / 2);
        PlaceText(document, view, textTypeId, text, x, y, centered);
    }

    private static void PlaceText(Document document, ViewDrafting view, ElementId textTypeId,
        string text, double xMm, double yMm, bool centered)
    {
        var options = new TextNoteOptions(textTypeId)
        {
            HorizontalAlignment = centered ? HorizontalTextAlignment.Center : HorizontalTextAlignment.Left,
            VerticalAlignment = VerticalTextAlignment.Middle,
        };
        TextNote.Create(document, view.Id, new XYZ(UC.MmToFt(xMm), UC.MmToFt(yMm), 0), text, options);
    }

    private static void DrawLine(Document document, ViewDrafting view,
        double x1Mm, double y1Mm, double x2Mm, double y2Mm)
    {
        var start = new XYZ(UC.MmToFt(x1Mm), UC.MmToFt(y1Mm), 0);
        var end = new XYZ(UC.MmToFt(x2Mm), UC.MmToFt(y2Mm), 0);
        document.Create.NewDetailCurve(view, Line.CreateBound(start, end));
    }
}
