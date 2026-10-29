using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Infrastructure.Converters;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>격점상세도 시트 1장 = 좌상단 평면 + 좌하단 3D + 우측 드래프팅(배관재료표 도면).
/// 같은 이름 시트에 뷰 다 있으면 손대지 않음, 빠진 뷰만 추가·배치.</summary>
internal sealed class RevitJunctionDetailSheetRepo : IJunctionDetailSheetRepo
{
    // A3 도곽 기준 축척. 평면은 격점상세도 창 축척 그대로
    private const int ThreeDScale = 50;
    private const string ViewportTypeName = "제목 없음";
    private const string ViewportTypeNameEn = "No Title";
    // 도곽 바깥선 안쪽 여백·뷰 간격(mm)
    private const double MarginMm = 25;
    private const double GapMm = 10;

    private readonly Func<Document?> _document;

    public RevitJunctionDetailSheetRepo(Func<Document?> document) => _document = document;

    public JunctionDetailSheetChange CreateOrUpdateSheet(JunctionDetailSheetRequest request)
    {
        var document = _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var warnings = new List<string>();
        var plan = FindView<ViewPlan>(document, request.PlanViewName, x => x.ViewType == ViewType.FloorPlan);
        var drafting = FindView<ViewDrafting>(document, request.DraftingViewName, _ => true);
        var threeD = FindView<View3D>(document, request.ThreeDViewName, x => !x.IsPerspective);
        var sheet = new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .FirstOrDefault(x => !x.IsPlaceholder && string.Equals(x.Name, request.SheetName, StringComparison.Ordinal));

        // 이미 다 배치 = 시트·3D 설정 모두 그대로(사용자 수정 보존)
        var placed = sheet?.GetAllPlacedViews() ?? new HashSet<ElementId>();
        bool Missing(View? view) => view is null || !placed.Contains(view.Id);
        if (sheet is not null && threeD is not null && !Missing(threeD) && (plan is null || !Missing(plan))
            && (drafting is null || !Missing(drafting)))
        {
            warnings.AddRange(AbsentWarnings(request, plan, drafting));
            return new JunctionDetailSheetChange(JunctionDetailSheetStatus.Unchanged, warnings);
        }

        using var sub = new SubTransaction(document);
        sub.Start();
        try
        {
            var created = sheet is null;
            sheet ??= CreateSheet(document, request, warnings);
            var instances = request.Group.Nodes.Select(x => document.GetElement(new ElementId(x.Id)) as FamilyInstance)
                .OfType<FamilyInstance>().ToList();
            if (threeD is null || Missing(threeD))
                threeD = Configure3D(document, threeD ?? Create3D(document, request.ThreeDViewName), instances);
            warnings.AddRange(AbsentWarnings(request, plan, drafting));

            // 새로 놓은 뷰포트만 배치·유형 지정. 이미 놓인 뷰포트 위치는 사용자 몫
            var planPort = Place(document, sheet, plan, placed, warnings);
            var threeDPort = Place(document, sheet, threeD, placed, warnings);
            var draftingPort = Place(document, sheet, drafting, placed, warnings);
            var added = new[] { planPort, threeDPort, draftingPort }.OfType<Viewport>().ToList();
            if (added.Count == 0)
            {
                sub.RollBack();
                return new JunctionDetailSheetChange(JunctionDetailSheetStatus.Unchanged, warnings);
            }
            ApplyViewportType(document, added, warnings);
            document.Regenerate();
            Arrange(document, sheet, plan, threeD, drafting, added);
            sub.Commit();
            return new JunctionDetailSheetChange(
                created ? JunctionDetailSheetStatus.Created : JunctionDetailSheetStatus.Updated, warnings);
        }
        catch { sub.RollBack(); throw; }
    }

