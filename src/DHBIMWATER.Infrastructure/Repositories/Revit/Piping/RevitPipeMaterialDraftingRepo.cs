using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;

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
        ("형상", 30, true),
        ("수량", 14, true),
        ("비고", 30, false),  // 직관·단관은 관 재료 코드가 자동으로 들어온다(docs/141 § B-7). 나머지는 빈 칸.
    ];

    private const double RowHeightMm = 9;
    private const double TitleGapMm = 6;      // 표 상단선과 제목 문자 사이 간격
    private const double TextPaddingMm = 2;   // 좌측 정렬 칸의 안쪽 여백

    private readonly Func<Document?> _document;
    public RevitPipeMaterialDraftingRepo(Func<Document?> document) => _document = document;

    public IReadOnlyList<PipeMaterialPart> GetParts()
        => PipeMaterialPartCollector.Read(Document());

    public IReadOnlySet<string> GetExistingViewNames()
        => new FilteredElementCollector(Document())
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(x => !x.IsTemplate)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

    public void CreateTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true)
    {
        var document = Document();
        // 격점 하나의 실패가 나머지 격점까지 되돌리지 않도록 그 격점의 변경만 묶는다(docs/208 § C-2).
        using var sub = new SubTransaction(document);
        sub.Start();
        try { DrawTable(document, CreateView(document, viewName), title, rows, includeTypeImage); sub.Commit(); }
        catch { sub.RollBack(); throw; }
    }

    public bool RedrawTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true)
    {
        var document = Document();

        // 이름은 있는데 드래프팅뷰가 아니면(다른 종류의 뷰가 그 이름을 쓰는 중) 아무것도 지우지 않는다.
        // 여기서 예외를 던지면 UseCase가 트랜잭션을 통째로 롤백해 정상인 다른 격점 도면까지 날아간다(docs/173 § B).
        var view = new FilteredElementCollector(document)
            .OfClass(typeof(ViewDrafting))
            .Cast<ViewDrafting>()
            .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, viewName, StringComparison.Ordinal));
        if (view is null) return false;

        // 축척을 1로 되돌린다 — 이 클래스의 mm 값은 1:1을 전제로 하고(CreateView 주석),
        // 문자 크기(용지 mm)와 칸 폭의 눈금도 1:1에서만 맞는다. 사용자가 축척을 바꿔 놨다면 표가 이미 깨져 있다.
        // 지우기 전에 판정한다 — 축척이 뷰 템플릿에 잠겨 있으면 Scale setter가 던지고, 그때 뷰가 비어 있으면 안 된다(docs/208 § C-3).
        if (view.Scale != 1 && view.ViewTemplateId != ElementId.InvalidElementId)
            throw new DhbimwaterException(ErrorCodes.MT_ViewScaleLocked,
                $"드래프팅뷰 '{view.Name}'에 뷰 템플릿이 적용돼 축척을 1:1로 바꿀 수 없습니다. 템플릿을 해제한 뒤 다시 실행하세요.");

        // 격점 하나의 실패가 나머지 격점까지 되돌리지 않도록 그 격점의 변경만 묶는다(docs/208 § C-2).
        // ClearView 뒤 그리기가 실패하면 뷰가 비어 버리므로 반드시 함께 되돌린다.
        using var sub = new SubTransaction(document);
        sub.Start();
        try
        {
            ClearView(document, view);
            if (view.Scale != 1) view.Scale = 1;
            DrawTable(document, view, title, rows, includeTypeImage);
            sub.Commit();
        }
        catch { sub.RollBack(); throw; }
        return true;
    }

    /// <summary>뷰가 소유한 요소를 전부 지운다(docs/173 — 전체 삭제 후 재작도).
    /// <para>사용자가 이 뷰에 덧붙인 치수·상세항목·주석도 함께 사라진다. 실행 전 사용자 확인이 유일한 방어선이다.</para>
    /// <para>한 번의 <see cref="Document.Delete(ICollection{ElementId})"/>로 넘겨야 종속 요소를
    /// Revit이 알아서 함께 처리한다. 하나씩 지우면 이미 사라진 ID를 다시 지우려다 예외가 난다.</para></summary>
    private static void ClearView(Document document, ViewDrafting view)
    {
        // 뷰 ID로 좁힌 collector는 그 뷰에 보이는 요소 = 드래프팅뷰가 소유한 요소다.
        // WhereElementIsNotElementType()로 유형(TextNoteType 등)은 제외한다 — 유형을 지우면 문서 전체가 망가진다.
        var owned = new FilteredElementCollector(document, view.Id)
            .WhereElementIsNotElementType()
            .ToElementIds();
        if (owned.Count == 0) return;
        document.Delete(owned);
    }

    /// <summary>표 본체를 그린다. 생성(<see cref="CreateTableView"/>)과 갱신(<see cref="RedrawTableView"/>)이 공유한다 —
    /// 두 경로가 갈라지면 같은 격점의 새 표와 다시 그린 표가 서로 달라진다.
    /// <para>머리행 1줄 + 자료행 n줄. 좌표 원점은 표 좌상단이고, 아래로 갈수록 Y가 작아진다.</para></summary>
    private static void DrawTable(Document document, ViewDrafting view, string title,
        IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true)
    {
        var lineCount = rows.Count + 1;
        var totalWidthMm = Columns.Sum(x => x.WidthMm);
        var totalHeightMm = lineCount * RowHeightMm;

        DrawGrid(document, view, totalWidthMm, totalHeightMm, lineCount);
        DrawTexts(document, view, title, rows, includeTypeImage);
    }

    private Document Document()
        => _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");

    private static ViewDrafting CreateView(Document document, string viewName)
    {
        var draftingType = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting)
            ?? throw new DhbimwaterException(ErrorCodes.MT_DraftingViewTypeMissing,
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
        IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage)
    {
        var textTypeId = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        if (textTypeId == ElementId.InvalidElementId)
            throw new DhbimwaterException(ErrorCodes.MT_TextNoteTypeMissing,
                "기본 문자 유형(TextNoteType)이 프로젝트에 없습니다. 템플릿에 문자 유형을 추가하세요.");

        // 제목은 표 좌상단 위에 왼쪽 정렬로 놓는다.
        PlaceText(document, view, textTypeId, title, 0, TitleGapMm, centered: false);

        // 머리행
        for (var col = 0; col < Columns.Length; col++)
            PlaceCellText(document, view, textTypeId, Columns[col].Heading, col, row: 0, centered: true);

        // 자료행. 형상 칸은 유형 이미지를 놓는다.
        // 비고는 모델의 DH_비고 값을 그대로 그린다 — 일람표와 같은 값이어야 한다(docs/141 § B-7).
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
                row.Remark,                 // 비고
            };
            for (var col = 0; col < Columns.Length; col++)
                PlaceCellText(document, view, textTypeId, values[col], col, i + 1, Columns[col].Centered);
            if (includeTypeImage) PlaceShapeImage(document, view, row, i + 1);
        }
    }

    private static void PlaceShapeImage(Document document, ViewDrafting view, PipeMaterialRow row, int tableRow)
    {
        if (row.SymbolTypeId <= 0 || document.GetElement(new ElementId(row.SymbolTypeId)) is not FamilySymbol symbol)
            return;
        var imageTypeId = symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_IMAGE)?.AsElementId();
        if (imageTypeId is null || imageTypeId == ElementId.InvalidElementId || document.GetElement(imageTypeId) is not ImageType)
            return;

        const int shapeColumn = 3;
        var xMm = Columns.Take(shapeColumn).Sum(x => x.WidthMm) + Columns[shapeColumn].WidthMm / 2;
        var yMm = -(tableRow * RowHeightMm + RowHeightMm / 2);
        var center = new XYZ(UC.MmToFt(xMm), UC.MmToFt(yMm), 0);
        var image = ImageInstance.Create(document, view, imageTypeId,
            new ImagePlacementOptions(center, BoxPlacement.Center));
        var maxWidth = UC.MmToFt(Columns[shapeColumn].WidthMm - 2);
        var maxHeight = UC.MmToFt(RowHeightMm - 2);
        if (image.Width <= 0 || image.Height <= 0) return;
        var factor = Math.Min(maxWidth / image.Width, maxHeight / image.Height);
        image.LockProportions = true;
        image.Width *= factor;
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
