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
        var instances = PipeMaterialPartCollector.AddinInstances(document).ToList();
        var connectors = instances.ToDictionary(x => x.Id.Value, PipeMaterialConnectorReader.EndConnectors);
        var connectorIndex = new Dictionary<(long Element, int Connector), int>();
        foreach (var instance in instances)
        for (var i = 0; i < connectors[instance.Id.Value].Count; i++)
            connectorIndex[(instance.Id.Value, connectors[instance.Id.Value][i].Id)] = i;

        return instances
            .Select(x => (Instance: x, JunctionId: JunctionId(x)))
            .Where(x => !string.IsNullOrWhiteSpace(x.JunctionId))
            .GroupBy(x => x.JunctionId, StringComparer.Ordinal)
            .Select(group => new JunctionDetailGroup(group.Key,
                group.Select(x => new JunctionNode(x.Instance.Id.Value,
                    connectors[x.Instance.Id.Value].Select(connector =>
                    {
                        var direction = connector.CoordinateSystem.BasisZ.Normalize();
                        var partner = PipeMaterialConnectorReader.Partner(connector, x.Instance.Id.Value, connectorIndex);
                        return new JunctionConnector(direction.X, direction.Y, direction.Z, partner.Id);
                    }).ToList())).ToList()))
            .ToList();
    }

    public IReadOnlyList<JunctionPreviewItem> GetPreviewItems(JunctionDetailGroup group)
    {
        var document = Document();
        var order = JunctionVerticalChain.Resolve(group.JunctionId, group.Nodes).Slots.ToDictionary(x => x.ElementId, x => x.Order);
        var disconnected = group.Nodes.Where(x => x.Connectors.Any(connector => connector.PartnerId is null))
            .Select(x => x.Id).ToHashSet();
        var viewName = PipeMaterialViewName.For(CreateJunctionDetailViewUseCase.NamePrefix, group.JunctionId);
        var upDirection = new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(x => !x.IsTemplate && x.ViewType == ViewType.FloorPlan && x.Name == viewName)?.UpDirection
            ?? XYZ.BasisY;
        return Instances(document, group).Select(instance =>
        {
            string Read(string name) => (ValvePipingParameters.Guids.TryGetValue(name, out var guid)
                ? instance.get_Parameter(guid) : null)?.AsString() ?? instance.LookupParameter(name)?.AsString() ?? string.Empty;
            var transform = instance.GetTransform();
            var rotation = Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * 180 / Math.PI;
            return new JunctionPreviewItem(instance.Id.Value,
                instance.LookupParameter(ValvePipingParameters.ItemNumber)?.AsInteger() ?? 0,
                Read(ValvePipingParameters.ItemName), Read(ValvePipingParameters.ItemSpec),
                JunctionSymbolKindMapper.Map(Read(ValvePipingParameters.Part), Read(ValvePipingParameters.ItemName)),
                order.GetValueOrDefault(instance.Id.Value), ToVector(transform.BasisX), ToVector(transform.BasisY),
                ToVector(transform.BasisZ), ToVector(upDirection), rotation, !disconnected.Contains(instance.Id.Value));
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
            var root = FindRoot(group, instances)
                ?? throw new InvalidOperationException($"격점 {group.JunctionId}: 수직 분기 뿌리 부재가 없습니다.");
            var level = document.GetElement(root.LevelId) as Level
                ?? throw new InvalidOperationException($"격점 {group.JunctionId}: 뿌리 부재에 레벨이 없습니다.");
            var viewName = PipeMaterialViewName.For(CreateJunctionDetailViewUseCase.NamePrefix, group.JunctionId);
            var existing = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, viewName, StringComparison.Ordinal));
            if (existing is not null && (existing is not ViewPlan || existing.ViewType != ViewType.FloorPlan))
                throw new InvalidOperationException($"'{viewName}' 이름을 다른 종류의 뷰가 사용합니다.");
            if (existing is ViewPlan existingPlan && existingPlan.GenLevel?.Id != level.Id)
                throw new InvalidOperationException($"'{viewName}'의 레벨이 격점 뿌리 부재 레벨과 다릅니다.");

            var created = existing is null;
            var view = existing as ViewPlan ?? CreateView(document, viewName, level);
            if (view.ViewTemplateId != ElementId.InvalidElementId)
                throw new InvalidOperationException($"'{viewName}'에 뷰 템플릿이 적용돼 있습니다.");

            view.DetailLevel = ViewDetailLevel.Coarse;
            view.Scale = options.ViewScale;
            ConfigureRange(view, level, instances);
            ConfigureCrop(view, instances, slots, options.StepPaperMm);
            ConfigureCategories(document, view);
            ConfigureJunctionFilter(document, view, group.JunctionId);

            var warnings = ResetInstances(instances);
            WriteOffsets(instances, slots, view, options.StepPaperMm, warnings);
            if (options.PlaceTags) PlaceNumberTags(document, view, instances, group.Nodes, slots, options.TagTypeId, options.StepPaperMm, warnings);
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

    private static ViewPlan CreateView(Document document, string name, Level level)
    {
        var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.FloorPlan)
            ?? throw new InvalidOperationException("프로젝트에 평면뷰 유형이 없습니다.");
        var view = ViewPlan.Create(document, type.Id, level.Id);
        view.Name = name;
        return view;
    }

    private static void ConfigureRange(ViewPlan view, Level level, IReadOnlyList<FamilyInstance> instances)
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
        IReadOnlyList<VerticalSlot> slots, double stepPaperMm)
    {
        var bounds = Bounds(instances);
        var maxOrder = slots.Count == 0 ? 0 : slots.Max(x => x.Order);
        var shift = view.UpDirection * RevitUnitConverter.MmToFt(maxOrder * stepPaperMm * view.Scale);
        var crop = view.CropBox;
        var inverse = crop.Transform.Inverse;
        var corners = new[]
        {
            new XYZ(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new XYZ(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
            new XYZ(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
            new XYZ(bounds.Max.X, bounds.Max.Y, bounds.Min.Z)
        };
        var local = corners.Concat(corners.Select(x => x + shift)).Select(inverse.OfPoint).ToList();
        var margin = RevitUnitConverter.MmToFt(1000);
        crop.Min = new XYZ(local.Min(x => x.X) - margin, local.Min(x => x.Y) - margin, crop.Min.Z);
        crop.Max = new XYZ(local.Max(x => x.X) + margin, local.Max(x => x.Y) + margin, crop.Max.Z);
        view.CropBoxActive = true;
        view.CropBox = crop;
    }

    private static (XYZ Min, XYZ Max) Bounds(IReadOnlyList<FamilyInstance> instances)
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
            var keep = category.Id.Value == (long)BuiltInCategory.OST_PipeFitting ||
                category.Id.Value == (long)BuiltInCategory.OST_PipeAccessory;
            view.SetCategoryHidden(category.Id, !keep);
        }
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
            foreach (var parameter in parameters) parameter.Set(0d);
        }
        return warnings;
    }

    private static void WriteOffsets(IReadOnlyList<FamilyInstance> instances, IReadOnlyList<VerticalSlot> slots,
        ViewPlan view, double stepPaperMm, List<string> warnings)
    {
        var byId = instances.ToDictionary(x => x.Id.Value);
        foreach (var slot in slots)
        {
            if (!byId.TryGetValue(slot.ElementId, out var instance) ||
                !TryParameters(instance, warnings, out var parameters)) continue;
            var distance = RevitUnitConverter.MmToFt(slot.Order * stepPaperMm * view.Scale);
            var world = view.UpDirection * distance;
            var transform = instance.GetTransform();
            var local = SymbolOffsetMath.ToLocal(ToVector(world), ToVector(transform.BasisX),
                ToVector(transform.BasisY), ToVector(transform.BasisZ));
            parameters[0].Set(local.X);
            parameters[1].Set(local.Y);
            parameters[2].Set(local.Z);
        }
    }

    private static SymbolVector ToVector(XYZ vector) => new(vector.X, vector.Y, vector.Z);

    private static bool TryParameters(FamilyInstance instance, List<string> warnings, out Parameter[] parameters)
    {
        var names = new[] { JunctionDetailParameters.SymbolOffsetX, JunctionDetailParameters.SymbolOffsetY,
            JunctionDetailParameters.SymbolOffsetZ };
        var found = names.Select(instance.LookupParameter).ToArray();
        parameters = [];
        if (found.All(x => x is null))
        {
            warnings.Add($"부재 {instance.Id.Value}: 패밀리 갱신 필요 (심볼 오프셋 매개변수 없음)");
            return false;
        }
        if (found.Any(x => x is null || x.IsReadOnly || x.StorageType != StorageType.Double))
        {
            warnings.Add($"부재 {instance.Id.Value}: 심볼 오프셋 매개변수가 없거나 읽기 전용입니다.");
            return false;
        }
        parameters = found.Select(x => x!).ToArray();
        return true;
    }

    private static void PlaceNumberTags(Document document, ViewPlan view, IReadOnlyList<FamilyInstance> instances,
        IReadOnlyList<JunctionNode> nodes, IReadOnlyList<VerticalSlot> slots, long tagTypeId, double stepPaperMm, List<string> warnings)
    {
        var types = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_MultiCategoryTags)
            .WhereElementIsElementType().OfType<FamilySymbol>().ToList();
        var selectedType = tagTypeId > 0 ? types.FirstOrDefault(x => x.Id.Value == tagTypeId) : null;
        if (selectedType is null && types.Count != 1)
        {
            warnings.Add(types.Count == 0 ? "격점 번호 태그 유형이 없어 태그를 만들지 않았습니다."
                : "격점 번호 태그 유형이 여러 개라 대상을 정할 수 없습니다.");
            return;
        }
        var type = selectedType ?? types[0];
        if (!type.IsActive) type.Activate();
        var taggedIds = new FilteredElementCollector(document, view.Id).OfClass(typeof(IndependentTag))
            .Cast<IndependentTag>().SelectMany(x => x.GetTaggedLocalElementIds())
            .ToHashSet();
        var orderById = slots.ToDictionary(x => x.ElementId, x => x.Order);
        var verticalIds = nodes.Where(x => x.Connectors.Count > 0 &&
                x.Connectors.All(connector => Math.Abs(connector.Dz) >= JunctionVerticalChain.VerticalCosine))
            .Select(x => x.Id).ToHashSet();
        var gap = RevitUnitConverter.MmToFt(4.5 * view.Scale);
        foreach (var instance in instances)
        {
            if (taggedIds.Contains(instance.Id)) continue;
            var center = instance.GetTransform().Origin;
            var isVertical = verticalIds.Contains(instance.Id.Value);
            if (isVertical && orderById.TryGetValue(instance.Id.Value, out var order))
                center += view.UpDirection * RevitUnitConverter.MmToFt(order * stepPaperMm * view.Scale);
            var position = isVertical ? center - view.RightDirection * gap : center - view.UpDirection * gap;
            try
            {
                IndependentTag.Create(document, type.Id, view.Id, new Reference(instance), false,
                    TagOrientation.Horizontal, position);
            }
            catch (Exception ex)
            {
                warnings.Add($"부재 {instance.Id.Value}: 번호 태그를 만들 수 없습니다 ({ex.Message})");
            }
        }
    }
}