    private static T? FindView<T>(Document document, string name, Func<T, bool> predicate) where T : View
        => new FilteredElementCollector(document).OfClass(typeof(T)).Cast<T>()
            .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, name, StringComparison.Ordinal) && predicate(x));

    private static IEnumerable<string> AbsentWarnings(JunctionDetailSheetRequest request, View? plan, View? drafting)
    {
        if (plan is null) yield return $"{request.SheetName}: 평면뷰 '{request.PlanViewName}'가 없어 배치하지 않았습니다.";
        if (drafting is null) yield return $"{request.SheetName}: 드래프팅뷰 '{request.DraftingViewName}'가 없어 배치하지 않았습니다.";
    }

    private static ViewSheet CreateSheet(Document document, JunctionDetailSheetRequest request, List<string> warnings)
    {
        var titleBlock = FindTitleBlock(document);
        if (titleBlock is null) warnings.Add($"{request.SheetName}: 제목 블록이 없어 도곽 없이 시트를 만들었습니다.");
        else if (!titleBlock.IsActive) titleBlock.Activate();
        var sheet = ViewSheet.Create(document, titleBlock?.Id ?? ElementId.InvalidElementId);
        sheet.Name = request.SheetName;
        sheet.SheetNumber = UniqueSheetNumber(document, request.SheetNumber);
        return sheet;
    }

    // A3 이름 든 제목 블록 우선, 없으면 첫 제목 블록
    private static FamilySymbol? FindTitleBlock(Document document)
    {
        var symbols = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .OrderBy(x => x.FamilyName, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
        return symbols.FirstOrDefault(x => $"{x.FamilyName} {x.Name}".Contains("A3", StringComparison.OrdinalIgnoreCase))
            ?? symbols.FirstOrDefault();
    }

    private static string UniqueSheetNumber(Document document, string number)
    {
        var used = new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Select(x => x.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(number)) return number;
        for (var i = 2; ; i++)
            if (!used.Contains($"{number}_{i}")) return $"{number}_{i}";
    }

    private static View3D Create3D(Document document, string name)
    {
        var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional)
            ?? throw new InvalidOperationException("프로젝트에 3D 뷰 유형이 없습니다.");
        var view = View3D.CreateIsometric(document, type.Id);
        view.Name = name;
        // 유형 기본 템플릿이 가시성 덮어쓰지 않게 해제
        if (view.ViewTemplateId != ElementId.InvalidElementId) view.ViewTemplateId = ElementId.InvalidElementId;
        return view;
    }

    // 레벨 끔 + 단면 상자 윗면을 부재 바로 위로 → 상부 슬래브 잘림
    private static View3D Configure3D(Document document, View3D view, IReadOnlyList<FamilyInstance> instances)
    {
        if (view.ViewTemplateId != ElementId.InvalidElementId)
            throw new InvalidOperationException($"'{view.Name}'의 뷰 템플릿을 해제한 뒤 다시 실행하세요.");
        view.Scale = ThreeDScale;
        view.DetailLevel = ViewDetailLevel.Medium;
        view.DisplayStyle = DisplayStyle.HLR;
        foreach (var category in new[] { BuiltInCategory.OST_Levels, BuiltInCategory.OST_SectionBox })
        {
            var id = new ElementId(category);
            if (view.CanCategoryBeHidden(id)) view.SetCategoryHidden(id, true);
        }
        if (instances.Count == 0) return view;

        var bounds = RevitJunctionDetailRepo.Bounds(instances);
        var minX = bounds.Min.X;
        var minY = bounds.Min.Y;
        var maxX = bounds.Max.X;
        var maxY = bounds.Max.Y;
        // 밸브실 벽 전체 포함. 축 = 세계 X·Y라 mm 좌표 그대로 ft 변환
        var room = RevitJunctionDetailRepo.FindValveRoom(document, instances, null, XYZ.BasisX, XYZ.BasisY).Room;
        if (room is not null)
        {
            minX = Math.Min(minX, RevitUnitConverter.MmToFt(room.OuterMinX));
            minY = Math.Min(minY, RevitUnitConverter.MmToFt(room.OuterMinY));
            maxX = Math.Max(maxX, RevitUnitConverter.MmToFt(room.OuterMaxX));
            maxY = Math.Max(maxY, RevitUnitConverter.MmToFt(room.OuterMaxY));
        }
        var margin = RevitUnitConverter.MmToFt(500);
        // TODO: 부재가 슬래브 하면 300mm 안까지 붙으면 슬래브 일부 보임. 필요 시 상부 바닥 숨김 병행
        view.SetSectionBox(new BoundingBoxXYZ
        {
            Min = new XYZ(minX - margin, minY - margin, bounds.Min.Z - margin),
            Max = new XYZ(maxX + margin, maxY + margin, bounds.Max.Z + RevitUnitConverter.MmToFt(300))
        });
        view.IsSectionBoxActive = true;
        return view;
    }

    private static Viewport? Place(Document document, ViewSheet sheet, View? view, ISet<ElementId> placed,
        List<string> warnings)
    {
        if (view is null || placed.Contains(view.Id)) return null;
        // 평면·3D·드래프팅은 시트 1장에만 놓임 → 다른 시트에 있으면 건너뜀
        if (!Viewport.CanAddViewToSheet(document, sheet.Id, view.Id))
        {
            warnings.Add($"{sheet.Name}: '{view.Name}'가 다른 시트에 배치돼 있어 추가하지 않았습니다.");
            return null;
        }
        return Viewport.Create(document, sheet.Id, view.Id, XYZ.Zero);
    }

    private static void ApplyViewportType(Document document, IReadOnlyList<Viewport> viewports, List<string> warnings)
    {
        foreach (var viewport in viewports)
        {
            var type = viewport.GetValidTypes().Select(document.GetElement).OfType<ElementType>()
                .FirstOrDefault(x => x.Name == ViewportTypeName)
                ?? viewport.GetValidTypes().Select(document.GetElement).OfType<ElementType>()
                    .FirstOrDefault(x => x.Name.Equals(ViewportTypeNameEn, StringComparison.OrdinalIgnoreCase));
            if (type is null)
            {
                warnings.Add($"뷰포트 유형 '{ViewportTypeName}'이 없어 기본 유형으로 두었습니다.");
                return;
            }
            viewport.ChangeTypeId(type.Id);
        }
    }

    // 좌상단 평면, 좌하단 3D, 왼쪽 열 오른쪽에 드래프팅(위 맞춤). 도곽 오른쪽 표제란 위치 몰라 왼쪽 열 기준으로 붙임
    private static void Arrange(Document document, ViewSheet sheet, View? plan, View? threeD, View? drafting,
        IReadOnlyList<Viewport> added)
    {
        var (frameMin, frameMax) = Frame(document, sheet);
        var margin = RevitUnitConverter.MmToFt(MarginMm);
        var gap = RevitUnitConverter.MmToFt(GapMm);
        var left = frameMin.X + margin;
        var top = frameMax.Y - margin;
        var bottom = frameMin.Y + margin;
        var ports = new FilteredElementCollector(document, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>().ToList();
        Viewport? PortOf(View? view) => view is null ? null : ports.FirstOrDefault(x => x.ViewId == view.Id);
        bool IsNew(Viewport? port) => port is not null && added.Any(x => x.Id == port.Id);

        var planPort = PortOf(plan);
        if (IsNew(planPort)) MoveCorner(planPort!, left, top, alignTop: true);
        var threeDPort = PortOf(threeD);
        if (IsNew(threeDPort)) MoveCorner(threeDPort!, left, bottom, alignTop: false);
        var draftingPort = PortOf(drafting);
        if (!IsNew(draftingPort)) return;
        var column = new[] { planPort, threeDPort }.OfType<Viewport>().Select(x => x.GetBoxOutline().MaximumPoint.X)
            .DefaultIfEmpty(left - gap).Max();
        MoveCorner(draftingPort!, column + gap, top, alignTop: true);
    }

    // 뷰포트 상자 왼쪽 위(또는 왼쪽 아래)를 지정 점에 맞춤
    private static void MoveCorner(Viewport viewport, double x, double y, bool alignTop)
    {
        var box = viewport.GetBoxOutline();
        var width = box.MaximumPoint.X - box.MinimumPoint.X;
        var height = box.MaximumPoint.Y - box.MinimumPoint.Y;
        var centerY = alignTop ? y - height / 2 : y + height / 2;
        viewport.SetBoxCenter(new XYZ(x + width / 2, centerY, 0));
    }

    // 도곽 범위 = 제목 블록 경계 상자. 없으면 시트 Outline, 그것도 비면 A3(420×297) 가정
    private static (XYZ Min, XYZ Max) Frame(Document document, ViewSheet sheet)
    {
        var titleBlock = new FilteredElementCollector(document, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType().FirstElement();
        if (titleBlock?.get_BoundingBox(sheet) is { } box) return (box.Min, box.Max);
        var outline = sheet.Outline;
        if (outline.Max.U - outline.Min.U > 1e-6 && outline.Max.V - outline.Min.V > 1e-6)
            return (new XYZ(outline.Min.U, outline.Min.V, 0), new XYZ(outline.Max.U, outline.Max.V, 0));
        return (XYZ.Zero, new XYZ(RevitUnitConverter.MmToFt(420), RevitUnitConverter.MmToFt(297), 0));
    }
}
