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
    private const string ViewportTypeName = "제목 없음";
    private const string ViewportTypeNameEn = "No Title";
    // 재료표 1:1·배치 치수 기준 도곽 폭(A1 가로). 다른 도곽은 폭 비율로 줄임(A3 = 절반)
    private const double DraftingBaseWidthMm = 841;
    // 배치 영역 = 도곽 바깥선(제목 블록 경계 상자)에서 안쪽으로 이만큼(A1 mm). 격점상세 기본 도곽 패밀리 값(2026-11-03 사용자)
    private const double AreaLeftMm = 25;
    private const double AreaTopMm = 20;
    private const double AreaRightMm = 19;
    private const double AreaBottomMm = 50;
    // 뷰 상자 모서리 ~ 배치 영역 모서리 거리, 뷰끼리 최소 간격(A1 mm). 상자 딱 붙이지 않게 여유
    private const double ViewOffsetMm = 20;
    private const double ViewGapMm = 20;
    // 배치 규칙 바꾸면 올림 → 이전 규칙으로 놓인 시트 한 번 다시 배치
    private const string LayoutVersion = "area-v1";
    // 겹침 피해 밀어내기 최대 횟수
    private const int MaxShift = 10;

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

        // 이미 다 배치 + 축척 같음 = 시트·3D 설정 모두 그대로(사용자 수정 보존)
        var placed = sheet?.GetAllPlacedViews() ?? new HashSet<ElementId>();
        bool Missing(View? view) => view is null || !placed.Contains(view.Id);
        // 평면 축척은 평면 단계가 이미 바꿈 → 3D 축척 다르면 축척 바뀐 실행.
        // 드래프팅 축척은 도곽 크기로 정함 → 사용자가 도곽 바꿨으면 다시 맞추고 재배치
        // 배치 규칙 버전 다른 시트(이전 규칙 배치)도 전부 다시 배치
        var scaleChanged = (threeD is not null && threeD.Scale != request.ViewScale)
            || (sheet is not null && drafting is not null && drafting.Scale != DraftingScale(document, sheet))
            || (sheet is not null && JunctionDetailSheetStorageSchema.ReadLayoutVersion(sheet) != LayoutVersion);
        if (sheet is not null && threeD is not null && !Missing(threeD) && (plan is null || !Missing(plan))
            && (drafting is null || !Missing(drafting)) && !scaleChanged)
        {
            warnings.AddRange(AbsentWarnings(request, plan, drafting));
            return new JunctionDetailSheetChange(JunctionDetailSheetStatus.Unchanged, warnings);
        }

        using var sub = new SubTransaction(document);
        sub.Start();
        try
        {
            var created = sheet is null;
            if (sheet is null)
            {
                sheet = CreateSheet(document, request, warnings);
                // 새 도곽 경계 상자 읽으려면 재생성 필요
                document.Regenerate();
            }
            var instances = request.Group.Nodes.Select(x => document.GetElement(new ElementId(x.Id)) as FamilyInstance)
                .OfType<FamilyInstance>().ToList();
            if (threeD is null || Missing(threeD))
                threeD = Configure3D(document, threeD ?? Create3D(document, request.ThreeDViewName), instances, request.ViewScale);
            else if (threeD.Scale != request.ViewScale) SetScale(threeD, request.ViewScale);
            if (drafting is not null) SetDraftingScale(drafting, DraftingScale(document, sheet), warnings);
            warnings.AddRange(AbsentWarnings(request, plan, drafting));

            // 새로 놓은 뷰포트만 배치·유형 지정. 이미 놓인 뷰포트 위치는 사용자 몫 — 축척 바뀐 때만 전부 다시 배치
            var planPort = Place(document, sheet, plan, placed, warnings);
            var threeDPort = Place(document, sheet, threeD, placed, warnings);
            var draftingPort = Place(document, sheet, drafting, placed, warnings);
            var added = new[] { planPort, threeDPort, draftingPort }.OfType<Viewport>().ToList();
            if (added.Count == 0 && !scaleChanged)
            {
                sub.RollBack();
                return new JunctionDetailSheetChange(JunctionDetailSheetStatus.Unchanged, warnings);
            }
            ApplyViewportType(document, added, warnings);
            document.Regenerate();
            Arrange(document, sheet, plan, threeD, drafting, scaleChanged ? null : added, warnings);
            JunctionDetailSheetStorageSchema.WriteLayoutVersion(sheet, LayoutVersion);
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

    public IReadOnlyList<JunctionDetailTitleBlock> GetTitleBlocks()
    {
        var document = _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        return TitleBlocks(document).Select(x => new JunctionDetailTitleBlock(x.Id.Value, $"{x.FamilyName} : {x.Name}")).ToList();
    }

    private static ViewSheet CreateSheet(Document document, JunctionDetailSheetRequest request, List<string> warnings)
    {
        // 창에서 고른 도곽 우선. 지워졌으면 A1 자동
        var chosen = request.TitleBlockId > 0
            ? document.GetElement(new ElementId(request.TitleBlockId)) as FamilySymbol : null;
        if (request.TitleBlockId > 0 && chosen is null)
            warnings.Add($"{request.SheetName}: 고른 도곽을 찾을 수 없어 A1 도곽을 자동으로 골랐습니다.");
        var titleBlock = chosen ?? FindTitleBlock(document);
        if (titleBlock is null) warnings.Add($"{request.SheetName}: 제목 블록이 없어 도곽 없이 시트를 만들었습니다.");
        else if (!titleBlock.IsActive) titleBlock.Activate();
        var sheet = ViewSheet.Create(document, titleBlock?.Id ?? ElementId.InvalidElementId);
        sheet.Name = request.SheetName;
        sheet.SheetNumber = UniqueSheetNumber(document, request.SheetNumber);
        return sheet;
    }

    // A1 이름 든 제목 블록 우선, 없으면 첫 제목 블록
    private static FamilySymbol? FindTitleBlock(Document document)
    {
        var symbols = TitleBlocks(document);
        return symbols.FirstOrDefault(x => $"{x.FamilyName} {x.Name}".Contains(
                JunctionDetailParameters.DefaultTitleBlockKeyword, StringComparison.OrdinalIgnoreCase))
            ?? symbols.FirstOrDefault();
    }

    private static List<FamilySymbol> TitleBlocks(Document document)
        => new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .OrderBy(x => x.FamilyName, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();

    // 재료표는 A1 크기로 1:1 그림(RevitPipeMaterialDraftingRepo). 작은 도곽은 축척으로 줄임: A1 1:1, A3 1:2.
    // 문자는 축척 따라 안 줄어 축척 바꿀 때 유형도 바꿈(A1 5mm, A3 2.5mm)
    private static int DraftingScale(Document document, ViewSheet sheet)
    {
        var (min, max) = Frame(document, sheet);
        var widthMm = RevitUnitConverter.FtToMm(max.X - min.X);
        return widthMm <= 0 ? 1 : Math.Max(1, (int)Math.Round(DraftingBaseWidthMm / widthMm));
    }

    private static void SetDraftingScale(View view, int scale, List<string> warnings)
    {
        if (view.Scale == scale) return;
        if (view.ViewTemplateId != ElementId.InvalidElementId)
        {
            warnings.Add($"'{view.Name}'에 뷰 템플릿이 있어 재료표 축척을 1:{scale}로 바꾸지 못했습니다.");
            return;
        }
        view.Scale = scale;
        // 문자는 축척 따라 안 줄어 → 축척 맞는 크기 유형으로(A1 5mm, A3 2.5mm)
        if (view is ViewDrafting drafting) RevitPipeMaterialDraftingRepo.ApplyTextSize(view.Document, drafting);
    }

    private static void SetScale(View view, int scale)
    {
        if (view.ViewTemplateId != ElementId.InvalidElementId)
            throw new InvalidOperationException($"'{view.Name}'의 뷰 템플릿을 해제한 뒤 다시 실행하세요.");
        view.Scale = scale;
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
    private static View3D Configure3D(Document document, View3D view, IReadOnlyList<FamilyInstance> instances, int scale)
    {
        SetScale(view, scale);
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

    // 배치 영역(도곽 안쪽) 모서리 기준. 뷰포트 상자(GetBoxOutline) 좌상단 = 평면, 좌하단 = 3D, 우상단 = 드래프팅,
    // 각각 영역 같은 모서리에서 ViewOffset 만큼 안쪽. 놓을 때마다 이미 놓인 상자와 겹치면 오른쪽으로 밀어냄. 영역 밖·겹침 남으면 경고.
    // added null = 축척 바뀜 → 세 뷰포트 전부 다시 배치. 안 옮기는 뷰포트(사용자 위치)는 장애물로만 씀
    private static void Arrange(Document document, ViewSheet sheet, View? plan, View? threeD, View? drafting,
        IReadOnlyList<Viewport>? added, List<string> warnings)
    {
        var (frameMin, frameMax) = Frame(document, sheet);
        // A1 mm → 이 도곽 ft. A3면 절반
        var factor = (frameMax.X - frameMin.X) / RevitUnitConverter.MmToFt(DraftingBaseWidthMm);
        double Ft(double a1Mm) => RevitUnitConverter.MmToFt(a1Mm) * factor;
        var gap = Ft(ViewGapMm);
        var offset = Ft(ViewOffsetMm);
        // 배치 영역 경계. 아래 앵커는 영역 모서리 + offset
        var right = frameMax.X - Ft(AreaRightMm);
        var bottom = frameMin.Y + Ft(AreaBottomMm);
        var top = frameMax.Y - Ft(AreaTopMm);
        var areaLeft = frameMin.X + Ft(AreaLeftMm);
        var left = areaLeft + offset;
        var ports = new FilteredElementCollector(document, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>().ToList();
        Viewport? PortOf(View? view) => view is null ? null : ports.FirstOrDefault(x => x.ViewId == view.Id);
        bool IsNew(Viewport? port) => port is not null && (added is null || added.Any(x => x.Id == port.Id));

        var planPort = PortOf(plan);
        var threeDPort = PortOf(threeD);
        var draftingPort = PortOf(drafting);
        var moving = new[] { planPort, threeDPort, draftingPort }.Where(IsNew).Select(x => x!.Id).ToHashSet();
        // 장애물 = 시트의 다른 뷰포트 전부(안 옮기는 것). 놓은 순서대로 추가
        var placed = ports.Where(x => !moving.Contains(x.Id)).Select(Box).ToList();
        var moved = new Dictionary<ElementId, Rect>();

        if (IsNew(planPort)) Put(planPort!, left, top - offset, alignTop: true);
        if (IsNew(threeDPort)) Put(threeDPort!, left, bottom + offset, alignTop: false);
        if (IsNew(draftingPort))
        {
            // 드래프팅 = 상자 우상단을 영역 우상단 − offset에 고정(오른쪽 정렬). 겹쳐도 안 밀고 경고만 —
            // 밀면 오른쪽 영역 밖으로 나감. 경고에 폭 적어 원인 확인용
            var box = Box(draftingPort!);
            var width = box.MaxX - box.MinX;
            Put(draftingPort!, right - offset - width, top - offset, alignTop: true, shift: false);
            var column = new[] { planPort, threeDPort }.OfType<Viewport>()
                .Select(x => moved.TryGetValue(x.Id, out var r) ? r : Box(x)).ToList();
            if (column.Count > 0 && column.Max(x => x.MaxX) + gap > right - offset - width)
                warnings.Add($"{sheet.Name}: 재료표 폭 {Mm(width):0}mm, 평면·3D 오른쪽 끝 {Mm(column.Max(x => x.MaxX) - frameMin.X):0}mm(도곽 왼쪽 기준) — 공간이 모자라 겹칠 수 있습니다.");
        }

        double Mm(double ft) => RevitUnitConverter.FtToMm(ft);

        void Put(Viewport port, double x, double y, bool alignTop, bool shift = true)
        {
            var box = Box(port);
            var (w, h) = (box.MaxX - box.MinX, box.MaxY - box.MinY);
            var target = new Rect(x, alignTop ? y - h : y, x + w, alignTop ? y : y + h);
            // 겹치는 상자 오른쪽으로 밀어냄. 밀어낸 자리에서 또 겹치면 반복
            for (var i = 0; shift && i < MaxShift && placed.FirstOrDefault(o => o.Overlaps(target, gap)) is { } hit; i++)
                target = target with { MinX = hit.MaxX + gap, MaxX = hit.MaxX + gap + w };
            port.SetBoxCenter(new XYZ((target.MinX + target.MaxX) / 2, (target.MinY + target.MaxY) / 2, 0));
            placed.Add(target);
            moved[port.Id] = target;
            if (target.MinX < areaLeft - 1e-6 || target.MaxX > right + 1e-6 || target.MinY < bottom - 1e-6
                || target.MaxY > top + 1e-6 || placed.Take(placed.Count - 1).Any(o => o.Overlaps(target, 0)))
                warnings.Add($"{sheet.Name}: '{document.GetElement(port.ViewId)?.Name}' 뷰가 배치 영역에 다 안 들어가거나 다른 뷰와 겹칩니다. 축척을 줄이거나 위치를 손으로 옮기세요.");
        }
    }

    private static Rect Box(Viewport viewport)
    {
        var box = viewport.GetBoxOutline();
        return new Rect(box.MinimumPoint.X, box.MinimumPoint.Y, box.MaximumPoint.X, box.MaximumPoint.Y);
    }

    // 시트 좌표(ft) 상자. gap 만큼 띄워야 안 겹친 것으로 봄
    private readonly record struct Rect(double MinX, double MinY, double MaxX, double MaxY)
    {
        public bool Overlaps(Rect other, double gap)
            => MinX < other.MaxX + gap && other.MinX < MaxX + gap && MinY < other.MaxY + gap && other.MinY < MaxY + gap;
    }

    // 도곽 범위 = 제목 블록 경계 상자. 없으면 시트 Outline, 그것도 비면 A1(841×594) 가정
    private static (XYZ Min, XYZ Max) Frame(Document document, ViewSheet sheet)
    {
        var titleBlock = new FilteredElementCollector(document, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType().FirstElement();
        if (titleBlock?.get_BoundingBox(sheet) is { } box) return (box.Min, box.Max);
        var outline = sheet.Outline;
        if (outline.Max.U - outline.Min.U > 1e-6 && outline.Max.V - outline.Min.V > 1e-6)
            return (new XYZ(outline.Min.U, outline.Min.V, 0), new XYZ(outline.Max.U, outline.Max.V, 0));
        return (XYZ.Zero, new XYZ(RevitUnitConverter.MmToFt(841), RevitUnitConverter.MmToFt(594), 0));
    }
}
