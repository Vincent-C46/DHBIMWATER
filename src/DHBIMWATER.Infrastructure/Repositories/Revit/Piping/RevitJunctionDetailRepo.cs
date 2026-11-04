using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Application.UseCases.Documentation;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Infrastructure.Converters;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

internal sealed class RevitJunctionDetailRepo : IJunctionDetailRepo
{
    private readonly Func<Document?> _document;

    public RevitJunctionDetailRepo(Func<Document?> document) => _document = document;

    public IReadOnlyList<JunctionDetailGroup> GetJunctions()
    {
        var document = Document();
        var instances = PipeMaterialPartCollector.AllPipingInstances(document).ToList();
        if (instances.Count == 0) return [];
        var ids = instances.Select(JunctionId).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var junctionId = ids.FirstOrDefault() ?? string.Empty;
        IReadOnlyList<string> warnings = ids.Count > 1
            ? [$"문서에 격점번호가 {ids.Count}종 있어 하나로 묶었습니다"] : [];
        var connectors = instances.ToDictionary(x => x.Id.Value, PipeMaterialConnectorReader.EndConnectors);
        var connectorIndex = new Dictionary<(long Element, int Connector), int>();
        foreach (var instance in instances)
        for (var i = 0; i < connectors[instance.Id.Value].Count; i++)
            connectorIndex[(instance.Id.Value, connectors[instance.Id.Value][i].Id)] = i;

        return [new JunctionDetailGroup(junctionId,
                instances.Select(x => new JunctionNode(x.Id.Value,
                    connectors[x.Id.Value].Select(connector =>
                    {
                        var direction = connector.CoordinateSystem.BasisZ.Normalize();
                        var partner = PipeMaterialConnectorReader.Partner(connector, x.Id.Value, connectorIndex);
                        return new JunctionConnector(direction.X, direction.Y, direction.Z, partner.Id);
                    }).ToList())).ToList(), warnings)];
    }

    public IReadOnlyList<JunctionPreviewItem> GetPreviewItems(JunctionDetailGroup group)
    {
        var document = Document();
        var chain = JunctionVerticalChain.Resolve(group.JunctionId, group.Nodes);
        var order = chain.Slots.ToDictionary(x => x.ElementId, x => x.Order);
        var orphans = chain.Orphans.ToHashSet();
        var plan = FindPlan(document, group.JunctionId);
        var upDirection = plan?.UpDirection ?? XYZ.BasisY;
        var rightDirection = plan?.RightDirection ?? XYZ.BasisX;
        return Instances(document, group).Select(instance =>
        {
            string Read(string name) => (ValvePipingParameters.Guids.TryGetValue(name, out var guid)
                ? instance.get_Parameter(guid) : null)?.AsString() ?? instance.LookupParameter(name)?.AsString() ?? string.Empty;
            var transform = instance.GetTransform();
            var rotation = Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * 180 / Math.PI;
            var name = Read(ValvePipingParameters.ItemName);
            var familyName = instance.Symbol?.FamilyName ?? instance.Symbol?.Name ?? instance.Name;
            var previewName = string.IsNullOrWhiteSpace(name) ? $"{familyName} : {instance.Symbol?.Name}" : name;
            var part = Read(ValvePipingParameters.Part);
            return new JunctionPreviewItem(instance.Id.Value,
                instance.LookupParameter(ValvePipingParameters.ItemNumber)?.AsInteger() ?? 0,
                previewName, Read(ValvePipingParameters.ItemSpec),
                JunctionSymbolKindMapper.Map(part, string.IsNullOrWhiteSpace(name) ? familyName : name),
                order.GetValueOrDefault(instance.Id.Value), ToVector(transform.BasisX), ToVector(transform.BasisY),
                ToVector(transform.BasisZ), ToVector(upDirection), rotation, !orphans.Contains(instance.Id.Value),
                ToVector(rightDirection), null,
                // 미리보기 캔버스 배치용 평면 위치(mm). 격점 평면뷰 오른쪽·위쪽 축(기호선도 미리보기 UseCase가 이 축으로 돌림)
                new ShapePoint(RevitUnitConverter.FtToMm(transform.Origin.DotProduct(rightDirection)),
                    RevitUnitConverter.FtToMm(transform.Origin.DotProduct(upDirection))));
        }).ToList();
    }

    public JunctionValveRoomLookup GetValveRoom(JunctionDetailGroup group)
    {
        var document = Document();
        var instances = Instances(document, group);
        if (instances.Count == 0) return new(null, null);
        var plan = FindPlan(document, group.JunctionId);
        return FindValveRoom(document, instances, FindRoot(group, instances),
            plan?.RightDirection ?? XYZ.BasisX, plan?.UpDirection ?? XYZ.BasisY);
    }

    private static ViewPlan? FindPlan(Document document, string junctionId)
    {
        var viewName = ViewName(junctionId);
        return new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(x => !x.IsTemplate && x.ViewType == ViewType.FloorPlan && x.Name == viewName);
    }

