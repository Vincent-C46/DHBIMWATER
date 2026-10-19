using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

/// <summary>캔버스 절점 부속 전환 — 곡관 ↔ 밸브부관 B, T형 ↔ 밸브부관 A(docs/225 § B·C·E, docs/227 § B·C·E).</summary>
public sealed class PipeLayoutValveBendTests
{
    private const string ValveBendFamily = "DH_밸브부관 : 100A";
    private const string ValveTeeFamily = "DH_밸브부관A : 1650A";

    [Fact]
    public void ValveBendFamilyList_IsFilteredByKeyword_AndAutoSelected()
    {
        var vm = CreateViewModel();

        // 밸브부관은 배관 부속류다 — 콤보(SegmentFamilyTypeNames)에 있고, 기본값 후보는 키워드가 든 부속류만이다.
        Assert.Contains(ValveBendFamily, vm.SegmentFamilyTypeNames);
        Assert.Contains(ValveTeeFamily, vm.SegmentFamilyTypeNames);
        Assert.Equal(2, vm.ValveBendFamilyTypeNames.Count);
        Assert.DoesNotContain("곡관90 : DN100", vm.ValveBendFamilyTypeNames);
        // A는 패밀리명(유형명 제외)의 `A` 표기로, B는 A가 아닌 첫 항목으로 추정한다.
        Assert.Equal(ValveTeeFamily, vm.ValveTeeFamilyTypeName);
        Assert.Equal(ValveBendFamily, vm.ValveBendFamilyTypeName);
    }

    [Fact]
    public void SetNodeFittingKind_ChangesNodeItem_AndCanBeUndone()
    {
        var vm = CreateCornerViewModel();
        var corner = CornerItem(vm);

        vm.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);

        Assert.True(CornerItem(vm).IsValveBend);
        Assert.Equal(PipeValveBend.PartNameB, CornerItem(vm).Label);
        Assert.Contains("밸브부관 A 0 · B 1", vm.NetworkSummary);

        vm.UndoCommand.Execute(null);

