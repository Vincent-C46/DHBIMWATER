using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

/// <summary>캔버스 바이패스 세트 배치(docs/228 § B).</summary>
public sealed class PipeLayoutBypassSetTests
{
    private const string MainValve1650 = "DH_버터플라이 밸브 : 1650A";
    private const string GateValve300 = "DH_게이트 밸브 : 300A";

    [Fact]
    public void Defaults_PreferGateForBypass_WithMatchingDn()
    {
        var vm = CreateViewModel();

        Assert.Equal("DH_게이트 밸브", vm.BypassValveFamilyName);
        Assert.Equal(GateValve300, vm.BypassValveTypeName);
        Assert.All(vm.BypassValveTypeNames, x => Assert.StartsWith("DH_게이트 밸브", x));
    }

    [Fact]
    public void PlacingOnMainPipe_AddsOneSet_WithoutMainValve_OnCursorSide()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;

        Assert.True(vm.IsDrawMode);
        Assert.True(vm.IsPlacingFitting);
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));   // 본관(→) 왼쪽 위
        Assert.True(vm.IsBypassPreviewVisible);
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));

        var set = Assert.Single(vm.BypassSetItems);
        // 본관 밸브는 세트가 아니다(docs/230) — 패밀리명은 비고 라벨은 세트 이름이다.
        Assert.Equal("", set.FamilyTypeName);
        Assert.Equal(BypassSet.TypeKey, set.DisplayName);
        Assert.Equal(new BypassSetSpec(GateValve300, 300, 1), set.Bypass);
        Assert.NotNull(set.BypassLine);
        Assert.Contains("세트 1", vm.NetworkSummary);
    }

    [Fact]
    public void MainValve_CanBePlacedInsideSet_AsOrdinaryValve()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.IsPlacingBypassSet = false;

        // 세트 중심과 같은 자리에 일반 밸브 — 간격 규칙이 서로 밀어내지 않아야 한다(docs/230 § B).
        vm.SelectedFamilyTypeName = MainValve1650;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 0)));

        var valve = Assert.Single(vm.InlineFittings, x => !x.IsBypassSet);
        Assert.Equal(MainValve1650, valve.FamilyTypeName);
        Assert.Equal(vm.BypassSetItems.Single().T, valve.T, 6);
    }

    [Fact]
    public void CursorBelowPipe_PlacesOnRightSide_AndFlipMovesIt()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, -50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, -50)));
        Assert.Equal(-1, vm.BypassSetItems.Single().Bypass!.Side);

        vm.FlipBypassSetCommand.Execute(vm.BypassSetItems.Single());

        Assert.Equal(1, vm.BypassSetItems.Single().Bypass!.Side);
        vm.UndoCommand.Execute(null);
        Assert.Equal(-1, vm.BypassSetItems.Single().Bypass!.Side);
    }

    [Fact]
    public void BypassDiameterNotSmallerThanMain_IsRejected()
    {
        var vm = CreateMainPipeViewModel();
        vm.BypassDiameterMm = 1650;
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));

        Assert.Empty(vm.BypassSetItems);
        Assert.Contains("본관 관경", vm.Status);
    }

    [Fact]
    public void Escape_EndsPlacement_AndSelectingValveCancelsIt()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleEscape();
        Assert.False(vm.IsPlacingBypassSet);

        vm.IsPlacingBypassSet = true;
        vm.SelectedFamilyTypeName = GateValve300;
        Assert.False(vm.IsPlacingBypassSet);
    }

    [Fact]
    public void CreateModel_SendsBypassSet_AndRequiresValveBendFamilies()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.IsPlacingBypassSet = false;

        vm.ValveTeeFamilyTypeName = null;
        Assert.Contains("밸브부관 A 패밀리 선택 필요", vm.ValidationSummary);
        vm.ValveTeeFamilyTypeName = "DH_밸브부관A : 1650A";

        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;
        vm.CreateModelCommand.Execute(null);

        Assert.NotNull(request);
        var set = Assert.Single(request!.InlineFittings, x => x.IsBypassSet);
        Assert.Equal("", set.FamilyTypeName);
        Assert.Equal(GateValve300, set.Bypass!.BypassValveFamilyTypeName);
    }

    [Theory]
    [InlineData(1, 1)]     // 바이패스가 +Y 쪽 — 가져온 선 방향(+X)의 왼쪽
    [InlineData(-1, -1)]
    public void ApplyPickedPiping_RestoresBypassSet_WithSideFromNormal(int normalY, int expectedSide)
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        var import = new PipeLayoutImport(
            [new ImportedEdge(new Point2D(-200, 0), new Point2D(2200, 0), 1650)],
            [new ImportedFitting(0, "", 0.5)
            {
                Bypass = new ImportedBypass(GateValve300, 300, new Point2D(0, normalY), [1, 2, 3, 4, 22])
            }],
            0, [1, 2, 3, 4, 22], []);

        vm.ApplyPickedPiping(import);

        var set = Assert.Single(vm.BypassSetItems);
        Assert.Equal(expectedSide, set.Bypass!.Side);
        Assert.DoesNotContain("바이패스 밸브가 비어 있는", vm.ValidationSummary);
    }

    [Fact]
    public void ApplyBypassValves_ReplacesBypassValve_AndKeepsSide()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.ApplyPickedPiping(new PipeLayoutImport(
            [new ImportedEdge(new Point2D(-200, 0), new Point2D(2200, 0), 1650)],
            [new ImportedFitting(0, "", 0.5) { Bypass = new ImportedBypass("", 300, new Point2D(0, -1), [1]) }],
            0, [1], []));
        Assert.Contains("바이패스 밸브가 비어 있는 세트 1개", vm.ValidationSummary);

        vm.ApplyBypassValvesCommand.Execute(vm.BypassSetItems.Single());

        var set = vm.BypassSetItems.Single();
        Assert.Equal(GateValve300, set.Bypass!.BypassValveFamilyTypeName);
        Assert.Equal(-1, set.Bypass.Side);
        Assert.DoesNotContain("바이패스 밸브가 비어 있는", vm.ValidationSummary);
    }

    private static PipeLayoutViewModel CreateMainPipeViewModel()
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
        vm.DiameterMm = 1650;
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(0, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(4000, 0)));
        return vm;
    }

    private static PipeLayoutViewModel CreateViewModel() => new(new StubRepo());

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
        public IEnumerable<string> GetPipeAccessoryTypeNames()
            => ["DH_게이트 밸브 : 1650A", GateValve300, MainValve1650, "DH_버터플라이 밸브 : 300A"];
        public IEnumerable<string> GetPipeFittingTypeNames()
            => ["직관 : DN100", "곡관90 : DN100", "T형 : DN100", "DH_밸브부관A : 1650A", "DH_밸브부관B : 300A"];
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