    // 부재 범위 XY ±10m, Z 뷰 범위(아래 500·위 1000)와 겹치는 직선 벽 → 4면 판정 (docs/293)
    internal static JunctionValveRoomLookup FindValveRoom(Document document, IReadOnlyList<FamilyInstance> instances,
        FamilyInstance? root, XYZ right, XYZ up)
    {
        var bounds = Bounds(instances);
        var reach = RevitUnitConverter.MmToFt(10000);
        var outline = new Outline(
            new XYZ(bounds.Min.X - reach, bounds.Min.Y - reach, bounds.Min.Z - RevitUnitConverter.MmToFt(500)),
            new XYZ(bounds.Max.X + reach, bounds.Max.Y + reach, bounds.Max.Z + RevitUnitConverter.MmToFt(1000)));
        var walls = new FilteredElementCollector(document).OfClass(typeof(Wall))
            .WherePasses(new BoundingBoxIntersectsFilter(outline)).Cast<Wall>()
            .Where(x => x.WallType.Kind != WallKind.Curtain && x.Location is LocationCurve { Curve: Line })
            .Select(x => Footprint(x, right, up)).OfType<JunctionWallFootprint>().ToList();
        // 부재 원점마다 4면 벽 판정, 다수결. 벽 밖 부재 섞여도 실 찾음 (docs/303)
        var origins = instances.Select(x => x.GetTransform().Origin).ToList();
        var planCenter = new ShapePoint(
            (origins.Min(x => PlanMm(x, right)) + origins.Max(x => PlanMm(x, right))) / 2,
            (origins.Min(x => PlanMm(x, up)) + origins.Max(x => PlanMm(x, up))) / 2);
        var points = origins.Select(x => new ShapePoint(PlanMm(x, right), PlanMm(x, up))).ToList();
        var rootOrigin = root?.GetTransform().Origin;
        ShapePoint? rootPoint = rootOrigin is null ? null
            : new ShapePoint(PlanMm(rootOrigin, right), PlanMm(rootOrigin, up));
        var room = JunctionValveRoom.ResolveMajority(walls, points, rootPoint);
        return new(room, room is null ? JunctionValveRoom.ExplainMajorityFailure(walls, planCenter) : null);
    }