        Assert.False(CornerItem(vm).IsValveBend);
    }

    [Fact]
    public void Validation_RequiresValveSide_UntilFlipped()
    {
        var vm = CreateCornerViewModel();
        var corner = CornerItem(vm);
        vm.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);

        Assert.Contains("밸브 쪽을 정할 수 없는 밸브부관 B 1개", vm.ValidationSummary);
        Assert.False(CornerItem(vm).HasValveSide);

        vm.FlipValveBendSide(corner.Id);

        Assert.DoesNotContain("밸브 쪽을 정할 수 없는", vm.ValidationSummary);
        Assert.True(CornerItem(vm).HasValveSide);
    }

    [Fact]
    public void Validation_RejectsNonRightAngleValveBend()
    {
        var vm = CreateDrawingViewModel();
        vm.UseAngleSnap = false;
        Draw(vm, new(0, 0), new(1000, 0));
        Draw(vm, new(1000, 0), new(1866, 500));
        var corner = CornerItem(vm);
        vm.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);
        vm.FlipValveBendSide(corner.Id);

        Assert.Contains("90°로 꺾이지 않은 밸브부관 B 1개", vm.ValidationSummary);
    }

    [Fact]
    public void Validation_RequiresValveBendFamily_ButNotBendFamily_WhenAllElbowsAreValveBends()
    {
        var vm = CreateCornerViewModel();
        var corner = CornerItem(vm);
        vm.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);
        vm.FlipValveBendSide(corner.Id);
        vm.BendFamilyTypeName = null;
        vm.ValveBendFamilyTypeName = null;

        Assert.DoesNotContain("곡관 패밀리를 선택하세요", vm.ValidationSummary);
        Assert.Contains("밸브부관 B 패밀리 선택 필요", vm.ValidationSummary);
    }

    [Fact]
    public void CreateModel_SendsValveTeeAndValveBend_WithMainDiameterFromValveTee()
    {
        // docs/227 — 본관 D1650에 밸브부관 A(T자), 분기 D300이 90° 꺾인 곳에 밸브부관 B.
        var vm = CreateBypassViewModel();
        vm.SetNodeFittingKind(vm.Nodes.Single(x => x.IsTee).Id, PipeNodeFittingKind.ValveTee);
        vm.SetNodeFittingKind(CornerItem(vm).Id, PipeNodeFittingKind.ValveBend);
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        Assert.Equal("", vm.ValidationSummary);
        vm.CreateModelCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Single(request!.Nodes, x => x.IsValveTee);
        var bend = Assert.Single(request.Nodes, x => x.IsValveBend);
        Assert.Equal(1650, bend.MainDiameterMm);
        // 밸브 없이도 A 반대쪽 규칙으로 밸브 쪽이 정해진다 — (1000,1000)→(2000,1000) 선.
        var valveSide = request.Edges.Single(x => x.Id == bend.ValveSideEdgeId);
        Assert.Equal(2000, Math.Max(valveSide.Start.X, valveSide.End.X), 3);
        Assert.Equal(ValveBendFamily, request.SegmentFamilies!.ValveBendFamilyTypeName);
        Assert.Equal(ValveTeeFamily, request.SegmentFamilies.ValveTeeFamilyTypeName);
        // docs/232 § A-3 — 주관 직경은 키워드 우선순위(`주관_구경` → `주관` …)라 `공칭_구경`보다 `주관구경`이 먼저다.
        Assert.Equal("주관구경", request.SegmentFamilies.ValveTeeMainDiameterParameterName);
        Assert.Equal("수평분기", request.SegmentFamilies.ValveTeeHorizontalBranchParameterName);
        Assert.Equal("수직분기", request.SegmentFamilies.ValveTeeVerticalBranchParameterName);
    }

    [Fact]
    public void Validation_BlocksValveBend_NotLinkedToValveTee()
    {
        var vm = CreateBypassViewModel();
        var corner = CornerItem(vm);
        vm.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);
        vm.FlipValveBendSide(corner.Id);   // 밸브 쪽은 정해졌지만 T가 A가 아니다

        Assert.Contains("밸브부관 A의 분기와 이어지지 않은 밸브부관 B 1개", vm.ValidationSummary);
    }

    [Fact]
    public void ValveTee_OnlyOnTee_AndSkipsTeeFamilyRequirement()
    {
        var vm = CreateBypassViewModel();
        var tee = vm.Nodes.Single(x => x.IsTee);

        vm.SetNodeFittingKind(CornerItem(vm).Id, PipeNodeFittingKind.ValveTee);
        Assert.False(CornerItem(vm).IsValveTee);

        vm.SetNodeFittingKind(tee.Id, PipeNodeFittingKind.ValveTee);
        vm.TeeFamilyTypeName = null;

        Assert.True(vm.Nodes.Single(x => x.IsTee).IsValveTee);
        Assert.Equal(PipeValveBend.PartNameA, vm.Nodes.Single(x => x.IsTee).Label);
        Assert.DoesNotContain("T형 패밀리를 선택하세요", vm.ValidationSummary);
        Assert.Contains("밸브부관 A 1 · B 0", vm.NetworkSummary);
    }

    [Fact]
    public void ApplyPickedPiping_RestoresValveTee()
    {
        var vm = CreateDrawingViewModel();
        var import = new PipeLayoutImport(
            [
                new ImportedEdge(new Point2D(0, 0), new Point2D(1000, 0), 1650),
                new ImportedEdge(new Point2D(1000, 0), new Point2D(2000, 0), 1650),
                new ImportedEdge(new Point2D(1000, 0), new Point2D(1000, 1000), 300),
            ],
            [], 0, [], [])
        {
            NodeFittings = [new ImportedNodeFitting(new Point2D(1000, 0), 7, PipeNodeFittingKind.ValveTee)],
            ValveTeeFamilyTypeName = ValveTeeFamily
        };

        vm.ApplyPickedPiping(import);

        Assert.True(vm.Nodes.Single(x => x.IsTee).IsValveTee);
        Assert.Equal(ValveTeeFamily, vm.ValveTeeFamilyTypeName);
    }

    [Fact]
    public void FamilySettings_RoundTripValveBend()
    {
        var vm = CreateViewModel();
        vm.ValveBendDiameterParameterName = "공칭_구경";
        var saved = vm.ToFamilySettings();

        var other = CreateViewModel();
        other.ValveBendFamilyTypeName = null;
        other.ApplyFamilySettings(saved);

        Assert.Equal(ValveBendFamily, saved.ValveBendFamilyTypeName);
        Assert.Equal(ValveBendFamily, other.ValveBendFamilyTypeName);
        Assert.Equal("공칭_구경", other.ValveBendDiameterParameterName);
        Assert.Equal(ValveBendFamily, saved.ToSelection().ValveBendFamilyTypeName);
    }

    [Fact]
    public void ApplyPickedPiping_RestoresValveBendNodeAndSide()
    {
        var vm = CreateDrawingViewModel();
        var import = new PipeLayoutImport(
            [
                new ImportedEdge(new Point2D(0, 0), new Point2D(3000, 0), 100),
                new ImportedEdge(new Point2D(3000, 0), new Point2D(3000, 2000), 100),
            ],
            [], 0, [], [])
        {
            NodeFittings = [new ImportedNodeFitting(new Point2D(3000, 0), 22, FittingKind: PipeNodeFittingKind.ValveBend, ValveSideDirection: new Point2D(-1, 0))],
            ValveBendFamilyTypeName = ValveBendFamily
        };

        vm.ApplyPickedPiping(import);

        var corner = CornerItem(vm);
        Assert.True(corner.IsValveBend);
        Assert.True(corner.HasValveSide);
        // 표시선이 -X(화면 왼쪽)로 뻗는다.
        Assert.True(corner.ValveSideX2 < corner.X);
        Assert.DoesNotContain("밸브 쪽을 정할 수 없는", vm.ValidationSummary);
    }

    private static PipeNodeItem CornerItem(PipeLayoutViewModel vm) => vm.Nodes.Single(x => x.IsElbow);

    private static PipeLayoutViewModel CreateCornerViewModel()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new(0, 0), new(1000, 0));
        Draw(vm, new(1000, 0), new(1000, 1000));
        return vm;
    }

    /// <summary>본관 (0,0)→(2000,0) D1650, 분기 (1000,0)→(1000,1000) D300, 꺾여서 (1000,1000)→(2000,1000) D300.
    /// T 절점 (1000,0)과 꺾인 절점 (1000,1000)이 생긴다.</summary>
    private static PipeLayoutViewModel CreateBypassViewModel()
    {
        var vm = CreateDrawingViewModel();
        vm.DiameterMm = 1650;
        Draw(vm, new(0, 0), new(2000, 0));
        vm.DiameterMm = 300;
        Draw(vm, new(1000, 0), new(1000, 1000));
        Draw(vm, new(1000, 1000), new(2000, 1000));
        return vm;
    }

    private static PipeLayoutViewModel CreateDrawingViewModel()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.SnapReferencePoint = false;
        vm.SnapEndpoint = false;
        vm.SnapMidpoint = false;
        vm.SnapQuadrant = false;
        vm.SnapIntersection = false;
        vm.SnapPerpendicular = false;
        vm.SnapNearest = false;
        vm.SnapOutline = false;
        return vm;
    }

    private static void Draw(PipeLayoutViewModel vm, Point2D start, Point2D end)
    {
        vm.HandleCanvasClick(vm.Transform.ToScreen(start));
        vm.HandleCanvasClick(vm.Transform.ToScreen(end));
    }

    // 수직분기(docs/301) — 이 파일의 스텁이 밸브부관 A·관 패밀리를 갖고 있어 여기 둠.
    [Fact]
    public void VerticalBranch_ConfirmedDialog_SendsRequestWithModelFamilies()
    {
        var vm = CreateViewModel();
        VerticalBranchRequest? sent = null;
        VerticalBranchViewModel? shown = null;
        vm.OpenVerticalBranchDialog = dialog => { shown = dialog; return true; };
        vm.PlaceVerticalBranchAction = request => sent = request;

        vm.ApplyVerticalBranchStart(new VerticalBranchStart(42, ValveTeeFamily, 100, 0));

        Assert.NotNull(shown);
        Assert.Contains("DH_게이트 밸브 : 100A", shown!.AddOptions);
        Assert.NotNull(sent);
        Assert.Equal(42, sent!.StartElementId);
        Assert.Equal(vm.SegmentFamilyTypeName, sent.Families.SegmentFamilyTypeName);
        Assert.Equal(vm.ValveTeeVerticalBranchParameterName, sent.Families.ValveTeeVerticalBranchParameterName);
        Assert.True(Assert.Single(sent.Parts).IsPipe);
    }

    [Fact]
    public void VerticalBranch_CancelOrPickError_SendsNothing()
    {
        var vm = CreateViewModel();
        var sent = 0;
        vm.OpenVerticalBranchDialog = _ => false;
        vm.PlaceVerticalBranchAction = _ => sent++;

        vm.ApplyVerticalBranchStart(new VerticalBranchStart(42, ValveTeeFamily, 100, 0));
        Assert.Equal("수직분기 취소", vm.Status);
        vm.ApplyVerticalBranchStart(null, "[PL-313] 위 커넥터 없음");
        Assert.Equal("[PL-313] 위 커넥터 없음", vm.Status);
        Assert.Equal(0, sent);

        vm.ApplyVerticalBranchResult(true, "부품 2개 배치");
        Assert.Equal("최근: 부품 2개 배치", vm.VerticalBranchLastResult);
    }

    private static PipeLayoutViewModel CreateViewModel() => PipeLayoutTestDefaults.UseAbsoluteElevation(new(new StubRepo()));

    private sealed class StubRepo : IElementTypeQueryRepo
    {
        public IEnumerable<string> GetSlabTypeNames() => [];
        public IEnumerable<string> GetWallTypeNames() => [];
        public IEnumerable<string> GetColumnTypeNames() => [];
        public IEnumerable<string> GetColumnFamilyTypeNames() => [];
        public IEnumerable<string> GetBeamTypeNames() => [];
        public IEnumerable<string> GetConcreteColumnTypeNames() => [];
        public IEnumerable<string> GetConcreteBeamTypeNames() => [];
        public IEnumerable<string> GetStructuralFramingFamilyTypeNames() => [];
        public IEnumerable<string> GetAdaptiveComponentTypeNames() => [];
        public IEnumerable<string> GetAdaptiveInstanceParameterNames(string familyTypeName) => [];
        public int GetAdaptiveBendPointCount(string familyTypeName) => -1;
        public int? GetFamilyTypeCategoryId(string familyTypeName) => null;
        public IEnumerable<string> GetPipingSystemTypeNames() => ["급수"];
        public IEnumerable<string> GetPipeTypeNames() => ["배관"];
        public IEnumerable<string> GetLevelNames() => ["1층"];
        public IEnumerable<string> GetPipeAccessoryTypeNames() => ["DH_게이트 밸브 : 100A"];
        public IEnumerable<string> GetPipeFittingTypeNames() => ["직관 : DN100", "곡관90 : DN100", "T형 : DN100", ValveBendFamily, ValveTeeFamily];
        public IEnumerable<string> GetPipeAccessoryInstanceParameterNames(string familyTypeName)
            => ["길이", "DN", "공칭_구경", "수평분기", "수평분기_구경", "수직분기", "주관구경"];
        public IEnumerable<string> GetGenericModelTypeNames() => [];
        public IEnumerable<string> GetFoundationTypeNames() => [];
        public IEnumerable<string> GetBeamInstanceParameterNames(string beamTypeName) => [];
        public double GetStructuralFramingSectionWidthMm(string familyTypeName) => 400;
        public (double WidthX, double DepthY) GetStructuralColumnSectionSizeMm(string familyTypeName) => (400, 400);
        public IEnumerable<string> GetPipeAccessoryNumericInstanceParameterNames(string familyTypeName) => GetPipeAccessoryInstanceParameterNames(familyTypeName);
        public IReadOnlyList<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto> GetPipeAccessoryTypeLengthParameters(string familyTypeName) => [];
        public void InvalidateFamilyCaches() { }
    }
}
