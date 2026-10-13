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
        var viewName = ViewName(group.JunctionId);
        var plan = new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(x => !x.IsTemplate && x.ViewType == ViewType.FloorPlan && x.Name == viewName);
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
                // 미리보기 캔버스 배치용 평면 위치(mm). 기호선 읽기와 같은 뷰 오른쪽·위쪽 축
                new ShapePoint(RevitUnitConverter.FtToMm(transform.Origin.DotProduct(rightDirection)),
                    RevitUnitConverter.FtToMm(transform.Origin.DotProduct(upDirection))));
        }).ToList();
    }

    public string GetRoomType()
        => Document().ProjectInformation.LookupParameter(ValveRoomProjectInfoParameters.RoomType)?.AsString() ?? string.Empty;

    public IReadOnlyList<JunctionDetailTagType> GetTagTypes()
        => new FilteredElementCollector(Document()).OfCategory(BuiltInCategory.OST_MultiCategoryTags)
            .WhereElementIsElementType().OfType<FamilySymbol>()
            .Select(x => new JunctionDetailTagType(x.Id.Value, $"{x.FamilyName} : {x.Name}"))
            .OrderBy(x => x.Name, StringComparer.Ordinal).ToList();

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
            view.Scale = options.ViewScale;
            ConfigureRange(view, level, instances);
            ConfigureCrop(view, instances, slots, options);
            ConfigureCategories(document, view);
            RemovePreviousFilter(document, view);

            var warnings = ResetInstances(instances);
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
        return axis * RevitUnitConverter.MmToFt(slot.Order * set.StepPaperMm * view.Scale);
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
        IReadOnlyList<VerticalSlot> slots, JunctionDetailViewOptions options)
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
            var keep = category.Id.Value == (long)BuiltInCategory.OST_PipeFitting ||
                category.Id.Value == (long)BuiltInCategory.OST_PipeAccessory ||
                category.Id.Value == (long)BuiltInCategory.OST_GenericModel;
            view.SetCategoryHidden(category.Id, !keep);
            if (keep) ShowSubCategories(view, category);
        }
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
            if (!TryParameters(instance, warnings, out var parameters)) continue;
            foreach (var parameter in parameters) parameter?.Set(0d);
        }
        return warnings;
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
        var types = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_MultiCategoryTags)
            .WhereElementIsElementType().OfType<FamilySymbol>().ToList();
        var selectedType = options.TagTypeId > 0 ? types.FirstOrDefault(x => x.Id.Value == options.TagTypeId) : null;
        var type = selectedType ?? (types.Count == 1 ? types[0] : null);
        if (type is null)
            warnings.Add(types.Count == 0 ? "격점 번호 태그 유형이 없어 새 태그를 만들지 않았습니다."
                : "격점 번호 태그 유형이 여러 개라 새 태그 대상을 정할 수 없습니다.");
        else if (!type.IsActive) type.Activate();
        var groupIds = instances.Select(x => x.Id).ToHashSet();
        var existingTags = new FilteredElementCollector(document, view.Id).OfClass(typeof(IndependentTag))
            .Cast<IndependentTag>().Where(tag => tag.GetTaggedLocalElementIds().All(groupIds.Contains))
            .SelectMany(tag => tag.GetTaggedLocalElementIds()
                .Select(id => (id, tag))).GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.First().tag);
        var slotById = slots.ToDictionary(x => x.ElementId);
        var verticalIds = nodes.Where(x => x.Connectors.Count > 0 &&
                x.Connectors.All(connector => Math.Abs(connector.Dz) >= JunctionVerticalChain.VerticalCosine))
            .Select(x => x.Id).ToHashSet();
        var gap = RevitUnitConverter.MmToFt(JunctionPreviewLayout.TagGapPaperMm * view.Scale);
        var moved = 0;
        foreach (var instance in instances)
        {
            if ((instance.LookupParameter(ValvePipingParameters.ItemNumber)?.AsInteger() ?? 0) <= 0) continue;
            var center = instance.GetTransform().Origin;
            var isVertical = verticalIds.Contains(instance.Id.Value);
            var axis = options.ExpandAxis;
            if (isVertical && slotById.TryGetValue(instance.Id.Value, out var slot))
            {
                center += SlotShift(view, slot, options);
                axis = options.For(slot.SetId).Axis;
            }
            // 미리보기(JunctionPreviewLayout.Build)와 같은 규칙
            var side = axis == JunctionExpandAxis.Y ? view.RightDirection : view.UpDirection;
            var position = isVertical ? center - side * gap : center - view.UpDirection * gap;
            try
            {
                if (existingTags.TryGetValue(instance.Id, out var tag))
                {
                    tag.TagHeadPosition = position;
                    moved++;
                }
                else if (type is not null) IndependentTag.Create(document, type.Id, view.Id, new Reference(instance), false,
                    TagOrientation.Horizontal, position);
            }
            catch (Exception ex)
            {
                warnings.Add($"부재 {instance.Id.Value}: 번호 태그를 만들 수 없습니다 ({ex.Message})");
            }
        }
        if (moved > 0) warnings.Add($"번호 태그 {moved}개를 다시 배치했습니다");
    }
}