    // 벽 솔리드 꼭짓점을 벽 방향·법선으로 투영한 범위 → 위치선(중심선·마감면) 설정과 무관한 평면 사각형
    private static JunctionWallFootprint? Footprint(Wall wall, XYZ right, XYZ up)
    {
        var line = (Line)((LocationCurve)wall.Location).Curve;
        var along = new XYZ(line.Direction.X, line.Direction.Y, 0);
        if (along.IsZeroLength()) return null;
        along = along.Normalize();
        var normal = XYZ.BasisZ.CrossProduct(along);
        var points = new List<XYZ>();
        if (wall.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Coarse }) is { } geometry)
            foreach (var item in geometry)
                if (item is Solid { Volume: > 0 } solid)
                    foreach (Edge edge in solid.Edges) points.AddRange(edge.Tessellate());
        if (points.Count == 0) return null;
        var a = points.Select(x => x.DotProduct(along)).ToList();
        var n = points.Select(x => x.DotProduct(normal)).ToList();
        var corners = new[] { (a.Min(), n.Min()), (a.Max(), n.Min()), (a.Max(), n.Max()), (a.Min(), n.Max()) }
            .Select(c => along * c.Item1 + normal * c.Item2)
            .Select(x => new ShapePoint(PlanMm(x, right), PlanMm(x, up))).ToList();
        return new JunctionWallFootprint(wall.Id.Value, corners);
    }

    private static double PlanMm(XYZ point, XYZ axis) => RevitUnitConverter.FtToMm(point.DotProduct(axis));

    private static XYZ PlanPoint(ViewPlan view, double rightMm, double upMm)
        => view.RightDirection * RevitUnitConverter.MmToFt(rightMm) + view.UpDirection * RevitUnitConverter.MmToFt(upMm);

    // 이전 치수 문자 = 이 뷰 문자 중 "숫자 x 숫자" 형식. 사용자가 같은 형식 문자 넣으면 같이 지워짐
    private static readonly System.Text.RegularExpressions.Regex RoomLabelPattern =
        new(@"^\d+(\.\d+)? x \d+(\.\d+)?$");

    // 밸브실 안쪽 치수 문자. 바깥 우측 상단 모서리 위, 오른쪽 정렬 (docs/293)
    private static void PlaceRoomLabel(Document document, ViewPlan view, JunctionValveRoom? room, List<string> warnings)
    {
        var previous = new FilteredElementCollector(document, view.Id).OfClass(typeof(TextNote)).Cast<TextNote>()
            .Where(x => RoomLabelPattern.IsMatch(x.Text.Trim())).Select(x => x.Id).ToList();
        if (previous.Count > 0) document.Delete(previous);
        if (room is null)
        {
            warnings.Add($"{view.Name}: 부재를 둘러싼 4면 벽을 찾지 못해 내부 치수 문자를 넣지 않았습니다.");
            return;
        }
        var typeId = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        if (typeId == ElementId.InvalidElementId)
        {
            warnings.Add($"{view.Name}: 기본 문자 유형이 없어 내부 치수 문자를 넣지 않았습니다.");
            return;
        }
        // 문자 기준점 = 위쪽 끝 → 글자 높이 + 1.5mm(도면)만큼 올려 벽 위에 얹음
        var size = document.GetElement(typeId)?.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble()
            ?? RevitUnitConverter.MmToFt(2.5);
        var lift = (size + RevitUnitConverter.MmToFt(1.5)) * view.Scale;
        var corner = PlanPoint(view, room.OuterMaxX, room.OuterMaxY) + view.UpDirection * lift;
        var anchor = new XYZ(corner.X, corner.Y, view.GenLevel?.Elevation ?? 0);
        TextNote.Create(document, view.Id, anchor, room.Label,
            new TextNoteOptions(typeId) { HorizontalAlignment = HorizontalTextAlignment.Right });
    }

    public string GetRoomType()
        => Document().ProjectInformation.LookupParameter(ValveRoomProjectInfoParameters.RoomType)?.AsString() ?? string.Empty;

    public IReadOnlyList<JunctionDetailTagType> GetTagTypes()
        => TagSymbols(Document())
            .Select(x => new JunctionDetailTagType(x.Id.Value, $"[{x.Category?.Name}] {x.FamilyName} : {x.Name}"))
            .OrderBy(x => x.Name, StringComparer.Ordinal).ToList();

    // 번호 태그 후보 = 다중 카테고리 + 배관 이음쇠 + 배관 부속류 태그. 격점 부재가 이음쇠·부속류뿐이라 이 셋이면 충분
    private static readonly BuiltInCategory[] TagCategories =
        [BuiltInCategory.OST_MultiCategoryTags, BuiltInCategory.OST_PipeFittingTags, BuiltInCategory.OST_PipeAccessoryTags];

    private static List<FamilySymbol> TagSymbols(Document document)
        => new FilteredElementCollector(document).WherePasses(new ElementMulticategoryFilter(TagCategories))
            .WhereElementIsElementType().OfType<FamilySymbol>().ToList();

    // 태그 유형이 이 부재를 태그할 수 있는지. 다중 카테고리 = 전부, 범주 태그 = 같은 범주 부재만
    private static bool CanTag(FamilySymbol type, FamilyInstance instance)
    {
        var tagCategory = type.Category?.Id.Value;
        if (tagCategory == (long)BuiltInCategory.OST_MultiCategoryTags) return true;
        return instance.Category?.Id.Value switch
        {
            (long)BuiltInCategory.OST_PipeFitting => tagCategory == (long)BuiltInCategory.OST_PipeFittingTags,
            (long)BuiltInCategory.OST_PipeAccessory => tagCategory == (long)BuiltInCategory.OST_PipeAccessoryTags,
            _ => false,
        };
    }

    public JunctionDetailViewChange CreateOrUpdateView(JunctionDetailGroup group, IReadOnlyList<VerticalSlot> slots, JunctionDetailViewOptions options)
    {
        var document = Document();
        using var sub = new SubTransaction(document);
        sub.Start();
        try
        {
            var instances = Instances(document, group);
            var root = FindRoot(group, instances);
            var level = SelectLevel(document, root, instances)
                ?? throw new InvalidOperationException($"격점 {group.JunctionId}: 사용할 레벨을 찾을 수 없습니다.");
            var viewName = ViewName(group.JunctionId);
            var existing = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, viewName, StringComparison.Ordinal));
            if (existing is not null && (existing is not ViewPlan || existing.ViewType != ViewType.FloorPlan))
                throw new InvalidOperationException($"'{viewName}' 이름을 다른 종류의 뷰가 사용합니다.");
            if (existing is ViewPlan existingPlan && existingPlan.GenLevel?.Id != level.Id)
                throw new InvalidOperationException($"'{viewName}'의 레벨이 대상 부재 레벨과 다릅니다.");

            var created = existing is null;
            var view = existing as ViewPlan ?? CreateView(document, viewName, level);
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                if (created) view.ViewTemplateId = ElementId.InvalidElementId;
                else throw new InvalidOperationException($"'{viewName}'의 뷰 템플릿을 해제한 뒤 다시 실행하세요.");
            }

            view.DetailLevel = ViewDetailLevel.Coarse;
            // 벽 뒤 심볼도 보이게 와이어프레임 (docs/292)
            view.DisplayStyle = DisplayStyle.Wireframe;
            view.Scale = options.ViewScale;
            ConfigureRange(view, level, instances);
            var room = FindValveRoom(document, instances, root, view.RightDirection, view.UpDirection).Room;
            ConfigureCrop(view, instances, slots, options, room);
            ConfigureCategories(document, view);
            RemovePreviousFilter(document, view);

            var warnings = ResetInstances(instances);
            RotateSymbols(instances, slots, view, options, warnings);
            PlaceRoomLabel(document, view, room, warnings);
            WriteOffsets(instances, slots, view, options, warnings);
            if (options.PlaceTags) PlaceNumberTags(document, view, instances, group.Nodes, slots, options, warnings);
            sub.Commit();
            return new JunctionDetailViewChange(created, warnings);
        }
        catch { sub.RollBack(); throw; }
    }

    public IReadOnlyList<string> ResetOffsets(JunctionDetailGroup group)
    {
        var document = Document();
        using var sub = new SubTransaction(document);
        sub.Start();
        try
        {
            var warnings = ResetInstances(Instances(document, group));
            sub.Commit();
            return warnings;
        }
        catch { sub.RollBack(); throw; }
    }

    private Document Document()
        => _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");

    private static string JunctionId(FamilyInstance instance)
        => (instance.get_Parameter(ValvePipingParameters.Guids[ValvePipingParameters.JunctionId])
            ?? instance.LookupParameter(ValvePipingParameters.JunctionId))?.AsString() ?? string.Empty;

    private static List<FamilyInstance> Instances(Document document, JunctionDetailGroup group)
        => group.Nodes.Select(x => document.GetElement(new ElementId(x.Id)) as FamilyInstance)
            .OfType<FamilyInstance>().ToList();

    private static FamilyInstance? FindRoot(JunctionDetailGroup group, IReadOnlyList<FamilyInstance> instances)
    {
        var rootId = group.Nodes.FirstOrDefault(node =>
            node.Connectors.Any(x => Math.Abs(x.Dz) >= JunctionVerticalChain.VerticalCosine) &&
            node.Connectors.Any(x => Math.Abs(x.Dz) < JunctionVerticalChain.VerticalCosine))?.Id;
        return instances.FirstOrDefault(x => x.Id.Value == rootId);
    }

    private static string ViewName(string junctionId)
        => string.IsNullOrWhiteSpace(junctionId) ? "격점상세도_미지정"
            : PipeMaterialViewName.For(CreateJunctionDetailViewUseCase.NamePrefix, junctionId);

    internal static Level? SelectLevel(Document document, FamilyInstance? root,
        IReadOnlyList<FamilyInstance> instances)
    {
        if (root is not null && document.GetElement(root.LevelId) is Level rootLevel) return rootLevel;
        var mostCommon = instances.Select(x => document.GetElement(x.LevelId) as Level).OfType<Level>()
            .GroupBy(x => x.Id.Value).OrderByDescending(x => x.Count()).ThenBy(x => x.Key)
            .Select(x => x.First()).FirstOrDefault();
        if (mostCommon is not null) return mostCommon;
        var lowest = Bounds(instances).Min.Z;
        return new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
            .Where(x => x.Elevation <= lowest).OrderByDescending(x => x.Elevation).FirstOrDefault();
    }

    // 세트별 축·부호 간격으로 순서만큼 이동. 음수 간격 = 반대 방향
    private static XYZ SlotShift(ViewPlan view, VerticalSlot slot, JunctionDetailViewOptions options)
    {
        var set = options.For(slot.SetId);
        var axis = set.Axis == JunctionExpandAxis.X ? view.RightDirection : view.UpDirection;
        return axis * RevitUnitConverter.MmToFt(slot.Order * set.StepMm);
    }

    private static void RemovePreviousFilter(Document document, ViewPlan view)
    {
        var name = $"격점상세도_필터_{view.Id.Value}";
        var filter = new FilteredElementCollector(document).OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>().FirstOrDefault(x => x.Name == name);
        if (filter is not null && view.GetFilters().Contains(filter.Id)) view.RemoveFilter(filter.Id);
    }

    internal static ViewPlan CreateView(Document document, string name, Level level)
    {
        var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.FloorPlan)
            ?? throw new InvalidOperationException("프로젝트에 평면뷰 유형이 없습니다.");
        var view = ViewPlan.Create(document, type.Id, level.Id);
        view.Name = name;
        return view;
    }

    internal static void ConfigureRange(ViewPlan view, Level level, IReadOnlyList<FamilyInstance> instances)
    {
        var bounds = Bounds(instances);
        var range = view.GetViewRange();
        foreach (var plane in new[] { PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane,
                     PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane })
            range.SetLevelId(plane, level.Id);
        var upper = bounds.Max.Z + RevitUnitConverter.MmToFt(1000) - level.Elevation;
        var lower = bounds.Min.Z - RevitUnitConverter.MmToFt(500) - level.Elevation;
        range.SetOffset(PlanViewPlane.TopClipPlane, upper);
        range.SetOffset(PlanViewPlane.CutPlane, upper);
        range.SetOffset(PlanViewPlane.BottomClipPlane, lower);
        range.SetOffset(PlanViewPlane.ViewDepthPlane, lower);
        view.SetViewRange(range);
    }

    private static void ConfigureCrop(ViewPlan view, IReadOnlyList<FamilyInstance> instances,
        IReadOnlyList<VerticalSlot> slots, JunctionDetailViewOptions options, JunctionValveRoom? room)
    {
        var bounds = Bounds(instances);
        // 세트마다 방향이 달라 모든 이동량을 범위에 넣음
        var shifts = slots.Select(slot => SlotShift(view, slot, options)).Prepend(XYZ.Zero).ToList();
        var crop = view.CropBox;
        var inverse = crop.Transform.Inverse;
        var corners = new[]
        {
            new XYZ(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new XYZ(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
            new XYZ(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
            new XYZ(bounds.Max.X, bounds.Max.Y, bounds.Min.Z)
        };
        var local = shifts.SelectMany(shift => corners.Select(x => x + shift)).Select(inverse.OfPoint).ToList();
        // 밸브실 벽 전체 포함 (docs/293)
        if (room is not null)
            local.AddRange(new[]
            {
                PlanPoint(view, room.OuterMinX, room.OuterMinY), PlanPoint(view, room.OuterMaxX, room.OuterMaxY),
                PlanPoint(view, room.OuterMinX, room.OuterMaxY), PlanPoint(view, room.OuterMaxX, room.OuterMinY)
            }.Select(inverse.OfPoint));
        var margin = RevitUnitConverter.MmToFt(1000);
        crop.Min = new XYZ(local.Min(x => x.X) - margin, local.Min(x => x.Y) - margin, crop.Min.Z);
        crop.Max = new XYZ(local.Max(x => x.X) + margin, local.Max(x => x.Y) + margin, crop.Max.Z);
        view.CropBoxActive = true;
        view.CropBox = crop;
    }

    internal static (XYZ Min, XYZ Max) Bounds(IReadOnlyList<FamilyInstance> instances)
    {
        var points = new List<XYZ>();
        foreach (var instance in instances)
        {
            var box = instance.get_BoundingBox(null);
            if (box is null) points.Add(instance.GetTransform().Origin);
            else { points.Add(box.Min); points.Add(box.Max); }
        }
        if (points.Count == 0) throw new InvalidOperationException("격점에 배관 부재가 없습니다.");
        return (new XYZ(points.Min(x => x.X), points.Min(x => x.Y), points.Min(x => x.Z)),
            new XYZ(points.Max(x => x.X), points.Max(x => x.Y), points.Max(x => x.Z)));
    }

    private static void ConfigureCategories(Document document, ViewPlan view)
    {
        foreach (Category category in document.Settings.Categories)
        {
            if (category.CategoryType != CategoryType.Model || !view.CanCategoryBeHidden(category.Id)) continue;
            // 심볼 .rfa = 일반 모델. 공유 중첩이면 자기 카테고리로 가시성 판정 → 같이 켬 (docs/284)
            // 벽 = 밸브실 위치 파악용 (docs/292)
            var keep = category.Id.Value == (long)BuiltInCategory.OST_PipeFitting ||
                category.Id.Value == (long)BuiltInCategory.OST_PipeAccessory ||
                category.Id.Value == (long)BuiltInCategory.OST_GenericModel ||
                category.Id.Value == (long)BuiltInCategory.OST_Walls;
            view.SetCategoryHidden(category.Id, !keep);
            if (keep) ShowSubCategories(view, category);
        }
        // 단면 기호 = 주석 카테고리라 위 루프 대상 아님 → 따로 끔
        var sections = Category.GetCategory(document, BuiltInCategory.OST_Sections);
        if (sections is not null && view.CanCategoryBeHidden(sections.Id)) view.SetCategoryHidden(sections.Id, true);
    }

    // 하위 카테고리 개별로 꺼져 있으면 심볼 선 빠짐 → 재실행마다 켬
    internal static void ShowSubCategories(View view, Category category)
    {
        foreach (Category subCategory in category.SubCategories)
            if (view.CanCategoryBeHidden(subCategory.Id)) view.SetCategoryHidden(subCategory.Id, false);
    }

    private static void ConfigureJunctionFilter(Document document, ViewPlan view, string junctionId)
    {
        var parameter = SharedParameterElement.Lookup(document, ValvePipingParameters.Guids[ValvePipingParameters.JunctionId])
            ?? throw new InvalidOperationException("DH_격점번호 공유 매개변수가 없어 뷰 필터를 만들 수 없습니다.");
        var rule = ParameterFilterRuleFactory.CreateEqualsRule(parameter.Id, junctionId);
        var condition = new ElementParameterFilter(rule, true);
        var name = $"격점상세도_필터_{view.Id.Value}";
        var filter = new FilteredElementCollector(document).OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>().FirstOrDefault(x => x.Name == name);
        if (filter is null)
        {
            var categories = new List<ElementId>
            {
                new(BuiltInCategory.OST_PipeFitting), new(BuiltInCategory.OST_PipeAccessory)
            };
            filter = ParameterFilterElement.Create(document, name, categories, condition);
        }
        else filter.SetElementFilter(condition);
        if (!view.GetFilters().Contains(filter.Id)) view.AddFilter(filter.Id);
        view.SetFilterVisibility(filter.Id, false);
    }

    private static List<string> ResetInstances(IReadOnlyList<FamilyInstance> instances)
    {
        var warnings = new List<string>();
        foreach (var instance in instances)
        {
            // 회전 복원은 오프셋 매개변수 유무와 별개(docs/306)
            RestoreRotation(instance, warnings);
            if (!TryParameters(instance, warnings, out var parameters)) continue;
            foreach (var parameter in parameters) parameter?.Set(0d);
        }
        if (instances.Count > 0) instances[0].Document.Regenerate();
        return warnings;
    }

    // 애드인이 돌린 부재 → 기록한 원래 BasisY 각으로 되돌림, 기록 지움(docs/306)
    private static void RestoreRotation(FamilyInstance instance, List<string> warnings)
    {
        if (JunctionRotationStorageSchema.Read(instance) is not { } original) return;
        var delta = NormalizeAngle(original - PlanAngle(instance.GetTransform().BasisY));
        if (Math.Abs(delta) < 1e-6 || TryRotate(instance, delta, warnings, "원래 방향 복원"))
            JunctionRotationStorageSchema.Clear(instance);
    }

    // 세트 부재 심볼 위쪽(BasisY) → 펼침 방향. 축 = 수직 커넥터 원점 Z선이라 연결 유지(docs/306)
    private static void RotateSymbols(IReadOnlyList<FamilyInstance> instances, IReadOnlyList<VerticalSlot> slots,
        ViewPlan view, JunctionDetailViewOptions options, List<string> warnings)
    {
        var byId = instances.ToDictionary(x => x.Id.Value);
        var rotated = false;
        foreach (var slot in slots)
        {
            if (!byId.TryGetValue(slot.ElementId, out var instance)) continue;
            var transform = instance.GetTransform();
            var angle = JunctionSymbolRotation.AngleRadians(ToVector(transform.BasisY), ToVector(view.RightDirection),
                ToVector(view.UpDirection), options.For(slot.SetId));
            if (angle is null)
            {
                warnings.Add($"부재 {instance.Id.Value}: 로컬 Y가 수직이라 심볼 방향 회전을 건너뜁니다");
                continue;
            }
            if (angle == 0) continue;
            // 뷰 오른쪽 × 위쪽 = 뷰 법선. 평면뷰는 +Z → 뷰 반시계 = 월드 Z 반시계
            var normal = view.RightDirection.CrossProduct(view.UpDirection).Z >= 0 ? 1 : -1;
            var original = PlanAngle(transform.BasisY);
            if (!TryRotate(instance, angle.Value * normal, warnings, "심볼 방향 회전")) continue;
            if (JunctionRotationStorageSchema.Read(instance) is null) JunctionRotationStorageSchema.Write(instance, original);
            rotated = true;
        }
        // WriteOffsets가 새 GetTransform 읽게
        if (rotated) instances[0].Document.Regenerate();
    }

    private static bool TryRotate(FamilyInstance instance, double radians, List<string> warnings, string action)
    {
        try
        {
            var vertical = PipeMaterialConnectorReader.EndConnectors(instance)
                .FirstOrDefault(x => Math.Abs(x.CoordinateSystem.BasisZ.Z) >= JunctionVerticalChain.VerticalCosine);
            var origin = vertical?.Origin ?? instance.GetTransform().Origin;
            ElementTransformUtils.RotateElement(instance.Document, instance.Id,
                Line.CreateBound(origin, origin + XYZ.BasisZ), radians);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"부재 {instance.Id.Value}: {action} 실패 — {ex.Message}");
            return false;
        }
    }

    private static double PlanAngle(XYZ vector) => Math.Atan2(vector.Y, vector.X);

    private static double NormalizeAngle(double radians)
    {
        while (radians > Math.PI) radians -= 2 * Math.PI;
        while (radians < -Math.PI) radians += 2 * Math.PI;
        return radians;
    }

    private static void WriteOffsets(IReadOnlyList<FamilyInstance> instances, IReadOnlyList<VerticalSlot> slots,
        ViewPlan view, JunctionDetailViewOptions options, List<string> warnings)
    {
        var byId = instances.ToDictionary(x => x.Id.Value);
        foreach (var slot in slots)
        {
            if (!byId.TryGetValue(slot.ElementId, out var instance) ||
                !TryParameters(instance, warnings, out var parameters)) continue;
            var world = SlotShift(view, slot, options);
            var transform = instance.GetTransform();
            var local = SymbolOffsetMath.ToLocal(ToVector(world), ToVector(transform.BasisX),
                ToVector(transform.BasisY), ToVector(transform.BasisZ));
            var values = new[] { local.X, local.Y, local.Z };
            var present = parameters.Select(x => x is not null).ToArray();
            var writable = parameters.Select(x => x is { IsReadOnly: false, StorageType: StorageType.Double }).ToArray();
            var components = JunctionExpandDirection.WritableComponents(present, writable, local, out var missing);
            foreach (var index in components) parameters[index]!.Set(values[index]);
            foreach (var index in missing)
                warnings.Add($"부재 {instance.Id.Value}: 로컬 {new[] { "X", "Y", "Z" }[index]} 방향 오프셋이 필요하지만 매개변수가 없습니다");
        }
    }

    private static SymbolVector ToVector(XYZ vector) => new(vector.X, vector.Y, vector.Z);

    private static bool TryParameters(FamilyInstance instance, List<string> warnings, out Parameter?[] parameters)
    {
        var names = new[] { JunctionDetailParameters.SymbolOffsetX, JunctionDetailParameters.SymbolOffsetY,
            JunctionDetailParameters.SymbolOffsetZ };
        var found = names.Select(name => JunctionSymbolParameterLookup.Find(instance, name)).ToArray();
        parameters = [];
        if (found.All(x => x is null))
        {
            warnings.Add($"부재 {instance.Id.Value}: 패밀리 갱신 필요 (심볼 오프셋 매개변수 없음)");
            return false;
        }
        if (found.Any(x => x is not null && (x.IsReadOnly || x.StorageType != StorageType.Double)))
        {
            warnings.Add($"부재 {instance.Id.Value}: 심볼 오프셋 매개변수가 없거나 읽기 전용입니다.");
            return false;
        }
        parameters = found;
        return true;
    }

    private static void PlaceNumberTags(Document document, ViewPlan view, IReadOnlyList<FamilyInstance> instances,
        IReadOnlyList<JunctionNode> nodes, IReadOnlyList<VerticalSlot> slots, JunctionDetailViewOptions options,
        List<string> warnings)
    {
        var types = TagSymbols(document);
        var selectedType = options.TagTypeId > 0 ? types.FirstOrDefault(x => x.Id.Value == options.TagTypeId) : null;
        if (types.Count == 0) warnings.Add("격점 번호 태그 유형이 없어 새 태그를 만들지 않았습니다.");
        // 부재별 유형: 고른 유형이 맞으면 그것, 아니면 맞는 유형이 하나뿐일 때 그것(이음쇠 + 부속류 태그 섞어 쓰기)
        FamilySymbol? TypeFor(FamilyInstance instance)
        {
            if (selectedType is not null && CanTag(selectedType, instance)) return selectedType;
            var compatible = types.Where(x => CanTag(x, instance)).ToList();
            return compatible.Count == 1 ? compatible[0] : null;
        }
        var noType = new List<string>();
        var groupIds = instances.Select(x => x.Id).ToHashSet();
        var existingTags = new FilteredElementCollector(document, view.Id).OfClass(typeof(IndependentTag))
            .Cast<IndependentTag>().Where(tag => tag.GetTaggedLocalElementIds().All(groupIds.Contains))
            .SelectMany(tag => tag.GetTaggedLocalElementIds()
                .Select(id => (id, tag))).GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.First().tag);
        var slotById = slots.ToDictionary(x => x.ElementId);
        var verticalIds = nodes.Where(x => x.Connectors.Count > 0 &&
                x.Connectors.All(connector => Math.Abs(connector.Dz) >= JunctionVerticalChain.VerticalCosine))
            .Select(x => x.Id).ToHashSet();
        // 미리보기가 정한 머리·지시선 끝(docs/296). 도면 mm × 축척 = 실제 mm
        var planned = (options.TagPlacements ?? []).GroupBy(x => x.ElementId).ToDictionary(x => x.Key, x => x.First());
        var paperToFt = RevitUnitConverter.MmToFt(view.Scale);
        var gap = RevitUnitConverter.MmToFt(JunctionPreviewLayout.TagGapPaperMm * view.Scale);
        var kept = 0;
        foreach (var instance in instances)
        {
            if ((instance.LookupParameter(ValvePipingParameters.ItemNumber)?.AsInteger() ?? 0) <= 0) continue;
            var origin = instance.GetTransform().Origin;
            var center = origin;
            var isVertical = verticalIds.Contains(instance.Id.Value);
            var axis = options.ExpandAxis;
            if (isVertical && slotById.TryGetValue(instance.Id.Value, out var slot))
            {
                center += SlotShift(view, slot, options);
                axis = options.For(slot.SetId).Axis;
            }
            // 종전 규칙 머리. 이전 버전 태그 소유 판정 + 배치 정보 없을 때 대체
            var side = axis == JunctionExpandAxis.Y ? view.RightDirection : view.UpDirection;
            var legacyHead = isVertical ? center - side * gap : center - view.UpDirection * gap;
            XYZ head, leaderEnd;
            if (planned.TryGetValue(instance.Id.Value, out var plan))
            {
                // 미리보기 좌표 = 원점·뷰 축 내적(mm) → 뷰 축 합성. 높이는 부재 원점 Z
                XYZ ToModel(double x, double y)
                {
                    var v = view.RightDirection * (x * paperToFt) + view.UpDirection * (y * paperToFt);
                    return new XYZ(v.X, v.Y, origin.Z);
                }
                head = ToModel(plan.HeadX, plan.HeadY);
                leaderEnd = ToModel(plan.LeaderX, plan.LeaderY);
            }
            else
            {
                head = legacyHead;
                leaderEnd = center;
            }
            try
            {
                if (existingTags.TryGetValue(instance.Id, out var tag))
                {
                    if (!IsAddinPlaced(tag, instance, legacyHead)) { kept++; continue; }
                    ApplyLeader(tag, instance, head, leaderEnd);
                }
                else if (types.Count > 0 && TypeFor(instance) is null)
                {
                    noType.Add(instance.Category?.Name ?? "기타");
                }
                else if (types.Count > 0)
                {
                    var type = TypeFor(instance)!;
                    if (!type.IsActive) type.Activate();
                    var created = IndependentTag.Create(document, type.Id, view.Id, new Reference(instance), true,
                        TagOrientation.Horizontal, head);
                    ApplyLeader(created, instance, head, leaderEnd);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"부재 {instance.Id.Value}: 번호 태그를 만들 수 없습니다 ({ex.Message})");
            }
        }
        if (noType.Count > 0)
            warnings.Add($"번호 태그 유형을 정할 수 없어 {noType.Count}개 부재({string.Join(", ", noType.Distinct())})는 태그를 만들지 않았습니다. " +
                "그 범주 태그나 다중 카테고리 태그를 로드하거나, 맞는 유형을 고르세요.");
        if (kept > 0) warnings.Add($"사용자가 옮긴 번호 태그 {kept}개는 그대로 두었습니다");
    }

    // 위치 같음 허용 오차: 실제 0.5mm
    private static readonly double SamePositionFt = RevitUnitConverter.MmToFt(0.5);

    /// <summary>애드인이 둔 자리 그대로인지(docs/296 § C). 기록 있으면 기록과 비교(머리·지시선 끝),
    /// 없으면(이전 버전 태그) 종전 규칙 머리와 비교. 평면 위치만 비교(Z 무시).</summary>
    private static bool IsAddinPlaced(IndependentTag tag, FamilyInstance instance, XYZ legacyHead)
    {
        if (JunctionTagStorageSchema.Read(tag) is { } record)
        {
            if (!SamePlan(tag.TagHeadPosition, record.Head)) return false;
            if (record.LeaderEnd is null || !tag.HasLeader || tag.LeaderEndCondition != LeaderEndCondition.Free) return true;
            return SamePlan(tag.GetLeaderEnd(new Reference(instance)), record.LeaderEnd);
        }
        return SamePlan(tag.TagHeadPosition, legacyHead);
    }

    private static bool SamePlan(XYZ a, XYZ b)
        => Math.Abs(a.X - b.X) < SamePositionFt && Math.Abs(a.Y - b.Y) < SamePositionFt;

    /// <summary>지시선 켬 · 자유 끝 · 끝 = 부재 중심선 위 · 꺾임점 = 끝과 머리 중점 → 직선 지시선(docs/296).
    /// 머리 먼저 → 끝 → 꺾임점 순서(꺾임점 뒤에 머리 옮기면 Revit이 꺾임점 다시 잡을 수 있음).
    /// 끝나면 소유 기록 = Revit이 실제로 둔 값. 보정되면 다음 실행에서 사용자 이동으로 잘못 판정하므로 넘긴 값 말고 다시 읽음.</summary>
    private static void ApplyLeader(IndependentTag tag, FamilyInstance instance, XYZ head, XYZ leaderEnd)
    {
        var reference = new Reference(instance);
        tag.HasLeader = true;
        tag.LeaderEndCondition = LeaderEndCondition.Free;
        tag.TagHeadPosition = head;
        tag.SetLeaderEnd(reference, leaderEnd);
        tag.SetLeaderElbow(reference, (head + leaderEnd) / 2);
        JunctionTagStorageSchema.Write(tag, tag.TagHeadPosition, tag.GetLeaderEnd(reference));
    }
}
