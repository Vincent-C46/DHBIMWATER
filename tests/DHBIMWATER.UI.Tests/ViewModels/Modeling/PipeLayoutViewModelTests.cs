using DHBIMWATER.Application.DTOs.Revit;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.ViewModels.Modeling;
using System.Text.Json;
using System.Windows;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

public sealed class PipeLayoutViewModelTests
{
    [Fact]
    public void GridSnapMm_DefaultsToTenMillimeters()
    {
        var vm = CreateViewModel();

        Assert.Equal(10, vm.GridSnapMm);
    }

    [Fact]
    public void SelectionBox_Window_SelectsOnlyFullyContainedEdges()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        Draw(vm, new Point2D(0, 1000), new Point2D(1000, 1000));
        vm.Mode = PipeLayoutMode.Selection;
        var edge = vm.Edges.OrderBy(x => x.Y1).Last();

        vm.BeginSelectionBox(new Point(Math.Min(edge.X1, edge.X2) - 10, edge.Y1 - 10));
        vm.CommitSelectionBox(new Point(Math.Max(edge.X1, edge.X2) + 10, edge.Y1 + 10));

        Assert.False(vm.IsCrossingSelection);
        Assert.Equal([edge.Id], vm.SelectedEdgeIds);
        Assert.True(vm.Edges.Single(x => x.Id == edge.Id).IsSelected);
    }

    [Fact]
    public void SelectionBox_Crossing_SelectsIntersectingEdgeWithEndpointsOutside()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.Mode = PipeLayoutMode.Selection;
        var edge = Assert.Single(vm.Edges);
        var centerX = (edge.X1 + edge.X2) / 2;

        vm.BeginSelectionBox(new Point(centerX + 10, edge.Y1 - 10));
        vm.CommitSelectionBox(new Point(centerX - 10, edge.Y1 + 10));

        Assert.True(vm.IsCrossingSelection);
        Assert.Equal([edge.Id], vm.SelectedEdgeIds);
    }

    [Fact]
    public void SelectionBox_SelectsEdgeAndFitting_ThenDeleteRemovesBoth()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(500, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));
        vm.Mode = PipeLayoutMode.Selection;
        var edge = Assert.Single(vm.Edges);
        var fitting = Assert.Single(vm.InlineFittings);

        vm.BeginSelectionBox(new Point(Math.Min(edge.X1, edge.X2) - 10, edge.Y1 - 10));
        vm.CommitSelectionBox(new Point(Math.Max(edge.X1, edge.X2) + 10, edge.Y1 + 10));

        Assert.Equal([edge.Id], vm.SelectedEdgeIds);
        Assert.Equal([fitting.Id], vm.SelectedFittingIds);
        Assert.True(vm.DeleteSelectedEdgeCommand.CanExecute(null));

        vm.DeleteSelectedEdgeCommand.Execute(null);

        Assert.Empty(vm.Edges);
        Assert.Empty(vm.InlineFittings);
        Assert.Empty(vm.SelectedEdgeIds);
        Assert.Empty(vm.SelectedFittingIds);
    }

    [Fact]
    public void SelectionBox_ShortClick_ClearsExistingSelection()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.Mode = PipeLayoutMode.Selection;
        vm.SelectEdge(Assert.Single(vm.Edges).Id);

        vm.BeginSelectionBox(new Point(200, 200));
        vm.CommitSelectionBox(new Point(202, 202));

        Assert.Empty(vm.SelectedEdgeIds);
        Assert.Null(vm.SelectedEdge);
        Assert.False(vm.IsSelectionBoxVisible);
    }

    [Fact]
    public void HandleEscape_ClearsMultiSelection()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.Mode = PipeLayoutMode.Selection;
        var edge = Assert.Single(vm.Edges);
        vm.BeginSelectionBox(new Point(Math.Min(edge.X1, edge.X2) - 10, edge.Y1 - 10));
        vm.CommitSelectionBox(new Point(Math.Max(edge.X1, edge.X2) + 10, edge.Y1 + 10));

        vm.HandleEscape();

        Assert.Empty(vm.SelectedEdgeIds);
        Assert.Empty(vm.SelectedFittingIds);
        Assert.All(vm.Edges, x => Assert.False(x.IsSelected));
    }

    /// <summary>확정된 선의 SubLine은 <b>그 선의</b> 관경을 따른다. 기본 관경을 바꿔도 이미 그린 선은
    /// 그대로다 — 선별 관경으로 바뀐 뒤의 동작이다(docs/152 § D-3).</summary>
    [Fact]
    public void ConfirmedEdge_SubLinesFollowOwnDiameter()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.DiameterMm = 200;
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));

        var edge = Assert.Single(vm.Edges);
        Assert.Equal(100 * vm.Transform.PixelsPerMillimeter, Math.Abs(edge.SubAY1 - edge.Y1), 6);

        vm.DiameterMm = 400;

        edge = Assert.Single(vm.Edges);
        Assert.Equal(200, edge.DiameterMm);
        Assert.Equal(100 * vm.Transform.PixelsPerMillimeter, Math.Abs(edge.SubAY1 - edge.Y1), 6);
    }

    /// <summary>선택한 선의 관경을 바꾸면 그 선의 SubLine도 따라간다.</summary>
    [Fact]
    public void SelectedEdge_SubLinesFollowEditedDiameter()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.DiameterMm = 200;
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.Mode = PipeLayoutMode.Selection;
        vm.SelectEdge(Assert.Single(vm.Edges).Id);

        vm.DiameterMm = 400;

        var edge = Assert.Single(vm.Edges);
        Assert.Equal(400, edge.DiameterMm);
        Assert.Equal(200 * vm.Transform.PixelsPerMillimeter, Math.Abs(edge.SubAY1 - edge.Y1), 6);
    }

    [Fact]
    public void DiameterMm_MarksOnlyMatchingDnValveFamily()
    {
        var vm = CreateViewModel(["게이트밸브 200A : 표준", "게이트밸브 300A : 표준", "게이트밸브 400A : 표준"]);

        vm.DiameterMm = 300;

        Assert.Equal("게이트밸브 300A", Assert.Single(vm.ValveFamilyNames, item => item.IsDiameterMatched).Name);
    }

    [Fact]
    public void DiameterMm_DoesNotSelectFamilyType_WhenNothingWasSelected()
    {
        var vm = CreateViewModel(["게이트밸브 200A : 표준", "게이트밸브 300A : 표준"]);

        vm.DiameterMm = 300;

        Assert.Equal("게이트밸브 300A", vm.SelectedValveFamilyName);
        Assert.Null(vm.SelectedFamilyTypeName);
        Assert.False(vm.IsPlacingFitting);
    }

    [Fact]
    public void DiameterMm_ReplacesSelectedValveFamily_WithMatchingFamilyInSameSeries()
    {
        var vm = CreateViewModel(["게이트밸브 200A : 표준", "게이트밸브 300A : 표준", "다른밸브 300A : 표준"]);
        vm.DiameterMm = 100;
        vm.SelectedValveFamilyName = "게이트밸브 200A";
        vm.SelectedFamilyTypeName = "게이트밸브 200A : 표준";

        vm.DiameterMm = 300;

        Assert.Equal("게이트밸브 300A", vm.SelectedValveFamilyName);
        Assert.Null(vm.SelectedFamilyTypeName);
    }

    [Fact]
    public void DiameterMm_KeepsSelectedValveFamily_WhenSameSeriesHasNoMatchingFamily()
    {
        var vm = CreateViewModel(["게이트밸브 200A : 표준", "다른밸브 300A : 표준"]);
        vm.SelectedValveFamilyName = "게이트밸브 200A";
        vm.SelectedFamilyTypeName = "게이트밸브 200A : 표준";

        vm.DiameterMm = 300;

        Assert.Equal("게이트밸브 200A", vm.SelectedValveFamilyName);
        Assert.Equal("게이트밸브 200A : 표준", vm.SelectedFamilyTypeName);
    }

    [Fact]
    public void DiameterMm_DoesNotUseNearestValveFamily_WhenNoExactDnExists()
    {
        var vm = CreateViewModel(["게이트밸브 200A : 표준", "게이트밸브 400A : 표준", "다른밸브 300A : 표준"]);
        vm.SelectedValveFamilyName = "게이트밸브 200A";
        vm.SelectedFamilyTypeName = "게이트밸브 200A : 표준";

        vm.DiameterMm = 350;

        Assert.Equal("게이트밸브 200A", vm.SelectedValveFamilyName);
        Assert.Equal("게이트밸브 200A : 표준", vm.SelectedFamilyTypeName);
    }

    [Fact]
    public void DrawingPreview_ZeroLength_HidesSubLines()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        var point = new Point2D(1000, 1000);
        vm.HandleCanvasClick(vm.Transform.ToScreen(point));

        vm.HandleCanvasMove(vm.Transform.ToScreen(point));

        Assert.False(vm.IsSubLineVisible);
    }

    [Fact]
    public void DrawingPreview_HorizontalPipe_OffsetsSubLinesByPipeRadius()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.DiameterMm = 200;
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(1000, 1500)));

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(3000, 1500)));

        var expectedOffset = 100 * vm.Transform.PixelsPerMillimeter;
        Assert.True(vm.IsSubLineVisible);
        Assert.Equal(expectedOffset, Math.Abs(vm.SubLineAY1 - vm.PreviewY1), 6);
        Assert.Equal(expectedOffset, Math.Abs(vm.SubLineBY1 - vm.PreviewY1), 6);
        Assert.True((vm.SubLineAY1 - vm.PreviewY1) * (vm.SubLineBY1 - vm.PreviewY1) < 0);
    }

    [Fact]
    public void TempDimensions_HorizontalPipe_AddsOnlyPerpendicularSubLineDimension()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.DiameterMm = 200;
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(-1000, 0)));

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(1000, 0)));

        Assert.Equal(3, vm.TempDimensions.Count);
        Assert.Equal(2, vm.TempDimensions.Count(x => !x.IsSubLine));
        var subLine = Assert.Single(vm.TempDimensions, x => x.IsSubLine);
        Assert.Equal("#C39BD3", subLine.Stroke);
    }

    [Fact]
    public void TempDimensions_DiagonalPipe_AddsSubLineDimensionInBothDirections()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.DiameterMm = 200;
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(-1000, -1000)));

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(0, 0)));

        Assert.Equal(4, vm.TempDimensions.Count);
        Assert.Equal(2, vm.TempDimensions.Count(x => x.IsSubLine));
    }

    [Fact]
    public void TempDimensions_WithoutOutline_RemainEmpty()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(1000, 1000)));

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 2000)));

        Assert.True(vm.IsSubLineVisible);
        Assert.Empty(vm.TempDimensions);
    }

    [Fact]
    public void DrawingCleanup_CancelClearAndUndo_HideSubLinesAndClearDimensions()
    {
        var cancelVm = CreateDrawingViewModel();
        PrepareVisibleSubLinePreview(cancelVm);
        cancelVm.CancelDrawingCommand.Execute(null);
        AssertPreviewCleared(cancelVm);

        var clearVm = CreateDrawingViewModel();
        DisableOsnap(clearVm);
        Draw(clearVm, new Point2D(500, 500), new Point2D(750, 500));
        PrepareVisibleSubLinePreview(clearVm);
        clearVm.ClearCommand.Execute(null);
        AssertPreviewCleared(clearVm);

        var undoVm = CreateDrawingViewModel();
        DisableOsnap(undoVm);
        Draw(undoVm, new Point2D(500, 500), new Point2D(750, 500));
        PrepareVisibleSubLinePreview(undoVm);
        undoVm.UndoCommand.Execute(null);
        AssertPreviewCleared(undoVm);
    }

    [Fact]
    public void HandleCanvasMove_SnapsToReferenceAxis_WhenNearestSnapIsEnabled()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.SnapReferencePoint = false;
        vm.SnapNearest = true;   // 근처점은 기본 해제다(2026-09-16). 이 테스트의 대상이므로 명시적으로 켠다.

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(50, 500)));

        var expected = vm.Transform.ToScreen(new Point2D(0, 500));
        Assert.True(vm.IsSnapMarkerVisible);
        Assert.Equal(expected.X, vm.SnapMarkerX, 6);
        Assert.Equal(expected.Y, vm.SnapMarkerY, 6);
        Assert.Equal(expected.X, vm.CursorX, 6);
        Assert.Equal(expected.Y, vm.CursorY, 6);
        Assert.Equal("·", vm.SnapMarkerSymbol);
    }

    [Fact]
    public void HandleCanvasMove_DoesNotSnapToReferenceAxis_WhenNearestSnapIsDisabled()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.SnapReferencePoint = false;
        vm.SnapNearest = false;

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(50, 500)));

        Assert.False(vm.IsSnapMarkerVisible);
    }

    [Fact]
    public void HandleCanvasMove_SnapsPerpendicularToReferenceAxis_WhenDrawing()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.SnapReferencePoint = false;
        vm.SnapNearest = false;
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 500)));

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(50, 500)));

        var expected = vm.Transform.ToScreen(new Point2D(0, 500));
        Assert.True(vm.IsSnapMarkerVisible);
        Assert.Equal(expected.X, vm.CursorX, 6);
        Assert.Equal(expected.Y, vm.CursorY, 6);
        Assert.Equal("⊥", vm.SnapMarkerSymbol);
        Assert.Equal("직교점", vm.SnapMarkerLabel);
    }

    [Fact]
    public void HandleCanvasMove_SnapsToReferenceAxisIntersection_WhenIntersectionSnapIsEnabled()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.SnapReferencePoint = false;
        vm.SnapNearest = false;

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(50, 50)));

        var expected = vm.Transform.ToScreen(new Point2D(0, 0));
        Assert.True(vm.IsSnapMarkerVisible);
        Assert.Equal(expected.X, vm.CursorX, 6);
        Assert.Equal(expected.Y, vm.CursorY, 6);
        Assert.Equal("×", vm.SnapMarkerSymbol);
        Assert.Equal("교차점", vm.SnapMarkerLabel);
    }

    [Fact]
    public void HandleCanvasMove_DoesNotSnapInSelectionMode()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.SnapReferencePoint = false;
        vm.SnapNearest = true;
        vm.Mode = PipeLayoutMode.Selection;
        var raw = new Point2D(50, 500);

        vm.HandleCanvasMove(vm.Transform.ToScreen(raw));

        var expected = vm.Transform.ToScreen(raw);
        Assert.False(vm.IsSnapMarkerVisible);
        Assert.Equal(expected.X, vm.CursorX, 6);
        Assert.Equal(expected.Y, vm.CursorY, 6);
    }

    [Fact]
    public void DrawingPreview_UsesSameReferenceAxisSnapAsConfirmedPoint()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.SnapReferencePoint = false;
        vm.SnapNearest = true;   // 근처점은 기본 해제다(2026-09-16). 이 테스트의 대상이므로 명시적으로 켠다.
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 500)));

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(50, 1000)));

        var expected = vm.Transform.ToScreen(new Point2D(0, 1000));
        Assert.Equal(expected.X, vm.PreviewX2, 6);
        Assert.Equal(expected.Y, vm.PreviewY2, 6);
    }

    [Fact]
    public void GridSnapMm_AcceptsCustomPositiveValue_AndRejectsNonPositiveValue()
    {
        var vm = CreateViewModel();

        vm.GridSnapMm = 25;

        Assert.Equal(25, vm.GridSnapMm);
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.GridSnapMm = 0);
        Assert.Equal(25, vm.GridSnapMm);
    }

    [Fact]
    public void StraightLengthMm_RejectsNonPositiveValue_AndAcceptsCustomValue()
    {
        var vm = CreateViewModel();

        Assert.Throws<ArgumentOutOfRangeException>(() => vm.StraightLengthMm = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.StraightLengthMm = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.StraightLengthMm = double.NaN);

        vm.StraightLengthMm = 500;

        Assert.Equal(500, vm.StraightLengthMm);
    }

    // docs/245: 정척 수직보조선 — 엣지 시작점부터 정척 길이마다, 끝점과 겹치는 눈금은 제외.
    [Theory]
    [InlineData(13000, 2)]
    [InlineData(12000, 1)]
    [InlineData(5000, 0)]
    public void StraightTicks_CommittedEdge_DrawsTickEveryStraightLength(double lengthMm, int expected)
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.StraightLengthMm = 6000;

        Draw(vm, new Point2D(0, 0), new Point2D(lengthMm, 0));

        Assert.Equal(expected, CountTicks(Assert.Single(vm.Edges).StraightTicks));
    }

    [Fact]
    public void StraightTicks_ChangingStraightLength_RedrawsCommittedEdge()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.StraightLengthMm = 6000;
        Draw(vm, new Point2D(0, 0), new Point2D(13000, 0));

        vm.StraightLengthMm = 4000;

        Assert.Equal(3, CountTicks(Assert.Single(vm.Edges).StraightTicks));
    }

    [Fact]
    public void StraightTicks_Preview_FollowsCursor()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.StraightLengthMm = 6000;
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(0, 0)));

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(13000, 0)));

        Assert.True(vm.IsSubLineVisible);
        Assert.Equal(2, CountTicks(vm.SubLineTicks));
    }

    // docs/314: 인라인 부속 중심점에서 정척을 다시 센다(폭 0).
    [Theory]
    [InlineData(15000, new double[0], new double[] { 6000, 12000 })]
    [InlineData(15000, new[] { 0.5 }, new double[] { 6000, 13500 })]
    [InlineData(30000, new[] { 0.2, 0.8 }, new double[] { 12000, 18000 })]
    [InlineData(30000, new[] { 0.5, 0.5 }, new double[] { 6000, 12000, 21000, 27000 })]
    public void StraightTicks_RestartAtInlineFitting(double lengthMm, double[] fittingTs, double[] expectedMm)
    {
        var vm = CreateDrawingViewModel();
        vm.StraightLengthMm = 6000;

        vm.ApplyPickedPiping(CreateImport(edges: [new ImportedEdge(new Point2D(0, 0), new Point2D(lengthMm, 0), 150)],
            fittings: fittingTs.Select(t => new ImportedFitting(0, "밸브 : DN100", t)).ToList()));

        Assert.Equal(expectedMm, TickDistances(vm, Assert.Single(vm.Edges)), new DoubleTolerance(1));
    }

    [Fact]
    public void StraightTicks_FittingDrag_MovesRestartPoint()
    {
        var vm = CreateDrawingViewModel(["밸브 : DN100"]);
        DisableOsnap(vm);
        vm.StraightLengthMm = 6000;
        Draw(vm, new Point2D(0, 0), new Point2D(20000, 0));
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(8000, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(8000, 0)));
        vm.SelectedFamilyTypeName = null;
        vm.Mode = PipeLayoutMode.Selection;
        Assert.Equal([6000, 14000], TickDistances(vm, Assert.Single(vm.Edges)), new DoubleTolerance(1));

        var fitting = Assert.Single(vm.InlineFittings);
        Assert.True(vm.TryBeginFittingDrag(new Point(fitting.X, fitting.Y), fitting.Id));
        vm.UpdateDrag(vm.Transform.ToScreen(new Point2D(10000, 0)));

        Assert.Equal([6000, 16000], TickDistances(vm, Assert.Single(vm.Edges)), new DoubleTolerance(1));
    }

    private static int CountTicks(System.Windows.Media.Geometry? geometry)
        => geometry is null ? 0 : System.Windows.Media.PathGeometry.CreateFromGeometry(geometry).Figures.Count;

    /// <summary>눈금마다 엣지 시작점에서 눈금 중점까지 모델 거리(mm).</summary>
    private static double[] TickDistances(PipeLayoutViewModel vm, PipeEdgeItem edge)
    {
        if (edge.StraightTicks is null) return [];
        var origin = vm.Transform.ToModel(new Point(edge.X1, edge.Y1));
        return System.Windows.Media.PathGeometry.CreateFromGeometry(edge.StraightTicks).Figures
            .Select(f =>
            {
                var a = f.StartPoint;
                var b = ((System.Windows.Media.LineSegment)f.Segments[0]).Point;
                return vm.Transform.ToModel(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2)).DistanceTo(origin);
            })
            .OrderBy(x => x)
            .ToArray();
    }

    private sealed class DoubleTolerance(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;
        public int GetHashCode(double obj) => 0;
    }

    [Fact]
    public void Mode_DefaultsToDrawing()
    {
        var vm = CreateViewModel();

        Assert.Equal(PipeLayoutMode.Drawing, vm.Mode);
        Assert.True(vm.IsDrawMode);
        Assert.False(vm.IsSelectionMode);
    }

    [Fact]
    public void SelectEdge_InDrawingMode_DoesNotSelectEdge()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        var edgeId = Assert.Single(vm.Edges).Id;

        vm.SelectEdge(edgeId);

        Assert.Null(vm.SelectedEdge);
    }

    [Fact]
    public void SwitchingToDrawingMode_ClearsSelectedEdge()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        var edgeId = Assert.Single(vm.Edges).Id;
        vm.Mode = PipeLayoutMode.Selection;
        vm.SelectEdge(edgeId);
        Assert.NotNull(vm.SelectedEdge);

        vm.Mode = PipeLayoutMode.Drawing;

        Assert.Null(vm.SelectedEdge);
    }

    [Fact]
    public void HandleCanvasClick_InSelectionMode_DoesNotStartDrawing()
    {
        var vm = CreateDrawingViewModel();
        vm.Mode = PipeLayoutMode.Selection;

        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(1000, 1000)));

        Assert.False(vm.IsDrawing);
        Assert.Empty(vm.Edges);
    }

    [Fact]
    public void SwitchingToSelectionMode_WhileDrawing_CancelsDrawing()
    {
        var vm = CreateDrawingViewModel();
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(1000, 1000)));
        Assert.True(vm.IsDrawing);

        vm.Mode = PipeLayoutMode.Selection;

        Assert.False(vm.IsDrawing);
        Assert.False(vm.IsPreviewVisible);
        Assert.True(vm.IsSelectionMode);
    }

    [Fact]
    public void IsPlacingFitting_IsFalseInSelectionMode_WhenFamilyRemainsSelected()
    {
        var vm = CreateViewModel();
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        Assert.True(vm.IsPlacingFitting);

        vm.Mode = PipeLayoutMode.Selection;

        Assert.False(vm.IsPlacingFitting);
        Assert.Equal("밸브 : DN100", vm.SelectedFamilyTypeName);
    }

    [Fact]
    public void SelectFitting_ThenDeleteSelectedFittingCommand_RemovesIt()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(500, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));
        var fittingId = Assert.Single(vm.InlineFittings).Id;
        vm.Mode = PipeLayoutMode.Selection;

        vm.SelectFitting(fittingId);

        Assert.Equal(fittingId, vm.SelectedFitting?.Id);
        Assert.True(Assert.Single(vm.InlineFittings).IsSelected);
        Assert.True(vm.DeleteSelectedFittingCommand.CanExecute(null));

        vm.DeleteSelectedFittingCommand.Execute(null);

        Assert.Empty(vm.InlineFittings);
        Assert.Null(vm.SelectedFitting);
        Assert.Equal("선택 항목 삭제", vm.Status);   // 문구 간결화(docs/239 § C)
    }

    [Fact]
    public void Drawing_CreatesEdgeAtClickedPoint()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);

        Draw(vm, new(1000, 1000), new(4000, 1000));

        var edge = Assert.Single(vm.Edges);
        Assert.Equal(3000, edge.LengthMm, 6);
        AssertPoint(new(4000, 1000), EdgeEnd(vm, edge));
    }

    [Fact]
    public void Drawing_OverStraightLengthWithAngleSnap_CreatesFullLengthEdge()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        var start = new Point2D(1000, 1000);
        var raw = new Point2D(9000, 9000);
        vm.HandleCanvasClick(vm.Transform.ToScreen(start));

        vm.HandleCanvasMove(vm.Transform.ToScreen(raw));

        Assert.Equal("끝점 클릭 → 배관 확정", vm.Status);   // 문구 간결화(docs/239 § C)

        vm.HandleCanvasClick(vm.Transform.ToScreen(raw));

        var edge = Assert.Single(vm.Edges);
        var end = EdgeEnd(vm, edge);
        Assert.Equal(11310, edge.LengthMm, 6);  // 각도 스냅 길이는 기존처럼 10mm 격자로 반올림한다.
        Assert.Equal(end.X - start.X, end.Y - start.Y, 6);
    }

    [Fact]
    public void Drawing_OverStraightLengthWithoutAngleSnap_CreatesFullLengthEdge()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.UseAngleSnap = false;
        var start = new Point2D(1000, 1000);
        var raw = new Point2D(8000, 5000);

        Draw(vm, start, raw);

        var edge = Assert.Single(vm.Edges);
        var end = EdgeEnd(vm, edge);
        Assert.Equal(Math.Sqrt(65_000_000), edge.LengthMm, 6);
        Assert.Equal(0, (end.X - start.X) * (raw.Y - start.Y) - (end.Y - start.Y) * (raw.X - start.X), 6);
    }

    [Fact]
    public void Drawing_OverStraightLengthExistingEndpoint_SnapsExactly()
    {
        var vm = CreateDrawingViewModel();
        vm.StraightLengthMm = 20000;
        Draw(vm, new(10000, 0), new(11000, 0));
        vm.StraightLengthMm = 6000;
        vm.SnapNearest = false;
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(0, 0)));

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(10050, 0)));

        Assert.True(vm.IsSnapMarkerVisible);

        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(10050, 0)));

        var edge = Assert.Single(vm.Edges.Where(x => x.LengthMm > 9000));
        Assert.Equal(10000, edge.LengthMm, 6);
        AssertPoint(new(10000, 0), EdgeEnd(vm, edge));
    }

    [Fact]
    public void Drawing_ExistingEndpoint_SnapsExactly()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new(3000, 0), new(4000, 0));
        vm.SnapNearest = false;

        Draw(vm, new(0, 0), new(3050, 0));

        var edge = Assert.Single(vm.Edges.Where(x => x.LengthMm > 2000));
        Assert.Equal(3000, edge.LengthMm, 6);
        AssertPoint(new(3000, 0), EdgeEnd(vm, edge));
    }

    [Fact]
    public void Drawing_CustomStraightLength_DoesNotLimitCanvasEdge()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.StraightLengthMm = 3000;

        Draw(vm, new(1000, 1000), new(6000, 1000));

        Assert.Equal(5000, Assert.Single(vm.Edges).LengthMm, 6);
    }

    [Fact]
    public void Drawing_PreviewAndConfirmedEndpoint_AreIdentical()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        var start = new Point2D(1000, 1000);
        var raw = new Point2D(9000, 9000);
        vm.HandleCanvasClick(vm.Transform.ToScreen(start));
        vm.HandleCanvasMove(vm.Transform.ToScreen(raw));
        var previewEnd = vm.Transform.ToModel(new Point(vm.PreviewX2, vm.PreviewY2));

        vm.HandleCanvasClick(vm.Transform.ToScreen(raw));

        AssertPoint(previewEnd, EdgeEnd(vm, Assert.Single(vm.Edges)));
    }

    [Fact]
    public void RemoveFittingCommand_RemovesFittingWithoutSelectedEdge()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(0, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(1000, 0)));
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(500, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));
        var fitting = Assert.Single(vm.InlineFittings);

        vm.RemoveFittingCommand.Execute(fitting);

        Assert.Empty(vm.InlineFittings);
    }

    [Fact]
    public void UndoCommand_RestoresPreviousNetworkAfterDrawing()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(0, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(1000, 0)));
        Assert.Single(vm.Edges);

        vm.UndoCommand.Execute(null);

        Assert.Empty(vm.Edges);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void ValidationSummary_ReportsNonOrthogonalThreeWayBranch()
    {
        // 약 63° 분기는 T형도 Y형(30~60°)도 아니다 — 종전 메시지가 그대로 나온다(docs/187 § 1-2).
        // 각도 스냅을 끄지 않으면 45°로 끌려가 Y형이 된다.
        var vm = CreateViewModel();
        vm.UseAngleSnap = false;
        vm.HandleCanvasSizeChanged(1000, 1000);
        Draw(vm, new(-1000, 0), new(1000, 0));
        Draw(vm, new(0, 0), new(300, 600));

        Assert.Contains("직교 T가 아닌 3방향 분기 1개", vm.ValidationSummary);
    }

    [Fact]
    public void ValidationSummary_RequiresWyeFamily_ForFortyFiveDegreeBranch()
    {
        // 45° 분기는 Y형으로 분류되고, 패밀리를 고르지 않았으면 그 사유로 막힌다(docs/187 § 3-5).
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        Draw(vm, new(-1000, 0), new(1000, 0));
        Draw(vm, new(0, 0), new(1000, 1000));

        Assert.DoesNotContain("직교 T가 아닌 3방향 분기", vm.ValidationSummary);
        Assert.Contains("Y형관 패밀리 선택 필요", vm.ValidationSummary);
    }

    [Fact]
    public void CreateModelCommand_UsesSelectedSegmentFamilies_AndWaitsForCompletion()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        Draw(vm, new(0, 0), new(1000, 0));
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(PipeOutputMode.PipeAccessorySegment, request!.OutputMode);
        Assert.Equal("", request.LevelName);

        // 패밀리·파라미터는 스텁 목록 이름에서 자동 추정된다.
        Assert.NotNull(request.SegmentFamilies);
        Assert.Equal("직관 : DN100", request.SegmentFamilies!.SegmentFamilyTypeName);
        Assert.Equal("길이", request.SegmentFamilies.LengthParameterName);

        Assert.True(vm.IsCreating);
        Assert.False(vm.CreateModelCommand.CanExecute(null));

        vm.ApplyModelCreationResult(true, "완료");
        Assert.False(vm.IsCreating);
        Assert.Contains("완료", vm.Status);
    }

    [Fact]
    public void CreateModelCommand_UsesSingleBendFamily_ForNonStandardElbowAngle()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.UseAngleSnap = false;
        Draw(vm, new(0, 0), new(1000, 0));
        Draw(vm, new(1000, 0), new(1866, 500));
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        Assert.NotNull(request);
        Assert.DoesNotContain("90°·45°가 아닌", vm.ValidationSummary);
        Assert.Equal("곡관90 : DN100", request!.SegmentFamilies!.BendFamilyTypeName);
    }

    [Fact]
    public void Segment_family_selection_is_auto_guessed_from_the_fitting_list()
    {
        var vm = CreateViewModel();

        Assert.Equal("직관 : DN100", vm.SegmentFamilyTypeName);
        Assert.Equal("곡관90 : DN100", vm.BendFamilyTypeName);
        Assert.Equal("T형 : DN100", vm.TeeFamilyTypeName);
        Assert.Equal("길이", vm.LengthParameterName);
        Assert.Equal("DN", vm.DiameterParameterName);
    }

    [Fact]
    public void Segment_length_parameter_prefers_exact_straight_length_name()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            segmentParameterNames: ["볼트내밈길이", "직관_길이"]));

        Assert.Equal("직관_길이", vm.LengthParameterName);
    }

    [Fact]
    public void Segment_length_parameter_fallback_skips_bolt_names()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            segmentParameterNames: ["볼트내밈길이", "관길이"]));

        Assert.Equal("관길이", vm.LengthParameterName);
    }

    [Fact]
    public void Segment_length_parameter_is_null_when_only_bolt_names()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            segmentParameterNames: ["볼트내밈_길이"]));

        Assert.Null(vm.LengthParameterName);
    }

    [Fact]
    public void Saved_bolt_length_parameter_is_ignored_and_guess_kept()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            segmentParameterNames: ["볼트내밈_길이", "직관_길이"]));

        vm.ApplyFamilySettings(PipeLayoutFamilySettings.Empty with { LengthParameterName = "볼트내밈_길이" }, fromStorage: true);

        Assert.Equal("직관_길이", vm.LengthParameterName);
    }

    [Fact]
    public void User_can_still_pick_bolt_length_parameter_directly()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            segmentParameterNames: ["볼트내밈_길이", "직관_길이"]));

        vm.LengthParameterName = "볼트내밈_길이";

        Assert.Equal("볼트내밈_길이", vm.LengthParameterName);
    }

    [Fact]
    public void Bend_family_default_prefers_DH_prefix_over_plain_socket_bend()
    {
        // docs/145 § 1 — 키워드를 나눠 호출하던 때는 'DH' 없는 소켓곡관이 'DH_곡관'을 이겼다.
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            fittingNames: ["직관 : DN100", "소켓곡관 : KP 메카니컬 A형", "DH_곡관 : 90도", "T형 : DN100"]));

        Assert.Equal("DH_곡관 : 90도", vm.BendFamilyTypeName);
    }

    [Fact]
    public void Bend_family_default_prefers_socket_bend_among_DH_families()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            fittingNames: ["직관 : DN100", "DH_곡관 : 90도", "DH_소켓곡관 : KP 메카니컬 A형", "T형 : DN100"]));

        Assert.Equal("DH_소켓곡관 : KP 메카니컬 A형", vm.BendFamilyTypeName);
    }

    [Fact]
    public void DiameterParameterName_MatchesEnglishAbbreviation_WithoutSubstringFalsePositive()
    {
        // docs/145 § 2 — "ND"를 Contains로 찾으면 "Bend_길이"가 먼저 걸린다. 완전일치여야 한다.
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(["Bend_길이", "ND"]));

        Assert.Equal("ND", vm.DiameterParameterName);
    }

    /// <summary>docs/211 § I — 강관은 소켓 접합이 없어 라벨이 "용접"으로 보이고, 저장 값은 같은 BendConnection.Socket이다.</summary>
    [Fact]
    public void JointConnectionLabels_ShowWeldInsteadOfSocket_ForSteel()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(["길이", "ND"]));
        vm.JointConnectionLabel = "소켓";
        Assert.Equal(BendConnection.Socket, vm.JointConnection);
        Assert.Equal("소켓", vm.JointConnectionLabel);

        vm.IsSteelSelected = true;

        Assert.Equal(["플랜지", "용접"], vm.JointConnectionLabels);
        Assert.Equal("용접", vm.JointConnectionLabel);
        Assert.Equal(BendConnection.Socket, vm.JointConnection);

        vm.JointConnectionLabel = "플랜지";
        Assert.Equal(BendConnection.Flanged, vm.JointConnection);
        vm.JointConnectionLabel = "용접";
        Assert.Equal(BendConnection.Socket, vm.JointConnection);

        vm.IsDuctileIronSelected = true;
        Assert.Equal("소켓", vm.JointConnectionLabel);
    }

    [Fact]
    public void DiameterParameterName_MatchesNominalDiameterInEnglish()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(["길이", "Nominal Diameter"]));

        Assert.Equal("Nominal Diameter", vm.DiameterParameterName);
    }

    [Fact]
    public void DiameterParameterName_IsNull_WhenNoDiameterLikeParameterExists()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(["길이"]));

        Assert.Null(vm.DiameterParameterName);
    }

    [Fact]
    public void CreateModelCommand_PassesSelectedDiameterParameter()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.DiameterParameterName = "DN";
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        Assert.Equal("DN", request!.SegmentFamilies!.DiameterParameterName);
    }

    [Fact]
    public void FittingDiameterParameters_RefreshAndGuessForSelectedFamilies()
    {
        var parameterNames = new Dictionary<string, IEnumerable<string>>
        {
            ["곡관90 : DN100"] = ["곡관 직경"],
            ["곡관45 : DN100"] = ["곡관 지름"],
            ["T형 : DN100"] = ["호칭지름"],
            ["버터플라이 : DN100"] = ["Valve Diameter"]
        };
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            accessoryNames: ["밸브 : DN100", "버터플라이 : DN100"], parameterNamesByFamily: parameterNames));

        Assert.Equal(["곡관 직경"], vm.BendParameterNames);
        Assert.Equal("곡관 직경", vm.BendDiameterParameterName);
        Assert.Equal(["호칭지름"], vm.TeeParameterNames);
        Assert.Equal("호칭지름", vm.TeeDiameterParameterName);

        vm.BendFamilyTypeName = "곡관45 : DN100";
        vm.SelectedValveFamilyName = "버터플라이";
        vm.SelectedFamilyTypeName = "버터플라이 : DN100";

        Assert.Equal(["곡관 지름"], vm.BendParameterNames);
        Assert.Equal("곡관 지름", vm.BendDiameterParameterName);
        Assert.Equal(["Valve Diameter"], vm.ValveParameterNames);
        Assert.Equal("Valve Diameter", vm.ValveDiameterParameterName);
    }

    [Fact]
    public void DiameterParameterGuess_DeprioritizesRadius_AndTeePrefersMainDiameter()
    {
        var parameterNames = new Dictionary<string, IEnumerable<string>>
        {
            ["직관 : DN100"] = ["반지름", "직경", "길이"],
            ["곡관90 : DN100"] = ["반지름", "지름"],
            ["T형 : DN100"] = ["수평분기_구경", "지관_구경", "반지름", "주관_구경", "직경"],
            ["밸브 : DN100"] = ["반지름", "DN"]
        };
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            accessoryNames: ["밸브 : DN100"], parameterNamesByFamily: parameterNames));

        Assert.Equal("직경", vm.DiameterParameterName);
        Assert.Contains("반지름", vm.LengthParameterNames);
        Assert.Equal(["반지름", "지름"], vm.BendParameterNames);
        Assert.Equal(["수평분기_구경", "지관_구경", "반지름", "주관_구경", "직경"], vm.TeeParameterNames);
        Assert.Equal("주관_구경", vm.TeeDiameterParameterName);
        Assert.Equal("수평분기_구경", vm.TeeBranchDiameterParameterName);

        vm.SelectedValveFamilyName = "밸브";
        vm.SelectedFamilyTypeName = "밸브 : DN100";

        Assert.Equal(["반지름", "DN"], vm.ValveParameterNames);
        Assert.Equal("DN", vm.ValveDiameterParameterName);
    }

    [Fact]
    public void DiameterParameterGuess_StaysEmptyWhenOnlyRadiusCandidateExists()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            parameterNamesByFamily: new Dictionary<string, IEnumerable<string>>
            {
                ["직관 : DN100"] = ["반지름"]
            }));

        // 반지름은 자동선택하지 않는다(docs/133). 다만 목록에는 그대로 노출해 사용자가 직접 고를 수 있어야 한다.
        Assert.Null(vm.DiameterParameterName);
        Assert.Contains("반지름", vm.LengthParameterNames);
    }

    [Fact]
    public void BendDiameterParameterGuess_PrefersDiameterNameOverRadiusName()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            parameterNamesByFamily: new Dictionary<string, IEnumerable<string>>
            {
                ["곡관90 : DN100"] = ["곡관_구경", "곡관_구경반지름", "곡관_각도"]
            }));

        Assert.Equal("곡관_구경", vm.BendDiameterParameterName);
        Assert.Contains("곡관_구경반지름", vm.BendParameterNames);
    }

    [Fact]
    public void DiameterParameterGuess_PrefersNominalNameOverProjectSharedParameter()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            parameterNamesByFamily: new Dictionary<string, IEnumerable<string>>
            {
                ["직관 : DN100"] = ["O0201 - 관경", "공칭_지름", "길이"]
            }));

        // 프로젝트가 카테고리에 바인딩한 "O0201 - 관경"이 아니라 패밀리의 공칭 이름을 고른다(docs/138 § A).
        Assert.Equal("공칭_지름", vm.DiameterParameterName);
    }

    [Fact]
    public void ValveDiameterParameterGuess_PrefersNominalNameOverGasketParameter()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            accessoryNames: ["밸브A : DN100"],
            parameterNamesByFamily: new Dictionary<string, IEnumerable<string>>
            {
                ["밸브A : DN100"] = ["가스켓_구경", "공칭_구경"]
            }));

        // 이름 정렬상 먼저 오는 "가스켓_구경"이 아니라 "공칭_구경"이어야 한다(docs/138 § A).
        Assert.Equal("공칭_구경", vm.ValveDiameterParameterName);
    }

    [Fact]
    public void BendAngleParameterGuess_SkipsProjectAllowanceParameter()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            parameterNamesByFamily: new Dictionary<string, IEnumerable<string>>
            {
                ["곡관90 : DN100"] = ["곡관_각도", "허용굴곡각도", "공칭_지름", "곡관_구경반지름"]
            }));

        // "허용굴곡각도"는 프로젝트 판정용 파라미터라 곡관 각도로 잡히면 안 된다(docs/138 § B).
        Assert.Equal("곡관_각도", vm.BendAngleParameterName);
        Assert.Equal("공칭_지름", vm.BendDiameterParameterName);
    }

    [Fact]
    public void TeeDiameterParameterGuess_StaysEmptyWhenOnlyRadiusCandidatesExist()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            parameterNamesByFamily: new Dictionary<string, IEnumerable<string>>
            {
                ["T형 : DN100"] = ["주관_구경반지름", "수평분기_구경반지름"]
            }));

        Assert.Null(vm.TeeDiameterParameterName);
        Assert.Null(vm.TeeBranchDiameterParameterName);
    }

    [Fact]
    public void TeeBranchDiameterParameter_StaysEmptyWhenNoBranchCandidateExists()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            parameterNamesByFamily: new Dictionary<string, IEnumerable<string>>
            {
                ["T형 : DN100"] = ["공칭지름"]
            }));

        Assert.Equal("공칭지름", vm.TeeDiameterParameterName);
        Assert.Null(vm.TeeBranchDiameterParameterName);
    }

    [Fact]
    public void ValveFamilySelection_RefreshesDiameterParametersFromFirstType()
    {
        var vm = CreateParameterSelectionViewModel();

        Assert.Equal(["자동 DN", "사용자 DN"], vm.ValveParameterNames);
        Assert.Equal("자동 DN", vm.ValveDiameterParameterName);
        Assert.Null(vm.SelectedFamilyTypeName);
    }

    [Fact]
    public void ValveDiameterParameter_KeepsUserSelectionWhenPlacementTypeChanges()
    {
        var vm = CreateParameterSelectionViewModel();
        vm.ValveDiameterParameterName = "사용자 DN";

        vm.SelectedFamilyTypeName = "밸브A : DN150";

        Assert.Equal("사용자 DN", vm.ValveDiameterParameterName);
    }

    [Fact]
    public void ValveDiameterParameter_ReGuessesWhenNewFamilyDoesNotContainUserSelection()
    {
        var vm = CreateParameterSelectionViewModel();
        vm.ValveDiameterParameterName = "사용자 DN";

        vm.SelectedValveFamilyName = "밸브B";

        Assert.Equal(["대체 DN"], vm.ValveParameterNames);
        Assert.Equal("대체 DN", vm.ValveDiameterParameterName);
    }

    [Fact]
    public void ValveDiameterParameter_KeepsPerFamilySelectionWhenFamilyChangesBack()
    {
        var vm = CreateParameterSelectionViewModel();
        vm.ValveDiameterParameterName = "사용자 DN";

        vm.SelectedValveFamilyName = "밸브B";
        Assert.Equal("대체 DN", vm.ValveDiameterParameterName);

        // 패밀리를 오가도 각자 고른 값이 살아 있어야 한다(docs/135 § B).
        vm.SelectedValveFamilyName = "밸브A";
        Assert.Equal("사용자 DN", vm.ValveDiameterParameterName);

        vm.SelectedValveFamilyName = "밸브B";
        Assert.Equal("대체 DN", vm.ValveDiameterParameterName);
    }

    [Fact]
    public void CreateModelCommand_PassesValveDiameterParametersPerFamily()
    {
        var vm = CreateParameterSelectionViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.ValveDiameterParameterName = "사용자 DN";   // 밸브A
        vm.SelectedValveFamilyName = "밸브B";          // 밸브B는 추정값 "대체 DN"
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        var byFamily = request!.SegmentFamilies!.ValveDiameterParameterNamesByFamily;
        Assert.NotNull(byFamily);
        Assert.Equal("사용자 DN", byFamily!["밸브A"]);
        Assert.Equal("대체 DN", byFamily["밸브B"]);
    }

    [Fact]
    public void BendAndTeeParameters_KeepValidUserSelectionsWhenFamilyChanges()
    {
        var parameterNames = new Dictionary<string, IEnumerable<string>>
        {
            ["곡관A : DN100"] = ["자동 DN", "사용자 DN", "각도", "사용자 각도"],
            ["곡관B : DN100"] = ["자동 DN", "사용자 DN", "각도", "사용자 각도"],
            ["T형A : DN100"] = ["주관_구경", "사용자 DN", "사용자 분기 DN"],
            ["T형B : DN100"] = ["주관_구경", "사용자 DN", "사용자 분기 DN"]
        };
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            fittingNames: ["직관 : DN100", "곡관A : DN100", "곡관B : DN100", "T형A : DN100", "T형B : DN100"],
            parameterNamesByFamily: parameterNames));
        vm.BendDiameterParameterName = "사용자 DN";
        vm.BendAngleParameterName = "사용자 각도";
        vm.TeeDiameterParameterName = "사용자 DN";
        vm.TeeBranchDiameterParameterName = "사용자 분기 DN";

        vm.BendFamilyTypeName = "곡관B : DN100";
        vm.TeeFamilyTypeName = "T형B : DN100";

        Assert.Equal("사용자 DN", vm.BendDiameterParameterName);
        Assert.Equal("사용자 각도", vm.BendAngleParameterName);
        Assert.Equal("사용자 DN", vm.TeeDiameterParameterName);
        Assert.Equal("사용자 분기 DN", vm.TeeBranchDiameterParameterName);
    }

    [Fact]
    public void FittingParameters_KeepUserSelectionWhenCollectionResetPushesNull()
    {
        var parameterNames = new Dictionary<string, IEnumerable<string>>
        {
            ["곡관A : DN100"] = ["자동 DN", "사용자 DN", "각도", "사용자 각도"],
            ["곡관B : DN100"] = ["자동 DN", "사용자 DN", "각도", "사용자 각도"],
            ["T형A : DN100"] = ["주관_구경", "사용자 DN"],
            ["T형B : DN100"] = ["주관_구경", "사용자 DN"],
            ["밸브A : DN100"] = ["자동 DN", "사용자 DN"],
            ["밸브A : DN150"] = ["자동 DN", "사용자 DN"]
        };
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            accessoryNames: ["밸브A : DN100", "밸브A : DN150"],
            fittingNames: ["직관 : DN100", "곡관A : DN100", "곡관B : DN100", "T형A : DN100", "T형B : DN100"],
            parameterNamesByFamily: parameterNames));
        vm.BendParameterNames.CollectionChanged += (_, args) =>
        {
            if (args.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset) return;
            vm.BendDiameterParameterName = null;
            vm.BendAngleParameterName = null;
        };
        vm.TeeParameterNames.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                 vm.TeeDiameterParameterName = null;
        };
        vm.TeeBranchParameterNames.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                vm.TeeBranchDiameterParameterName = null;
        };
        vm.ValveParameterNames.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                vm.ValveDiameterParameterName = null;
        };
        vm.BendDiameterParameterName = "사용자 DN";
        vm.BendAngleParameterName = "사용자 각도";
        vm.TeeDiameterParameterName = "사용자 DN";
        vm.TeeBranchDiameterParameterName = "사용자 DN";
        vm.ValveDiameterParameterName = "사용자 DN";

        vm.BendFamilyTypeName = "곡관B : DN100";
        vm.TeeFamilyTypeName = "T형B : DN100";
        vm.SelectedFamilyTypeName = "밸브A : DN150";

        Assert.Equal("사용자 DN", vm.BendDiameterParameterName);
        Assert.Equal("사용자 각도", vm.BendAngleParameterName);
        Assert.Equal("사용자 DN", vm.TeeDiameterParameterName);
        Assert.Equal("사용자 DN", vm.TeeBranchDiameterParameterName);
        Assert.Equal("사용자 DN", vm.ValveDiameterParameterName);
    }

    [Fact]
    public void FamilyAndParameterDefaults_PreferDhPrefixAndConfiguredKeywords()
    {
        var parameterNames = new Dictionary<string, IEnumerable<string>>
        {
            ["DH 직관 : DN100"] = ["직경", "공칭지름", "길이"],
            ["DH 곡관 : DN100"] = ["지름", "각도", "공칭구경"],
            ["DH T형관 : DN100"] = ["호칭", "공칭구경"],
            ["DH Y형관 : DN100"] = ["호칭", "공칭구경"],
        };
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            fittingNames: ["직관 : DN100", "DH 직관 : DN100", "곡관 : DN100", "DH 곡관 : DN100", "T형관 : DN100", "DH T형관 : DN100", "DH Y형관 : DN100"],
            parameterNamesByFamily: parameterNames));

        Assert.Equal("DH 직관 : DN100", vm.SegmentFamilyTypeName);
        Assert.Equal("DH 곡관 : DN100", vm.BendFamilyTypeName);
        // Y형관은 docs/187부터 T형과 다른 칸이다 — 더는 T형 추정 키워드에 들어가지 않는다.
        Assert.Equal("DH T형관 : DN100", vm.TeeFamilyTypeName);
        Assert.Equal("DH Y형관 : DN100", vm.WyeFamilyTypeName);
        Assert.Equal("공칭지름", vm.DiameterParameterName);
        Assert.Equal("공칭구경", vm.BendDiameterParameterName);
        Assert.Equal("각도", vm.BendAngleParameterName);
        Assert.Equal("공칭구경", vm.TeeDiameterParameterName);
    }

    [Fact]
    public void CreateModelCommand_PassesFittingDiameterParameters()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.BendDiameterParameterName = "곡관 직경";
        vm.TeeDiameterParameterName = "T형 직경";
        vm.TeeBranchDiameterParameterName = "T형 분기 직경";
        vm.ValveDiameterParameterName = "밸브 직경";
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        Assert.Equal("곡관 직경", request!.SegmentFamilies!.BendDiameterParameterName);
        Assert.Equal("T형 직경", request.SegmentFamilies.TeeDiameterParameterName);
        Assert.Equal("T형 분기 직경", request.SegmentFamilies.TeeBranchDiameterParameterName);
        Assert.Equal("밸브 직경", request.SegmentFamilies.ValveDiameterParameterName);
    }

    [Fact]
    public void MissingDiameterParameter_DoesNotBlockModelCreation()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.CreateModelAction = _ => { };

        vm.DiameterParameterName = null;

        Assert.True(vm.CreateModelCommand.CanExecute(null));
        Assert.DoesNotContain("직경 파라미터", vm.ValidationSummary);
    }

    [Fact]
    public void Accessory_and_segment_family_lists_are_separated()
    {
        var vm = CreateViewModel();

        Assert.Equal(["밸브"], vm.ValveFamilyNames.Select(x => x.Name));
        Assert.Equal(["밸브 : DN100"], vm.FamilyTypeNames);
        // 저장소 순서가 아니라 패밀리명 정렬이다(docs/192 § D). 호칭지름이 모두 같아 이름순으로만 갈린다.
        Assert.Equal(
            ["T형 : DN100", "곡관45 : DN100", "곡관90 : DN100", "단관 : DN100", "직관 : DN100"],
            vm.SegmentFamilyTypeNames);
        Assert.Contains(vm.SegmentFamilyTypeName, vm.SegmentFamilyTypeNames);
        Assert.Contains(vm.BendFamilyTypeName, vm.SegmentFamilyTypeNames);
        Assert.Contains(vm.TeeFamilyTypeName, vm.SegmentFamilyTypeNames);
    }

    [Fact]
    public void ValveFamilySelection_FiltersTypes_AndKeepsFullPlacementKey()
    {
        var vm = CreateViewModel(["게이트밸브 300A : Z형", "게이트밸브 300A : A형", "버터플라이밸브 300A : 표준"]);

        vm.SelectedValveFamilyName = "게이트밸브 300A";

        Assert.Equal(["게이트밸브 300A : A형", "게이트밸브 300A : Z형"], vm.FamilyTypeNames);
        Assert.Null(vm.SelectedFamilyTypeName);
    }

    /// <summary>유형 목록은 호칭지름 숫자로 정렬한다 — 문자열 정렬이면 65A가 200A 아래로 간다(docs/192).</summary>
    [Fact]
    public void ValveTypeNames_SortByNominalDiameter_NotByText()
    {
        var vm = CreateViewModel([
            "게이트밸브 : 200A", "게이트밸브 : 65A", "게이트밸브 : 300A", "게이트밸브 : 80A", "게이트밸브 : 100A"]);

        vm.SelectedValveFamilyName = "게이트밸브";

        Assert.Equal([
            "게이트밸브 : 65A", "게이트밸브 : 80A", "게이트밸브 : 100A", "게이트밸브 : 200A", "게이트밸브 : 300A"],
            vm.FamilyTypeNames);
    }

    /// <summary>관·곡관·T형 등이 공유하는 목록은 패밀리로 묶고 그 안에서 호칭지름 순이다(docs/192 § D).
    /// 호칭지름만으로 줄 세우면 같은 패밀리가 흩어진다.</summary>
    [Fact]
    public void SegmentFamilyTypeNames_GroupByFamily_ThenSortByNominalDiameter()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(fittingNames: [
            "직관 : 200A", "곡관 : 65A", "직관 : 65A", "곡관 : 200A", "직관 : 100A"]));

        Assert.Equal([
            "곡관 : 65A", "곡관 : 200A", "직관 : 65A", "직관 : 100A", "직관 : 200A"],
            vm.SegmentFamilyTypeNames);
    }

    /// <summary>이음관 목록도 같은 규칙으로 정렬한다(docs/192 § D).</summary>
    [Fact]
    public void JointFamilyTypeNames_SortByFamilyThenNominalDiameter()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(fittingNames: [
            "직관 : 100A", "신축이음관 : 300A", "신축이음관 : 65A", "신축이음관 : 100A"]));

        Assert.Equal(["신축이음관 : 65A", "신축이음관 : 100A", "신축이음관 : 300A"], vm.JointFamilyTypeNames);
    }

    /// <summary>패밀리 콤보도 같은 규칙이다 — 유형만 고치면 그 위 콤보가 여전히 어긋나 더 헷갈린다(docs/192 § B).</summary>
    [Fact]
    public void ValveFamilyNames_SortByNominalDiameter_NotByText()
    {
        var vm = CreateViewModel([
            "게이트밸브 200A : 표준", "게이트밸브 65A : 표준", "게이트밸브 100A : 표준"]);

        Assert.Equal(["게이트밸브 65A", "게이트밸브 100A", "게이트밸브 200A"],
            vm.ValveFamilyNames.Select(x => x.Name));
    }

    [Fact]
    public void BendFamily_DefaultsToSocketElbowBeforeGenericElbow()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            fittingNames: ["일반곡관 : DN100", "소켓곡관 : DN100", "직관 : DN100", "T형 : DN100"]));

        Assert.Equal("소켓곡관 : DN100", vm.BendFamilyTypeName);
    }

    [Fact]
    public void Missing_length_parameter_blocks_model_creation()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        Draw(vm, new(0, 0), new(1000, 0));
        vm.CreateModelAction = _ => { };
        Assert.True(vm.CreateModelCommand.CanExecute(null));

        vm.LengthParameterName = null;

        Assert.False(vm.CreateModelCommand.CanExecute(null));
        Assert.Contains("관 길이 파라미터", vm.ValidationSummary);
    }

    [Fact]
    public void CreateModelCommand_PassesCustomStraightLength()
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        Draw(vm, new(0, 0), new(1000, 0));
        vm.StraightLengthMm = 500;
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(500, request!.SegmentFamilies!.StraightLengthMm);
    }

    [Fact]
    public void ValidationSummary_RequiresSegmentFamilyAndLengthParameter()
    {
        var vm = CreateViewModel();

        vm.SegmentFamilyTypeName = null;
        Assert.Contains("관 패밀리 선택 필요", vm.ValidationSummary);

        vm.SegmentFamilyTypeName = "직관 : DN100";
        vm.LengthParameterName = null;
        Assert.Contains("관 길이 파라미터 선택 필요", vm.ValidationSummary);
    }

    [Fact]
    public void ModelCreationFailure_NearNode_HighlightsAllIncidentEdges()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        Draw(vm, new Point2D(1000, 0), new Point2D(1000, 1000));

        vm.ApplyModelCreationResult(false, "연결 실패", [
            new PipeConnectionFailure(new Point2D(1050, 0), 600, "곡관", "곡관 : DN100", 1, "테스트")]);

        Assert.All(vm.Edges, edge => Assert.True(edge.IsError));
        Assert.All(vm.Edges, edge => Assert.Equal("#E74C3C", edge.Stroke));
    }

    [Fact]
    public void ModelCreationFailure_AwayFromNode_HighlightsNearestEdge()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        Draw(vm, new Point2D(0, 1000), new Point2D(1000, 1000));

        vm.ApplyModelCreationResult(false, "연결 실패", [
            new PipeConnectionFailure(new Point2D(500, 650), 600, "단관", "직관 : DN100", 1, "테스트")]);

        var highlighted = Assert.Single(vm.Edges.Where(edge => edge.IsError));
        Assert.Equal(1000, vm.Transform.ToModel(new Point(highlighted.LengthX, highlighted.LengthY)).Y, 6);
    }

    [Fact]
    public void ModelCreationFailure_BypassSetHighlightsOnlySet()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(4000, 0));
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        var fittingId = Assert.Single(vm.BypassSetItems).Id;

        vm.ApplyModelCreationResult(false, "연결 실패", [
            new PipeConnectionFailure(new Point2D(2000, 0), 600, BypassSet.TypeKey, "DH_게이트 밸브", 0, "테스트")
            { FittingId = fittingId }]);

        Assert.All(vm.Edges, edge => Assert.False(edge.IsError));
        var fitting = Assert.Single(vm.BypassSetItems);
        Assert.True(fitting.IsError);
        Assert.Equal("#E74C3C", fitting.Fill);
    }

    // ── 선별 관경 · 선택 편집 · 드래그 이동 (docs/152 § E-7) ─────────────────────

    [Fact]
    public void DiameterMm_ChangedBeforeDrawing_AppliesOnlyToLaterEdges()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.DiameterMm = 100;
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.DiameterMm = 200;
        Draw(vm, new Point2D(0, 1000), new Point2D(1000, 1000));

        Assert.Equal(100, EdgeAtModelY(vm, 0).DiameterMm);
        Assert.Equal(200, EdgeAtModelY(vm, 1000).DiameterMm);
    }

    [Fact]
    public void DiameterMm_WithSelection_EditsSelectedEdgeOnly()
    {
        var vm = TwoParallelEdges();
        var target = EdgeAtModelY(vm, 0);
        vm.Mode = PipeLayoutMode.Selection;
        vm.SelectEdge(target.Id);

        vm.DiameterMm = 500;

        Assert.Equal(500, vm.Edges.Single(x => x.Id == target.Id).DiameterMm);
        Assert.Equal(100, vm.Edges.Single(x => x.Id != target.Id).DiameterMm);
    }

    [Fact]
    public void DiameterMm_WithSelection_DoesNotChangeDefaultForNextEdge()
    {
        var vm = TwoParallelEdges();
        vm.DiameterMm = 300;
        vm.Mode = PipeLayoutMode.Selection;
        vm.SelectEdge(vm.Edges[0].Id);
        vm.DiameterMm = 500;

        vm.Mode = PipeLayoutMode.Drawing;

        // 선택이 풀리면 입력칸은 다시 기본 관경(300)을 가리키고, 새 선도 300으로 그려진다.
        Assert.Equal(300, vm.DiameterMm);
        Draw(vm, new Point2D(0, 2000), new Point2D(1000, 2000));
        Assert.Equal(300, EdgeAtModelY(vm, 2000).DiameterMm);
    }

    [Fact]
    public void DiameterCaption_SwitchesWithSelection()
    {
        var vm = TwoParallelEdges();

        Assert.Equal("기본 관경", vm.DiameterCaption);

        vm.Mode = PipeLayoutMode.Selection;
        vm.SelectEdge(vm.Edges[0].Id);

        Assert.Equal("선택 관경 (1)", vm.DiameterCaption);
    }

    [Fact]
    public void DiameterEdit_IsUndoable()
    {
        var vm = TwoParallelEdges();
        var target = vm.Edges[0];
        vm.Mode = PipeLayoutMode.Selection;
        vm.SelectEdge(target.Id);
        vm.DiameterMm = 300;

        vm.UndoCommand.Execute(null);

        Assert.All(vm.Edges, x => Assert.Equal(100, x.DiameterMm));
    }

    [Fact]
    public void EdgeDrag_MovesOnlySelectedEdge()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        Draw(vm, new Point2D(1000, 0), new Point2D(1000, 1000));
        vm.Mode = PipeLayoutMode.Selection;
        var horizontal = vm.Edges.Single(x => Math.Abs(x.Y1 - x.Y2) < 0.001);
        var verticalId = vm.Edges.Single(x => x.Id != horizontal.Id).Id;

        DragBy(vm, vm.TryBeginEdgeDrag, horizontal.Id, dxMm: 0, dyMm: -500);

        // 세로선은 제자리, 가로선만 내려간다(공유 절점 분리).
        var vertical = vm.Edges.Single(x => x.Id == verticalId);
        AssertPoint(new Point2D(1000, 0), vm.Transform.ToModel(new Point(vertical.X1, vertical.Y1)));
        Assert.All(vm.Edges.Where(x => x.Id != verticalId),
            x => Assert.Equal(-500, vm.Transform.ToModel(new Point(x.X1, x.Y1)).Y, 6));
    }

    [Fact]
    public void EdgeDrag_BelowThreshold_SelectsInsteadOfMoving()
    {
        var vm = TwoParallelEdges();
        vm.Mode = PipeLayoutMode.Selection;
        var edge = vm.Edges[0];
        var grab = new Point(edge.X1, edge.Y1);

        Assert.True(vm.TryBeginEdgeDrag(grab, edge.Id));
        vm.UpdateDrag(new Point(grab.X + 2, grab.Y));
        vm.CommitDrag(new Point(grab.X + 2, grab.Y));

        Assert.False(vm.IsDraggingCandidate);
        Assert.Equal([edge.Id], vm.SelectedEdgeIds);
        AssertPoint(vm.Transform.ToModel(grab), vm.Transform.ToModel(new Point(vm.Edges[0].X1, vm.Edges[0].Y1)));
    }

    [Fact]
    public void NodeDrag_MovesBothIncidentEdges()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        Draw(vm, new Point2D(1000, 0), new Point2D(1000, 1000));
        vm.Mode = PipeLayoutMode.Selection;
        var shared = vm.Nodes.Single(x => x.Degree == 2);

        DragBy(vm, vm.TryBeginNodeDrag, shared.Id, dxMm: 500, dyMm: 0);

        // 절점 그립은 분리하지 않는다 — 두 엣지가 모두 새 좌표를 공유한다.
        Assert.Equal(3, vm.Nodes.Count);
        Assert.Contains(vm.Nodes, x => Math.Abs(vm.Transform.ToModel(new Point(x.X, x.Y)).X - 1500) < 1e-6);
        Assert.DoesNotContain(vm.Nodes, x => x.Degree == 2
            && Math.Abs(vm.Transform.ToModel(new Point(x.X, x.Y)).X - 1000) < 1e-6);
    }

    [Fact]
    public void FittingDrag_ChangesPositionOnSameEdge()
    {
        var vm = CreateDrawingViewModel(["밸브 : DN100"]);
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(2000, 0));
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(400, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(400, 0)));
        vm.SelectedFamilyTypeName = null;
        vm.Mode = PipeLayoutMode.Selection;
        var fitting = Assert.Single(vm.InlineFittings);
        var edgeId = fitting.EdgeId;

        var grab = new Point(fitting.X, fitting.Y);
        Assert.True(vm.TryBeginFittingDrag(grab, fitting.Id));
        var target = vm.Transform.ToScreen(new Point2D(1500, 0));
        vm.UpdateDrag(target);
        vm.CommitDrag(target);

        var moved = Assert.Single(vm.InlineFittings);
        Assert.Equal(edgeId, moved.EdgeId);
        Assert.Equal(0.75, moved.T, 3);
    }

    /// <summary>모델 Y가 <paramref name="modelY"/>인 수평 엣지. 화면 Y는 아래로 증가하므로 화면 좌표로 정렬하면 안 된다.</summary>
    private static PipeEdgeItem EdgeAtModelY(PipeLayoutViewModel vm, double modelY)
        => vm.Edges.Single(x => Math.Abs(vm.Transform.ToModel(new Point(x.X1, x.Y1)).Y - modelY) < 1e-6);

    /// <summary>관경 100짜리 수평선 두 개. 선택 편집 검증의 공통 기반이다.</summary>
    private static PipeLayoutViewModel TwoParallelEdges()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.DiameterMm = 100;
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        Draw(vm, new Point2D(0, 1000), new Point2D(1000, 1000));
        return vm;
    }

    /// <summary>임계값을 넘긴 드래그 한 번을 모델 좌표 이동량으로 수행한다.</summary>
    private static void DragBy(PipeLayoutViewModel vm, Func<Point, Guid, bool> begin, Guid targetId, double dxMm, double dyMm)
    {
        var origin = vm.Transform.ToScreen(new Point2D(0, 0));
        var moved = vm.Transform.ToScreen(new Point2D(dxMm, dyMm));
        Assert.True(begin(origin, targetId));
        vm.UpdateDrag(moved);
        vm.CommitDrag(moved);
    }

    // ── Revit 부재 선택·표시 + 선 분할(docs/179) ────────────────────────────────

    [Fact]
    public void SelectInRevit_Disabled_WhenNoMappingExists()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.SelectInRevitAction = _ => { };
        SelectSingleEdge(vm);

        Assert.False(vm.SelectInRevitCommand.CanExecute(null));
    }

    [Fact]
    public void SelectInRevit_SendsEveryElementIdOfSelectedEdge()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        var edgeId = vm.Edges.Single().Id;
        // 엣지 1개가 정척 분할로 관 3본이 된 상황이다(1:N).
        ApplyMapping(vm, new() { [edgeId] = new long[] { 11, 22, 33 } });
        IReadOnlyList<long>? sent = null;
        vm.SelectInRevitAction = ids => sent = ids;
        SelectSingleEdge(vm);

        Assert.True(vm.SelectInRevitCommand.CanExecute(null));
        vm.SelectInRevitCommand.Execute(null);

        Assert.Equal([11L, 22L, 33L], sent);
    }

    [Fact]
    public void FailedModelCreation_KeepsPreviousMapping()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        var edgeId = vm.Edges.Single().Id;
        ApplyMapping(vm, new() { [edgeId] = new long[] { 11 } });

        // 실패는 트랜잭션이 통째로 롤백된 것이다. 모델이 안 바뀌었으므로 매핑도 그대로여야 한다.
        vm.ApplyModelCreationResult(false, "실패", [], new Dictionary<Guid, IReadOnlyList<long>>());

        IReadOnlyList<long>? sent = null;
        vm.SelectInRevitAction = ids => sent = ids;
        SelectSingleEdge(vm);
        vm.SelectInRevitCommand.Execute(null);

        Assert.Equal([11L], sent);
    }

    [Fact]
    public void MovingEdge_KeepsMapping()
    {
        // docs/179 § 0 — 선을 옮겨도 Revit 부재는 그대로다. 매핑을 무효화하지 않는 것이 확정 동작이다.
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        var edgeId = vm.Edges.Single().Id;
        ApplyMapping(vm, new() { [edgeId] = new long[] { 77 } });
        SelectSingleEdge(vm);

        // 선을 위로 끌어 옮긴다. Guid는 유지되고 좌표만 바뀐다.
        var edge = vm.Edges.Single();
        var grab = new Point(edge.X1, edge.Y1);
        Assert.True(vm.TryBeginEdgeDrag(grab, edge.Id));
        var moved = new Point(grab.X, grab.Y - 200);
        vm.UpdateDrag(moved);
        vm.CommitDrag(moved);

        IReadOnlyList<long>? sent = null;
        vm.SelectInRevitAction = ids => sent = ids;
        SelectSingleEdge(vm);
        vm.SelectInRevitCommand.Execute(null);

        Assert.Equal([77L], sent);
    }

    [Fact]
    public void SplitEdge_RequiresExactlyOneSelectedEdge()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));

        Assert.False(vm.SplitEdgeCommand.CanExecute(null));   // 0개 선택

        SelectSingleEdge(vm);
        Assert.True(vm.SplitEdgeCommand.CanExecute(null));
    }

    [Fact]
    public void SplitEdge_ClickingMidpoint_MakesTwoEdges()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        SelectSingleEdge(vm);

        vm.SplitEdgeCommand.Execute(null);
        Assert.True(vm.IsSplittingEdge);
        Assert.Equal("분할점 지정", vm.InteractionModeText);

        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));

        Assert.False(vm.IsSplittingEdge);
        Assert.Equal(2, vm.Edges.Count);
    }

    [Fact]
    public void SplitEdge_TooCloseToEndpoint_IsRefused()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        SelectSingleEdge(vm);
        vm.SplitEdgeCommand.Execute(null);

        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(50, 0)));

        Assert.Single(vm.Edges);
        Assert.True(vm.IsSplittingEdge);   // 지정 상태를 유지해 다시 클릭할 수 있다
        Assert.Contains("100mm 이상", vm.Status);
    }

    [Fact]
    public void SplitEdge_TooCloseToInlineFitting_IsRefused()
    {
        var vm = CreateDrawingViewModel(["게이트밸브 200A : 표준"]);
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(2000, 0));
        vm.SelectedFamilyTypeName = "게이트밸브 200A : 표준";
        // 배치는 미리보기를 커밋하는 방식이다 — Move로 미리보기를 세우지 않으면 클릭이 아무것도 놓지 않는다.
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(1000, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(1000, 0)));
        vm.SelectedFamilyTypeName = null;
        Assert.Single(vm.InlineFittings);
        SelectSingleEdge(vm);
        vm.SplitEdgeCommand.Execute(null);

        // 부속 중심(1000)에서 50mm 떨어진 자리 — 최소 이격 100mm 안쪽이다.
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(1050, 0)));

        Assert.Single(vm.Edges);
        Assert.Contains("100mm 이내", vm.Status);
    }

    [Fact]
    public void SplitEdge_IsUndoable()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        SelectSingleEdge(vm);
        vm.SplitEdgeCommand.Execute(null);
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));
        Assert.Equal(2, vm.Edges.Count);

        vm.UndoCommand.Execute(null);

        Assert.Single(vm.Edges);
    }

    [Fact]
    public void SplitEdge_Escape_CancelsWithoutChangingGraph()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        SelectSingleEdge(vm);
        vm.SplitEdgeCommand.Execute(null);

        vm.HandleEscape();

        Assert.False(vm.IsSplittingEdge);
        Assert.Single(vm.Edges);
        Assert.Contains("취소", vm.Status);
    }

    [Fact]
    public void SplitEdge_CarriesMappingToBothHalves()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        var edgeId = vm.Edges.Single().Id;
        ApplyMapping(vm, new() { [edgeId] = new long[] { 101, 102 } });
        SelectSingleEdge(vm);
        vm.SplitEdgeCommand.Execute(null);
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));

        // 어느 조각을 골라도 원본 관이 잡혀야 한다(docs/179 § H-4).
        foreach (var half in vm.Edges.ToList())
        {
            IReadOnlyList<long>? sent = null;
            vm.SelectInRevitAction = ids => sent = ids;
            SelectEdge(vm, half);
            vm.SelectInRevitCommand.Execute(null);
            Assert.Equal([101L, 102L], sent);
        }
    }

    /// <summary>생성 성공 결과로 매핑을 밀어 넣는다. 매핑을 직접 건드리는 공개 통로가 이것뿐이다.</summary>
    private static void ApplyMapping(PipeLayoutViewModel vm, Dictionary<Guid, IReadOnlyList<long>> mapping)
        => vm.ApplyModelCreationResult(true, "생성 완료", [], mapping);

    // ── 분할 표식 · 갈라진 끝점(docs/181) ──────────────────────────────────────

    /// <summary>분할 중 커서를 올리면 선에 수직한 표식이 보이고, 분할이 끝나면 사라진다(§ A).</summary>
    [Fact]
    public void SplitEdge_ShowsPerpendicularMarker_WhileSplitting()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        SelectSingleEdge(vm);
        Assert.False(vm.IsSplitMarkerVisible);

        vm.SplitEdgeCommand.Execute(null);
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(500, 0)));

        Assert.True(vm.IsSplitMarkerVisible);
        // 수평선이므로 표식은 세로여야 한다 — X가 같고 Y만 벌어진다.
        Assert.Equal(vm.SplitMarkerX1, vm.SplitMarkerX2, 6);
        Assert.True(Math.Abs(vm.SplitMarkerY2 - vm.SplitMarkerY1) > 1);

        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));
        Assert.False(vm.IsSplitMarkerVisible);
    }

    /// <summary>커서가 선에서 벗어나도 표식은 선 위 정사영 지점에 남는다(§ A-2).</summary>
    [Fact]
    public void SplitMarker_StaysOnTheEdge_WhenCursorIsOffTheLine()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        SelectSingleEdge(vm);
        vm.SplitEdgeCommand.Execute(null);

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(500, 800)));

        Assert.True(vm.IsSplitMarkerVisible);
        var center = vm.Transform.ToModel(new Point(
            (vm.SplitMarkerX1 + vm.SplitMarkerX2) / 2, (vm.SplitMarkerY1 + vm.SplitMarkerY2) / 2));
        AssertPoint(center, new Point2D(500, 0));
    }

    /// <summary>분할하면 같은 자리에 끝점 2개가 생긴다(§ B).</summary>
    [Fact]
    public void SplitEdge_LeavesTwoCoincidentEndPoints()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        SelectSingleEdge(vm);
        vm.SplitEdgeCommand.Execute(null);

        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));

        var middle = vm.Nodes.Where(x => Math.Abs(vm.Transform.ToModel(new Point(x.X, x.Y)).X - 500) < 1e-6).ToList();
        Assert.Equal(2, middle.Count);
        Assert.All(middle, x => Assert.Equal(NodeKind.EndPoint, x.NodeKind));
    }

    /// <summary>겹친 끝점 중 <b>선택된 선</b>에 물린 쪽이 잡힌다(§ B-2, 2026-09-17 사용자 확정).
    /// 끌고 나면 그 조각만 움직이고 나머지 조각은 제자리에 남는다.</summary>
    [Fact]
    public void CoincidentEndPointDrag_PicksTheSelectedEdgesNode()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        SelectSingleEdge(vm);
        vm.SplitEdgeCommand.Execute(null);
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));

        // 왼쪽 조각을 고른 뒤, 겹친 끝점을 아무거나 잡는다.
        var left = vm.Edges.Single(x => vm.Transform.ToModel(new Point(x.X1, x.Y1)).X < 1
            || vm.Transform.ToModel(new Point(x.X2, x.Y2)).X < 1);
        SelectEdge(vm, left);
        var grabbed = vm.Nodes.First(x => Math.Abs(vm.Transform.ToModel(new Point(x.X, x.Y)).X - 500) < 1e-6);
        vm.UseAngleSnap = false;   // 집기 규칙만 본다 — 끝점 각도 스냅(2026-10-29)은 끔

        Assert.True(vm.TryBeginNodeDrag(vm.Transform.ToScreen(new Point2D(500, 0)), grabbed.Id));
        var moved = vm.Transform.ToScreen(new Point2D(500, 600));
        vm.UpdateDrag(moved);
        vm.CommitDrag(moved);

        // 오른쪽 조각의 끝점은 제자리(500, 0)에 남아 있어야 한다.
        Assert.Contains(vm.Nodes, x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(500, 0)));
        Assert.Contains(vm.Nodes, x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(500, 600)));
        Assert.Equal(2, vm.Edges.Count);
        // 고른 조각(왼쪽)의 끝이 올라갔다 — 반대쪽을 집었다면 이 단언이 깨진다.
        var leftAfter = vm.Edges.Single(x => x.Id == left.Id);
        Assert.Contains(new[] { vm.Transform.ToModel(new Point(leftAfter.X1, leftAfter.Y1)), vm.Transform.ToModel(new Point(leftAfter.X2, leftAfter.Y2)) },
            p => AreClose(p, new Point2D(500, 600)));
    }

    /// <summary>Shift를 누른 채 끝점을 끌면 반대쪽 끝(여기서는 원점) 기준 45° 배수 8방향에 맞는다
    /// (docs/181 § B-3, 45° 추가는 docs/191). 수평·수직은 좌표가 곧 길이라 종전과 결과가 같다.</summary>
    [Theory]
    [InlineData(1200, 100, 1200, 0)]        // 4.8° → 수평
    [InlineData(200, 900, 0, 900)]          // 77.5° → 수직
    public void EndPointDrag_WithShift_SnapsToRightAngle(
        double cursorXMm, double cursorYMm, double expectedXMm, double expectedYMm)
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.Mode = PipeLayoutMode.Selection;
        var end = vm.Nodes.Single(x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(1000, 0)));

        Assert.True(vm.TryBeginNodeDrag(vm.Transform.ToScreen(new Point2D(1000, 0)), end.Id));
        var moved = vm.Transform.ToScreen(new Point2D(cursorXMm, cursorYMm));
        vm.UpdateDrag(moved, orthoSnap: true);
        vm.CommitDrag(moved, orthoSnap: true);

        Assert.Contains(vm.Nodes, x =>
            AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(expectedXMm, expectedYMm)));
    }

    /// <summary>대각선에서는 <b>선 길이</b>가 격자 배수여야 한다(docs/193 § A).
    /// 좌표는 <c>길이 × 0.7071</c>이라 격자를 벗어나는 것이 정상이다 — 길이가 우선이라는 사용자 확정.</summary>
    [Theory]
    [InlineData(1200, 900, 1, 1)]       // 36.9° → 45°
    [InlineData(-1000, 900, -1, 1)]     // 138.0° → 135°
    [InlineData(1000, -800, 1, -1)]     // -38.7° → -45°
    public void EndPointDrag_WithShift_SnapsDiagonalByLength(
        double cursorXMm, double cursorYMm, double signX, double signY)
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.Mode = PipeLayoutMode.Selection;
        var end = vm.Nodes.Single(x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(1000, 0)));

        Assert.True(vm.TryBeginNodeDrag(vm.Transform.ToScreen(new Point2D(1000, 0)), end.Id));
        var moved = vm.Transform.ToScreen(new Point2D(cursorXMm, cursorYMm));
        vm.UpdateDrag(moved, orthoSnap: true);
        vm.CommitDrag(moved, orthoSnap: true);

        // 앵커(원점)에서 가장 먼 절점이 방금 옮긴 끝점이다.
        var anchor = new Point2D(0, 0);
        var movedEnd = vm.Nodes
            .Select(x => vm.Transform.ToModel(new Point(x.X, x.Y)))
            .OrderByDescending(x => x.DistanceTo(anchor))
            .First();

        // 정확히 45° — 두 성분의 크기가 같고 부호가 기대한 사분면이다.
        Assert.Equal(Math.Abs(movedEnd.X), Math.Abs(movedEnd.Y), 3);
        Assert.Equal(signX, Math.Sign(movedEnd.X));
        Assert.Equal(signY, Math.Sign(movedEnd.Y));

        // 선 길이가 격자(기본 10mm) 배수다. 좌표는 격자를 벗어나도 된다.
        // %로 비교하면 1480.0000000000002 % 10 이 9.9999...로 튄다 — 반올림값과 직접 견준다.
        var length = anchor.DistanceTo(movedEnd);
        Assert.Equal(Math.Round(length / vm.GridSnapMm) * vm.GridSnapMm, length, 3);
        Assert.NotEqual(0, length);
    }

    /// <summary>Shift 각도 구속점 옆에 다른 선의 끝점이 있으면 그 끝점으로 확정한다 —
    /// 절점이 병합되는 편이 길이를 격자에 맞추는 것보다 쓸모 있다(docs/193 § B).
    /// <para>다른 선의 끝점은 원래부터 목록에 있으니 그 존재만으로는 아무것도 증명하지 못한다 —
    /// <b>각도 구속점 자리가 비었는지</b>로 판정한다.</para></summary>
    [Fact]
    public void EndPointDrag_WithShift_PrefersNearbyEndpointOverLengthGrid()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        // 각도 구속점에서 약 50mm 떨어진 자리(SnapTolerance 100mm 안)에 다른 선의 끝점을 둔다.
        Draw(vm, OtherEndpoint, new Point2D(2000, 2000));
        vm.Mode = PipeLayoutMode.Selection;
        vm.SnapEndpoint = true;
        var end = vm.Nodes.Single(x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(1000, 0)));

        Assert.True(vm.TryBeginNodeDrag(vm.Transform.ToScreen(new Point2D(1000, 0)), end.Id));
        var moved = vm.Transform.ToScreen(new Point2D(1200, 900));
        vm.UpdateDrag(moved, orthoSnap: true);
        vm.CommitDrag(moved, orthoSnap: true);

        Assert.DoesNotContain(vm.Nodes, x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), ShiftConstrainedEnd));
        Assert.Contains(vm.Nodes, x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), OtherEndpoint));
    }

    /// <summary>끝점 OSNAP을 꺼 두면 Shift는 각도 구속점만 쓴다 — 사용자 설정을 존중한다(docs/193 § B).</summary>
    [Fact]
    public void EndPointDrag_WithShift_IgnoresEndpoint_WhenEndpointOsnapIsOff()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        Draw(vm, OtherEndpoint, new Point2D(2000, 2000));
        vm.Mode = PipeLayoutMode.Selection;
        vm.SnapEndpoint = false;
        var end = vm.Nodes.Single(x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(1000, 0)));

        Assert.True(vm.TryBeginNodeDrag(vm.Transform.ToScreen(new Point2D(1000, 0)), end.Id));
        var moved = vm.Transform.ToScreen(new Point2D(1200, 900));
        vm.UpdateDrag(moved, orthoSnap: true);
        vm.CommitDrag(moved, orthoSnap: true);

        Assert.Contains(vm.Nodes, x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), ShiftConstrainedEnd));
    }

    /// <summary>원점 앵커에서 커서 (1200, 900)을 Shift로 끌었을 때의 각도 구속점.
    /// 45° 방향 정사영 길이 2100/√2 = 1484.9를 격자 10mm로 반올림한 1480을 그 방향으로 놓은 자리다(docs/193 § A).</summary>
    private static Point2D ShiftConstrainedEnd => new(1480 / Math.Sqrt(2), 1480 / Math.Sqrt(2));

    /// <summary><see cref="ShiftConstrainedEnd"/>에서 약 50mm 떨어진 다른 선의 끝점. SnapTolerance(100mm) 안이다.</summary>
    private static Point2D OtherEndpoint => new(1080, 1010);

    /// <summary>각도 스냅을 끄면 Shift 없이 격자 단위 자유 이동이다(종전 동작).</summary>
    [Fact]
    public void EndPointDrag_AngleSnapOff_MovesFreely()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.UseAngleSnap = false;
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.Mode = PipeLayoutMode.Selection;
        var end = vm.Nodes.Single(x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(1000, 0)));

        Assert.True(vm.TryBeginNodeDrag(vm.Transform.ToScreen(new Point2D(1000, 0)), end.Id));
        var moved = vm.Transform.ToScreen(new Point2D(1200, 900));
        vm.UpdateDrag(moved);
        vm.CommitDrag(moved);

        Assert.Contains(vm.Nodes, x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(1200, 900)));
    }

    /// <summary>끝점 드래그도 그리기처럼 반대쪽 끝 기준 45° 배수에 붙는다(2026-10-29). (1200, 900)은 36.9° → 45°.</summary>
    [Fact]
    public void EndPointDrag_AngleSnapOn_SnapsTo45DegreeFromOtherEnd()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.UseAngleSnap = true;
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.Mode = PipeLayoutMode.Selection;
        var end = vm.Nodes.Single(x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(1000, 0)));

        Assert.True(vm.TryBeginNodeDrag(vm.Transform.ToScreen(new Point2D(1000, 0)), end.Id));
        var moved = vm.Transform.ToScreen(new Point2D(1200, 900));
        vm.UpdateDrag(moved);
        vm.CommitDrag(moved);

        var edge = Assert.Single(vm.Edges);
        var a = vm.Transform.ToModel(new Point(edge.X1, edge.Y1));
        var b = vm.Transform.ToModel(new Point(edge.X2, edge.Y2));
        Assert.Equal(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y), 3);
    }

    /// <summary>끝점 드래그 중 다른 선 끝점 OSNAP이 각도 스냅보다 앞선다 — 붙이면 절점 병합.</summary>
    [Fact]
    public void EndPointDrag_OsnapEndpoint_WinsOverAngleSnap()
    {
        var vm = CreateDrawingViewModel();
        vm.UseAngleSnap = false;   // 작도는 자유 각도로
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        Draw(vm, new Point2D(3000, 0), new Point2D(1700, 900));
        vm.UseAngleSnap = true;
        vm.Mode = PipeLayoutMode.Selection;
        var end = vm.Nodes.Single(x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(1000, 0)));

        Assert.True(vm.TryBeginNodeDrag(vm.Transform.ToScreen(new Point2D(1000, 0)), end.Id));
        var moved = vm.Transform.ToScreen(new Point2D(1690, 880));
        vm.UpdateDrag(moved);
        Assert.True(vm.IsSnapMarkerVisible);
        vm.CommitDrag(moved);

        Assert.False(vm.IsSnapMarkerVisible);
        Assert.Contains(vm.Nodes, x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(1700, 900)));
        Assert.DoesNotContain(vm.Nodes, x => AreClose(vm.Transform.ToModel(new Point(x.X, x.Y)), new Point2D(1000, 0)));
    }

    private static bool AreClose(Point2D a, Point2D b) => a.DistanceTo(b) < 1e-3;

    private static void SelectSingleEdge(PipeLayoutViewModel vm) => SelectEdge(vm, vm.Edges.First());

    private static void SelectEdge(PipeLayoutViewModel vm, PipeEdgeItem edge)
    {
        vm.Mode = PipeLayoutMode.Selection;
        var minX = Math.Min(edge.X1, edge.X2) - 10;
        var maxX = Math.Max(edge.X1, edge.X2) + 10;
        var minY = Math.Min(edge.Y1, edge.Y2) - 10;
        var maxY = Math.Max(edge.Y1, edge.Y2) + 10;
        vm.BeginSelectionBox(new Point(minX, minY));
        vm.CommitSelectionBox(new Point(maxX, maxY));
    }

    // ── docs/233 § A — 선 선택 → 관경 편집칸 포커스 콜백 ─────────────────────────

    [Fact]
    public void SelectEdge_InSelectionMode_InvokesEdgeSelectedAction()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.Mode = PipeLayoutMode.Selection;
        var calls = 0;
        vm.EdgeSelectedAction = () => calls++;

        vm.SelectEdge(Assert.Single(vm.Edges).Id);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void CommitSelectionBox_WithEdge_InvokesEdgeSelectedAction_ButFittingOnlyDoesNot()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(500, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));
        vm.Mode = PipeLayoutMode.Selection;
        var edge = Assert.Single(vm.Edges);
        var fitting = Assert.Single(vm.InlineFittings);
        var calls = 0;
        vm.EdgeSelectedAction = () => calls++;

        // 부속만 감싸는 작은 창 선택 — 선 끝점이 밖이라 선은 잡히지 않는다(A-3).
        vm.BeginSelectionBox(new Point(fitting.X - 5, fitting.Y - 5));
        vm.CommitSelectionBox(new Point(fitting.X + 5, fitting.Y + 5));
        Assert.Empty(vm.SelectedEdgeIds);
        Assert.Equal([fitting.Id], vm.SelectedFittingIds);
        Assert.Equal(0, calls);

        // 선 전체를 감싸면 호출된다(A-1).
        vm.BeginSelectionBox(new Point(Math.Min(edge.X1, edge.X2) - 10, edge.Y1 - 10));
        vm.CommitSelectionBox(new Point(Math.Max(edge.X1, edge.X2) + 10, edge.Y1 + 10));
        Assert.Equal([edge.Id], vm.SelectedEdgeIds);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void SelectFitting_DoesNotInvokeEdgeSelectedAction()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(500, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));
        vm.Mode = PipeLayoutMode.Selection;
        var calls = 0;
        vm.EdgeSelectedAction = () => calls++;

        vm.SelectFitting(Assert.Single(vm.InlineFittings).Id);

        Assert.Equal(0, calls);
    }

    // ── docs/233 § B — 밸브 직경 후보 실수형 제한 · 유형 매개변수 관경 대조 ──────────

    private static PipeLayoutViewModel CreateTypeDiameterViewModel(double? nominalMm = 100)
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            accessoryNames: ["밸브 : DN100"],
            parameterNamesByFamily: new Dictionary<string, IEnumerable<string>> { ["밸브 : DN100"] = ["DN", "주석 축척 사용"] },
            numericParameterNamesByFamily: new Dictionary<string, IEnumerable<string>> { ["밸브 : DN100"] = ["DN"] },
            typeLengthParametersByType: new Dictionary<string, IReadOnlyList<PipeAccessoryTypeParameterDto>>
            {
                ["밸브 : DN100"] = nominalMm is { } mm ? [new PipeAccessoryTypeParameterDto("Nominal pipe size", mm)] : [],
            }));
        vm.DiameterMm = 100;
        return vm;
    }

    [Fact]
    public void ValveDiameterParameterCandidates_UseNumericInstanceParametersOnly()
    {
        var vm = CreateTypeDiameterViewModel();

        vm.SelectedFamilyTypeName = "밸브 : DN100";
        vm.BypassValveFamilyName = "밸브";
        vm.BypassValveTypeName = "밸브 : DN100";

        Assert.Equal(["DN"], vm.ValveParameterNames);
        Assert.Equal(["DN"], vm.BypassValveParameterNames);
        Assert.DoesNotContain("주석 축척 사용", vm.ValveParameterNames);
    }

    [Fact]
    public void ValveTypeDiameter_GuessesNominalPipeSize_AndReportsMatch()
    {
        var vm = CreateTypeDiameterViewModel();

        vm.SelectedFamilyTypeName = "밸브 : DN100";

        Assert.Equal(["Nominal pipe size"], vm.ValveTypeParameterNames);
        Assert.Equal("Nominal pipe size", vm.ValveTypeDiameterParameterName);
        Assert.False(vm.IsValveTypeDiameterMismatch);
        Assert.Contains("✓", vm.ValveTypeDiameterText);
        Assert.Contains("100", vm.ValveTypeDiameterText);
    }

    [Fact]
    public void ValveTypeDiameter_MismatchIsWarning_NotError()
    {
        var vm = CreateTypeDiameterViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(500, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(500, 0)));
        Assert.Single(vm.InlineFittings);
        Assert.DoesNotContain("경고:", vm.ValidationSummary);
        vm.Mode = PipeLayoutMode.Selection;
        vm.SelectEdge(Assert.Single(vm.Edges).Id);

        vm.DiameterMm = 200;   // 선택 관경만 200으로 — 유형 100mm와 어긋난다

        Assert.True(vm.IsValveTypeDiameterMismatch);
        Assert.Contains("200", vm.ValveTypeDiameterText);
        Assert.Contains("경고: 유형 직경이 선 관경과 다른 밸브 1개", vm.ValidationSummary);
        // 경고는 생성을 막지 않는다 — 오류 문구가 아니다.
        Assert.DoesNotContain("밸브", vm.ValidationSummary.Replace("경고: 유형 직경이 선 관경과 다른 밸브 1개 — 5번 섹션 유형 직경 매개변수 확인", ""));
    }

    [Fact]
    public void BypassValve_UsesFamilyFirstType_AsRepresentative()
    {
        // docs/244 § D-2 — 바이패스 관경 입력·유형 직경 대조가 없어졌다. 대표 유형은 패밀리 첫 유형이고(직경 파라미터 후보용),
        // 실제 유형은 모델 생성 때 DN으로 고른다.
        var vm = CreateTypeDiameterViewModel();
        vm.BypassValveFamilyName = "밸브";

        Assert.Equal("밸브 : DN100", vm.BypassValveTypeName);
        Assert.Equal(["DN"], vm.BypassValveParameterNames);
        Assert.DoesNotContain("바이패스 밸브 유형 직경", vm.ValidationSummary);
    }

    [Fact]
    public void ValveTypeDiameter_WithoutLengthTypeParameters_ExplainsAndNeverWarns()
    {
        var vm = CreateTypeDiameterViewModel(nominalMm: null);

        vm.SelectedFamilyTypeName = "밸브 : DN100";

        Assert.Empty(vm.ValveTypeParameterNames);
        Assert.Null(vm.ValveTypeDiameterParameterName);
        Assert.False(vm.IsValveTypeDiameterMismatch);
        Assert.Contains("확인할 수 없습니다", vm.ValveTypeDiameterText);
    }

    [Fact]
    public void ValveTypeDiameterParameterName_UserChoiceIsRememberedPerFamily()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            accessoryNames: ["밸브 : DN100", "밸브 : DN200"],
            typeLengthParametersByType: new Dictionary<string, IReadOnlyList<PipeAccessoryTypeParameterDto>>
            {
                ["밸브 : DN100"] = [new PipeAccessoryTypeParameterDto("Nominal pipe size", 100), new PipeAccessoryTypeParameterDto("길이", 500)],
                ["밸브 : DN200"] = [new PipeAccessoryTypeParameterDto("Nominal pipe size", 200), new PipeAccessoryTypeParameterDto("길이", 600)],
            }));
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        Assert.Equal("Nominal pipe size", vm.ValveTypeDiameterParameterName);

        vm.ValveTypeDiameterParameterName = "길이";
        vm.SelectedFamilyTypeName = "밸브 : DN200";

        Assert.Equal("길이", vm.ValveTypeDiameterParameterName);
        Assert.Contains("600", vm.ValveTypeDiameterText);
    }

    private static void Draw(PipeLayoutViewModel vm, Point2D start, Point2D end)
    {
        vm.HandleCanvasClick(vm.Transform.ToScreen(start));
        vm.HandleCanvasClick(vm.Transform.ToScreen(end));
    }

    private static void PrepareVisibleSubLinePreview(PipeLayoutViewModel vm)
    {
        DisableOsnap(vm);
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(-1000, -1000)));
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(0, 0)));
        Assert.True(vm.IsSubLineVisible);
        Assert.NotEmpty(vm.TempDimensions);
    }

    private static void AssertPreviewCleared(PipeLayoutViewModel vm)
    {
        Assert.False(vm.IsSubLineVisible);
        Assert.Empty(vm.TempDimensions);
    }

    private static PipeLayoutViewModel CreateDrawingViewModel(IEnumerable<string>? accessoryNames = null)
    {
        var vm = CreateViewModel(accessoryNames);
        vm.HandleCanvasSizeChanged(1000, 1000);
        return vm;
    }

    private static void DisableOsnap(PipeLayoutViewModel vm)
    {
        vm.SnapReferencePoint = false;
        vm.SnapEndpoint = false;
        vm.SnapMidpoint = false;
        vm.SnapQuadrant = false;
        vm.SnapIntersection = false;
        vm.SnapPerpendicular = false;
        vm.SnapNearest = false;
        vm.SnapOutline = false;
    }

    private static Point2D EdgeEnd(PipeLayoutViewModel vm, PipeEdgeItem edge) =>
        vm.Transform.ToModel(new Point(edge.X2, edge.Y2));

    private static void AssertPoint(Point2D expected, Point2D actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    [Fact]
    public void ApplyOutline_SetsArrowOffsetsToHalfOfInnerHeight()
    {
        var vm = CreateDrawingViewModel();

        vm.ApplyOutline(CreateOutline(4000, 3000));

        Assert.Equal(1500, vm.InOffsetMm, 6);
        Assert.Equal(1500, vm.OutOffsetMm, 6);
    }

    [Fact]
    public void ApplyOutline_KeepsUserEditedOffset_AndStillUpdatesTheOther()
    {
        var vm = CreateDrawingViewModel();
        vm.InOffsetMm = 300;

        vm.ApplyOutline(CreateOutline(4000, 3000));

        Assert.Equal(300, vm.InOffsetMm, 6);
        Assert.Equal(1500, vm.OutOffsetMm, 6);
    }

    [Fact]
    public void ClearOutline_ResetsUserEditedFlag_SoNextPickAppliesAutoOffset()
    {
        var vm = CreateDrawingViewModel();
        vm.InOffsetMm = 300;
        vm.ApplyOutline(CreateOutline(4000, 3000));

        vm.ClearOutlineCommand.Execute(null);
        vm.ApplyOutline(CreateOutline(4000, 3000));

        Assert.Equal(1500, vm.InOffsetMm, 6);
    }

    [Fact]
    public void ApplyPickedPiping_ReplacesDrawnGraph()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 1000));
        var drawnId = Assert.Single(vm.Edges).Id;

        vm.ApplyPickedPiping(CreateImport());

        var edge = Assert.Single(vm.Edges);
        Assert.NotEqual(drawnId, edge.Id);
        Assert.Equal(1200, vm.Elevation, 6);
    }

    [Fact]
    public void ElevationM_IsMeterViewOfMillimeterElevation()
    {
        // 관 중심 높이(EL) 입력은 m, 내부·Revit 배치는 mm다(docs/251 § C).
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ElevationM = 0.214;

        Assert.Equal(214, vm.Elevation, 6);
        Assert.Contains(nameof(PipeLayoutViewModel.ElevationM), changed);

        vm.ApplyPickedPiping(CreateImport());
        Assert.Equal(1.2, vm.ElevationM, 6);
    }

    [Fact]
    public void ApplyPickedPiping_ThenUndo_RestoresDrawnGraph()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 1000));
        var drawnId = Assert.Single(vm.Edges).Id;
        vm.ApplyPickedPiping(CreateImport());

        vm.UndoCommand.Execute(null);

        Assert.Equal(drawnId, Assert.Single(vm.Edges).Id);
    }

    [Fact]
    public void ApplyPickedPiping_Null_KeepsGraph()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 1000));
        var drawnId = Assert.Single(vm.Edges).Id;

        vm.ApplyPickedPiping(null);

        Assert.Equal(drawnId, Assert.Single(vm.Edges).Id);
        Assert.Contains("취소", vm.Status);
    }

    [Fact]
    public void ApplyPickedPiping_WithoutOutline_MovesReferenceToImportedCenter()
    {
        var vm = CreateDrawingViewModel();

        vm.ApplyPickedPiping(CreateImport());

        // 프로젝트 좌표 (0,0)~(3000,0)의 중심이 기준점이 되고, 캔버스에서는 ±1500이 된다.
        Assert.Equal(1500, vm.ReferenceX, 6);
        Assert.Equal(0, vm.ReferenceY, 6);
        Assert.Equal(3000, Assert.Single(vm.Edges).LengthMm, 3);
    }

    [Fact]
    public void ApplyPickedPiping_NodeFitting_IsSentWithSelectedEdge()
    {
        // docs/216 § A-3 — 가져온 곡관도 [Revit에서 보기]에 함께 잡혀야 한다.
        var vm = CreateDrawingViewModel();
        var import = CreateImport(edges:
        [
            new ImportedEdge(new Point2D(0, 0), new Point2D(3000, 0), 150) { ElementIds = [11] },
            new ImportedEdge(new Point2D(3000, 0), new Point2D(3000, 2000), 150) { ElementIds = [33] },
        ]) with { NodeFittings = [new ImportedNodeFitting(new Point2D(3000, 0), 22)] };
        vm.ApplyPickedPiping(import);
        IReadOnlyList<long>? sent = null;
        vm.SelectInRevitAction = ids => sent = ids;

        SelectSingleEdge(vm);
        vm.SelectInRevitCommand.Execute(null);

        Assert.NotNull(sent);
        Assert.Contains(22L, sent);
        Assert.True(sent!.Contains(11L) || sent.Contains(33L));
    }

    [Theory]
    [InlineData(3, 0, "모두 삭제됨")]
    [InlineData(3, 1, "부재 1개 (2개 삭제돼 제외)")]
    [InlineData(3, 3, "Revit 선택 — 부재 3개")]
    public void ApplySelectInRevitResult_ReportsActualSelection(int requested, int selected, string expected)
    {
        // docs/216 § B-3 — 상태줄은 요청 수가 아니라 실제로 선택된 수를 알린다.
        var vm = CreateDrawingViewModel();

        vm.ApplySelectInRevitResult(requested, selected);

        Assert.Contains(expected, vm.Status);
    }

    [Fact]
    public void ApplyPickedPiping_WithOutline_KeepsReferencePoint()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        var referenceX = vm.ReferenceX;
        var referenceY = vm.ReferenceY;

        vm.ApplyPickedPiping(CreateImport());

        Assert.Equal(referenceX, vm.ReferenceX, 6);
        Assert.Equal(referenceY, vm.ReferenceY, 6);
    }

    [Fact]
    public void ApplyPickedPiping_PlacesInlineFittingOnImportedEdge()
    {
        var vm = CreateDrawingViewModel();

        vm.ApplyPickedPiping(CreateImport(fittings: [new ImportedFitting(0, "밸브 : DN100", 0.5)]));

        var fitting = Assert.Single(vm.InlineFittings);
        Assert.Equal("밸브 : DN100", fitting.FamilyTypeName);
        Assert.Equal(0.5, fitting.T, 3);
    }

    [Fact]
    public void ApplyPickedPiping_PreservesImportedFittingsCloserThanManualSpacing()
    {
        var vm = CreateDrawingViewModel();

        vm.ApplyPickedPiping(CreateImport(fittings:
        [
            new ImportedFitting(0, "밸브 : DN100", 0.5),
            new ImportedFitting(0, "DH_이음관 : DN100", 0.52, IsJoint: true)
        ]));

        Assert.Equal([0.5, 0.52], vm.InlineFittings.OrderBy(x => x.T).Select(x => x.T));
    }

    [Fact]
    public void ApplyPickedPiping_BackfillsOnlyFamiliesThatExistInComboBox()
    {
        var vm = CreateDrawingViewModel();
        var bendBefore = vm.BendFamilyTypeName;

        vm.ApplyPickedPiping(CreateImport(bendFamilyTypeName: "없는곡관 : DN999"));

        Assert.Equal("직관 : DN100", vm.SegmentFamilyTypeName);
        Assert.Equal(bendBefore, vm.BendFamilyTypeName);   // 목록에 없는 이름은 선택하지 않는다
        Assert.Equal(150, vm.DiameterMm, 6);
    }

    [Fact]
    public void ApplyPickedPiping_TracksSourceElements_UntilCleared()
    {
        var vm = CreateDrawingViewModel();
        Assert.False(vm.HasImportedSource);

        vm.ApplyPickedPiping(CreateImport([11, 22]));
        Assert.True(vm.HasImportedSource);

        vm.ClearCommand.Execute(null);
        Assert.False(vm.HasImportedSource);
    }

    /// <summary>가져왔으면 체크 없이 항상 원본을 지우고 다시 만든다(docs/163 § B).</summary>
    [Fact]
    public void CreateModelCommand_AlwaysSendsImportedSourceElements()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyPickedPiping(CreateImport([11, 22]));
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(new long[] { 11, 22 }, request!.SourceElementIdsToDelete);
    }

    /// <summary>다시 [모델 생성]하면 직전 생성분을 지우고 다시 만든다 — 종전에는 한 벌 더 생겼다(docs/235 § C).</summary>
    [Fact]
    public void CreateModelCommand_AfterSuccess_SendsPreviouslyCreatedElementsForReplacement()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;
        vm.CreateModelCommand.Execute(null);
        var edgeId = vm.Edges[0].Id;
        vm.ApplyModelCreationResult(true, "완료", [], new Dictionary<Guid, IReadOnlyList<long>> { [edgeId] = [101, 102] });
        Assert.True(vm.HasReplaceTarget);
        Assert.False(vm.HasImportedSource);

        vm.CreateModelCommand.Execute(null);

        Assert.Equal(new long[] { 101, 102 }, request!.SourceElementIdsToDelete);
    }

    /// <summary>초기화하면 직전 생성분 연결도 끊는다 — 새 배치가 앞서 만든 배관을 지우지 않게(docs/235 § C).</summary>
    [Fact]
    public void Clear_AfterSuccess_ForgetsPreviouslyCreatedElements()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.CreateModelAction = _ => { };
        vm.CreateModelCommand.Execute(null);
        vm.ApplyModelCreationResult(true, "완료", [], new Dictionary<Guid, IReadOnlyList<long>> { [vm.Edges[0].Id] = [101] });

        vm.ClearCommand.Execute(null);
        Assert.False(vm.HasReplaceTarget);

        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 3000), new Point2D(1000, 3000));
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;
        vm.CreateModelCommand.Execute(null);
        Assert.Empty(request!.SourceElementIdsToDelete ?? []);
    }

    /// <summary>생성 대기 중에는 그리기·Undo·초기화가 막히고 생성 버튼도 다시 켜지지 않는다(docs/235 § D).</summary>
    [Fact]
    public void WhileCreating_EditsAreIgnored_AndCreateStaysDisabled()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.CreateModelAction = _ => { };
        vm.CreateModelCommand.Execute(null);
        Assert.True(vm.IsCreating);

        Draw(vm, new Point2D(0, 2000), new Point2D(1000, 2000));

        Assert.Single(vm.Edges);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.ClearCommand.CanExecute(null));
        Assert.True(vm.IsCreating);
        Assert.False(vm.CreateModelCommand.CanExecute(null));

        vm.ApplyModelCreationResult(true, "완료");
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    /// <summary>짧은 선에 부속을 두 번 넣으면 Core가 PL-113을 던진다 — 클릭 핸들러 밖으로 새지 않고 상태줄로 알린다(docs/235 § B).</summary>
    [Fact]
    public void PlacingSecondFittingOnShortEdge_ReportsCodeInsteadOfThrowing()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        Draw(vm, new Point2D(0, 0), new Point2D(150, 0));
        vm.SelectedFamilyTypeName = "밸브 : DN100";
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(75, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(75, 0)));
        Assert.Single(vm.InlineFittings);

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(75, 0)));
        var ex = Record.Exception(() => vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(75, 0))));

        Assert.Null(ex);
        Assert.Single(vm.InlineFittings);
        Assert.Contains("[PL-113]", vm.Status);
    }

    /// <summary>캔버스에서 선을 지워도 삭제 대상은 원본 전량 그대로다 — 전량 재생성이기 때문이다(docs/163 § B).</summary>
    [Fact]
    public void CreateModelCommand_AfterDeletingEdgeOnCanvas_StillSendsEveryImportedSource()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyPickedPiping(CreateImport([11, 22], edges:
        [
            new ImportedEdge(new Point2D(0, 0), new Point2D(3000, 0), 150),
            new ImportedEdge(new Point2D(0, 3000), new Point2D(3000, 3000), 150),
        ]));
        vm.Mode = PipeLayoutMode.Selection;
        vm.SelectEdge(vm.Edges[0].Id);
        vm.DeleteSelectedEdge();
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        Assert.Single(request!.Edges);
        Assert.Equal(new long[] { 11, 22 }, request!.SourceElementIdsToDelete);
    }

    /// <summary>Ctrl+Z로 가져오기를 되돌리면 삭제 예약도 함께 풀려야 한다(docs/163 § C).</summary>
    [Fact]
    public void Undo_AfterImport_ReleasesSourceElementsToDelete()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyPickedPiping(CreateImport([11, 22]));
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.UndoCommand.Execute(null);

        Assert.False(vm.HasImportedSource);
        // 되돌리면 캔버스가 비므로 생성 검증에 걸린다. 선을 하나 그려 요청이 나가게 한 뒤 삭제 목록을 본다.
        DisableOsnap(vm);
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(0, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(3000, 0)));
        vm.CreateModelCommand.Execute(null);
        Assert.NotNull(request);
        Assert.Empty(request!.SourceElementIdsToDelete ?? []);
    }

    /// <summary>초기화도 되돌릴 수 있어야 한다 — 원본 연결이 스냅샷에 함께 실린다(docs/163 § C-3).</summary>
    [Fact]
    public void Undo_AfterClear_RestoresImportedSource()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyPickedPiping(CreateImport([11, 22]));
        vm.ClearCommand.Execute(null);
        Assert.False(vm.HasImportedSource);

        vm.UndoCommand.Execute(null);

        Assert.True(vm.HasImportedSource);
    }

    [Fact]
    public void ApplyModelCreationResult_AfterDeletingSource_ForgetsImportedElements()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyPickedPiping(CreateImport([11, 22]));
        vm.CreateModelAction = _ => { };
        vm.CreateModelCommand.Execute(null);

        vm.ApplyModelCreationResult(true, "완료");

        Assert.False(vm.HasImportedSource);
    }

    /// <summary>프로젝트 좌표 (0,0)~(3000,0) 배관 하나짜리 가져오기 결과(docs/158).</summary>
    private static PipeLayoutImport CreateImport(
        IReadOnlyList<long>? sourceIds = null,
        IReadOnlyList<ImportedFitting>? fittings = null,
        string? bendFamilyTypeName = "곡관90 : DN100",
        string zone = "",
        IReadOnlyList<ImportedEdge>? edges = null)
        => new(
            edges ?? [new ImportedEdge(new Point2D(0, 0), new Point2D(3000, 0), 150)],
            fittings ?? [],
            1200,
            sourceIds ?? [],
            [],
            150,
            "직관 : DN100",
            bendFamilyTypeName,
            "T형 : DN100",
            zone);

    [Fact]
    public void ApplyOutline_ReadsValveRoomZoneFromWalls()
    {
        var vm = CreateDrawingViewModel();

        vm.ApplyOutline(CreateOutline(4000, 3000, "이토밸브실", "이토밸브실", "이토밸브실", "이토밸브실"));

        Assert.Equal("이토밸브실", vm.ValveRoomZone);
        Assert.Contains("유형 이토밸브실", vm.Status);
    }

    [Fact]
    public void ApplyOutline_MixedZone_DoesNotOverwriteUserChoice()
    {
        var vm = CreateDrawingViewModel();
        vm.ValveRoomZone = "제수밸브실";

        vm.ApplyOutline(CreateOutline(4000, 3000, "이토밸브실", "공기밸브실", "", ""));

        // 값을 지어내지 않는다. 사용자가 고른 값도 빈 값으로 덮지 않는다.
        Assert.Equal("제수밸브실", vm.ValveRoomZone);
        Assert.Contains("섞여 있어", vm.Status);
    }

    [Fact]
    public void ApplyOutline_WithoutZone_KeepsUserChoice()
    {
        var vm = CreateDrawingViewModel();
        vm.ValveRoomZone = "공기밸브실";

        vm.ApplyOutline(CreateOutline(4000, 3000));

        Assert.Equal("공기밸브실", vm.ValveRoomZone);
    }

    [Fact]
    public void ClearOutline_KeepsValveRoomZone()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000, "타이밸브실", "타이밸브실", "타이밸브실", "타이밸브실"));

        vm.ClearOutlineCommand.Execute(null);

        Assert.Equal("타이밸브실", vm.ValveRoomZone);
    }

    [Fact]
    public void CreateModelCommand_SendsValveRoomZone()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.ValveRoomZone = "제수밸브실";
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        Assert.Equal("제수밸브실", request!.Zone);
    }

    [Fact]
    public void CreateModelCommand_WithoutZone_SendsEmptyZone()
    {
        var vm = CreateDrawingViewModel();
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        Assert.Equal(string.Empty, request!.Zone);
    }

    [Fact]
    public void ApplyPickedPiping_RestoresValveRoomZone()
    {
        var vm = CreateDrawingViewModel();

        vm.ApplyPickedPiping(CreateImport(zone: "공기밸브실"));

        Assert.Equal("공기밸브실", vm.ValveRoomZone);
    }

    // ── IN/OUT 다중 포트 + 추천 배치(docs/172 § F-2) ─────────────────────────────────

    [Fact]
    public void Ports_DefaultToOneInAndOneOut_NeitherRemovable()
    {
        var vm = CreateViewModel();

        Assert.Equal(["IN", "OUT"], vm.Ports.Select(x => x.Label));
        Assert.Equal([PortFlow.In, PortFlow.Out], vm.Ports.Select(x => x.Flow));
        Assert.All(vm.Ports, p => Assert.False(p.CanRemove));
    }

    [Fact]
    public void AddInPort_InsertsIn2RightAfterIn_OnLeftWall_AndSpreadsBothEvenly()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));

        vm.AddInPortCommand.Execute(null);

        // 목록은 IN, IN2, …, OUT, OUT2 … 순이다(사용자 확정) — 맨 뒤가 아니라 IN 바로 뒤에 들어간다.
        Assert.Equal(["IN", "IN2", "OUT"], vm.Ports.Select(x => x.Label));
        var added = vm.Ports[1];
        Assert.Equal(OutlineSide.Left, added.Side);
        // 같은 벽 포트는 벽 길이 N+1 등분으로 자동 배치된다(docs/174 D1) — IN도 함께 1/3 자리로 옮겨진다.
        Assert.Equal(1000, vm.Ports[0].OffsetMm, 6);
        Assert.Equal(2000, added.OffsetMm, 6);
        Assert.Equal(1500, vm.Ports[2].OffsetMm, 6);
        // 기본 IN·OUT은 지울 수 없고 추가된 것만 지울 수 있다.
        Assert.False(vm.Ports[0].CanRemove);
        Assert.True(added.CanRemove);
        Assert.False(vm.Ports[2].CanRemove);
    }

    // ── docs/174 벽 중심 기준 균등 자동 배치 ────────────────────────────────────────

    [Fact]
    public void AddInPort_Twice_SpreadsThreePortsAtQuarters()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));

        vm.AddInPortCommand.Execute(null);
        vm.AddInPortCommand.Execute(null);

        Assert.Equal(["IN", "IN2", "IN3", "OUT"], vm.Ports.Select(x => x.Label));
        Assert.Equal([750, 1500, 2250, 1500], vm.Ports.Select(x => x.OffsetMm));
    }

    [Fact]
    public void AddOutPort_DoesNotMoveInPorts()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));

        vm.AddInPortCommand.Execute(null);
        vm.AddOutPortCommand.Execute(null);

        Assert.Equal(["IN", "IN2", "OUT", "OUT2"], vm.Ports.Select(x => x.Label));
        Assert.Equal([1000, 2000, 1000, 2000], vm.Ports.Select(x => x.OffsetMm));
    }

    [Fact]
    public void AddInPort_KeepsUserEditedIn_AndPlacesIn2AtRankTwo()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.Ports[0].OffsetMm = 300;   // 세터로 편집 → UserEdited

        vm.AddInPortCommand.Execute(null);

        // 편집한 포트는 고정(docs/174 D2). 자리는 편집 여부와 무관하게 세므로 IN2는 N=2의 k=2.
        Assert.Equal(300, vm.Ports[0].OffsetMm, 6);
        Assert.True(vm.Ports[0].UserEdited);
        Assert.Equal(2000, vm.Ports[1].OffsetMm, 6);
        Assert.False(vm.Ports[1].UserEdited);
    }

    [Fact]
    public void RemovePort_Redistributes_RemainingUnedited()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.AddInPortCommand.Execute(null);
        vm.AddInPortCommand.Execute(null);

        vm.RemovePortCommand.Execute(vm.Ports.Single(x => x.Label == "IN2"));

        Assert.Equal(["IN", "IN3", "OUT"], vm.Ports.Select(x => x.Label));
        Assert.Equal([1000, 2000, 1500], vm.Ports.Select(x => x.OffsetMm));
    }

    [Fact]
    public void RemovePort_KeepsUserEditedNeighbor()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.AddInPortCommand.Execute(null);
        vm.AddInPortCommand.Execute(null);
        vm.Ports.Single(x => x.Label == "IN3").OffsetMm = 2900;

        vm.RemovePortCommand.Execute(vm.Ports.Single(x => x.Label == "IN2"));

        Assert.Equal(1000, vm.Ports.Single(x => x.Label == "IN").OffsetMm, 6);
        Assert.Equal(2900, vm.Ports.Single(x => x.Label == "IN3").OffsetMm, 6);
    }

    [Fact]
    public void ApplyOutline_SpreadsUneditedPortsPerSide()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.AddInPortCommand.Execute(null);

        // 재피킹: 변 길이는 방금 받은 외곽(6000)으로 재야 한다 — _canvasOutline은 아직 이전 외곽이다.
        vm.ApplyOutline(CreateOutline(4000, 6000));

        Assert.Equal(["IN", "IN2", "OUT"], vm.Ports.Select(x => x.Label));
        Assert.Equal([2000, 4000, 3000], vm.Ports.Select(x => x.OffsetMm));
    }

    [Fact]
    public void AddPort_UpdatesValidation_NoOverlapForDefaultDiameter()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));

        vm.AddInPortCommand.Execute(null);

        Assert.DoesNotContain("관경보다 가까움", vm.ValidationSummary);
    }

    [Fact]
    public void Ports_StayOrderedByFlowThenOrdinal_WhenMixingInAndOutAdds()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));

        vm.AddOutPortCommand.Execute(null);
        vm.AddInPortCommand.Execute(null);
        vm.AddOutPortCommand.Execute(null);
        vm.AddInPortCommand.Execute(null);

        Assert.Equal(["IN", "IN2", "IN3", "OUT", "OUT2", "OUT3"], vm.Ports.Select(x => x.Label));
    }

    [Fact]
    public void RemovePort_DropsPort_AndReusesFreedOrdinal_InSortedPosition()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.AddInPortCommand.Execute(null);
        vm.AddInPortCommand.Execute(null);
        var in2 = vm.Ports.Single(x => x.Label == "IN2");

        vm.RemovePortCommand.Execute(in2);
        Assert.Equal(["IN", "IN3", "OUT"], vm.Ports.Select(x => x.Label));

        vm.AddInPortCommand.Execute(null);
        Assert.Equal(["IN", "IN2", "IN3", "OUT"], vm.Ports.Select(x => x.Label));
    }

    [Fact]
    public void RemovePort_DefaultInAndOut_AreNeverRemovable()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.AddInPortCommand.Execute(null);
        vm.AddOutPortCommand.Execute(null);
        var defaultIn = vm.Ports.Single(x => x.Label == "IN");
        var defaultOut = vm.Ports.Single(x => x.Label == "OUT");

        Assert.False(vm.RemovePortCommand.CanExecute(defaultIn));
        Assert.False(vm.RemovePortCommand.CanExecute(defaultOut));
        vm.RemovePortCommand.Execute(defaultIn);
        vm.RemovePortCommand.Execute(defaultOut);

        Assert.Equal(["IN", "IN2", "OUT", "OUT2"], vm.Ports.Select(x => x.Label));
        Assert.True(vm.RemovePortCommand.CanExecute(vm.Ports.Single(x => x.Label == "IN2")));
        Assert.True(vm.RemovePortCommand.CanExecute(vm.Ports.Single(x => x.Label == "OUT2")));
    }

    [Fact]
    public void AddPort_WithoutOutline_IsDisabled()
    {
        var vm = CreateViewModel();

        Assert.False(vm.AddInPortCommand.CanExecute(null));
        Assert.False(vm.AddOutPortCommand.CanExecute(null));
    }

    [Fact]
    public void ApplyOutline_RecentersEveryPortThatWasNotEdited()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.AddInPortCommand.Execute(null);
        vm.Ports.Single(x => x.Label == "IN2").OffsetMm = 700;

        vm.ApplyOutline(CreateOutline(4000, 2000));

        // docs/174: 좌측 벽은 IN·IN2 2개라 IN은 2000·1/3(편집한 IN2는 자리만 세고 값은 700 유지), 우측 벽은 OUT 1개라 중앙.
        Assert.Equal(666.7, vm.InOffsetMm, 6);
        Assert.Equal(1000, vm.OutOffsetMm, 6);
        Assert.Equal(700, vm.Ports.Single(x => x.Label == "IN2").OffsetMm, 6);
    }

    [Fact]
    public void InOffsetMm_Wrapper_ReadsAndWritesFirstInPort()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));

        vm.InOffsetMm = 300;

        var first = vm.Ports.First(x => x.Flow == PortFlow.In);
        Assert.Equal(300, first.OffsetMm, 6);
        Assert.True(first.UserEdited);
        Assert.Equal(300, vm.InOffsetMm, 6);
    }

    [Fact]
    public void ValidationSummary_FlagsPortsOnSameWallCloserThanDiameter()
    {
        var vm = CreateDrawingViewModel();
        vm.DiameterMm = 100;
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.AddInPortCommand.Execute(null);
        var in2 = vm.Ports.Single(x => x.Label == "IN2");

        in2.OffsetMm = 1050;   // IN 1000(docs/174 자동 배치) 과 50mm 차 < 관경 100
        Assert.Contains("관경보다 가까움", vm.ValidationSummary);

        in2.OffsetMm = 1200;
        Assert.DoesNotContain("관경보다 가까움", vm.ValidationSummary);
    }

    [Fact]
    public void OutlineArrows_FollowPorts_AndColorByFlowNotLabel()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.AddInPortCommand.Execute(null);

        Assert.Equal(vm.Ports.Count, vm.OutlineArrows.Count);
        var in2 = vm.OutlineArrows.Single(x => x.Label == "IN2");
        Assert.Equal("#1E8449", in2.Color);
        Assert.Equal(PortFlow.In, in2.Flow);
    }

    [Fact]
    public void CanApplyTemplate_RequiresOutlineAndKnownZone()
    {
        var vm = CreateDrawingViewModel();
        vm.ValveRoomZone = "이토밸브실";
        Assert.False(vm.CanApplyTemplate);

        vm.ApplyOutline(CreateOutline(4000, 3000));
        Assert.True(vm.CanApplyTemplate);

        vm.ValveRoomZone = "";
        Assert.False(vm.CanApplyTemplate);
        Assert.Contains("먼저 고르세요", vm.TemplateHint);
    }

    [Fact]
    public void DiameterMm_NewViewModel_DefaultsTo300()
    {
        Assert.Equal(300, CreateViewModel().DiameterMm);
    }

    [Fact]
    public void ApplyTemplate_SluiceRoom_UsesGateValveTypeMatchingEdgeDiameter()
    {
        var vm = CreateDrawingViewModel(["DH_게이트 밸브 : 100A", "DH_게이트 밸브 : 300A"]);
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.ValveRoomZone = "제수밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Equal("DH_게이트 밸브 : 300A", Assert.Single(vm.InlineFittings, x => !x.IsBypassSet).FamilyTypeName);
        Assert.Single(vm.BypassSetItems);
        Assert.DoesNotContain("바이패스 세트는", vm.Status);
    }

    // 타이밸브실(2026-11-09): 선 5본(T 2개), 바이패스 3세트, 버터플라이 6개(버터플라이 패밀리 선택)
    [Fact]
    public void ApplyTemplate_TieRoom_ThreeBypassSetsWithTwoButterflyValvesEach()
    {
        var vm = CreateDrawingViewModel(["DH_게이트 밸브 : 300A", "DH_버터플라이 밸브 : 300A"]);
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.ValveRoomZone = "타이밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Equal(5, vm.Edges.Count);
        Assert.Equal(3, vm.BypassSetItems.Count);
        var valves = vm.InlineFittings.Where(x => !x.IsBypassSet).ToList();
        Assert.Equal(6, valves.Count);
        Assert.All(valves, x => Assert.Equal("DH_버터플라이 밸브 : 300A", x.FamilyTypeName));
        // docs/322: 세트 3개 = 밸브 2개 세트(V2 = V1, 단관 300)
        Assert.All(vm.BypassSetItems, x => Assert.Contains("단관 300mm", x.BypassSummary));
    }

    // docs/322: 2개 세트는 타이밸브실만 — 제수밸브실 세트는 종전대로 밸브 1개
    [Fact]
    public void ApplyTemplate_SluiceRoom_BypassSetKeepsSingleValve()
    {
        var vm = CreateDrawingViewModel(["DH_게이트 밸브 : 300A"]);
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.ValveRoomZone = "제수밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.DoesNotContain("단관", Assert.Single(vm.BypassSetItems).BypassSummary);
    }

    [Fact]
    public void ApplyTemplate_SluiceRoom_MissingMatchingGateTypeReportsUnassignedValve()
    {
        var vm = CreateDrawingViewModel(["DH_게이트 밸브 : 100A"]);
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.ValveRoomZone = "제수밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Equal(string.Empty, Assert.Single(vm.InlineFittings, x => !x.IsBypassSet).FamilyTypeName);
        Assert.Contains("패밀리 미지정", vm.Status);
    }

    [Fact]
    public void ApplyTemplate_SluiceRoom_OutsideBypassRangeWarnsButKeepsSet()
    {
        var vm = CreateDrawingViewModel(["DH_게이트 밸브 : 100A"]);
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.ValveRoomZone = "제수밸브실";
        vm.DiameterMm = 100;

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Single(vm.BypassSetItems);
        Assert.Contains("덕타일주철관 DN300~1200", vm.Status);
        Assert.Contains("현재 DN100", vm.Status);
    }

    [Fact]
    public void ApplyTemplate_AirRoom_DrawsOneRunBetweenInAndOutAnchors()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.ValveRoomZone = "공기밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        var edge = Assert.Single(vm.Edges);
        var inArrow = vm.OutlineArrows.Single(x => x.Label == "IN");    // IN 화살표 tip(X2,Y2) = 벽면 접점
        var outArrow = vm.OutlineArrows.Single(x => x.Label == "OUT");  // OUT 화살표 tail(X1,Y1) = 벽면 접점
        Assert.Equal(inArrow.X2, edge.X1, 1);
        Assert.Equal(inArrow.Y2, edge.Y1, 1);
        Assert.Equal(outArrow.X1, edge.X2, 1);
        Assert.Equal(outArrow.Y1, edge.Y2, 1);
        Assert.Equal(4000, edge.LengthMm, 6);
    }

    [Fact]
    public void ApplyTemplate_SluiceRoom_MainValveAndBypassSetOnOneRun()
    {
        // 표준도(2026-10-28): IN2 없음, 본관 밸브 + 부제수 바이패스 세트가 같은 선 같은 자리.
        var vm = CreateDrawingViewModel(["DH_게이트 밸브 : 300A"]);
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.ValveRoomZone = "제수밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Equal(["IN", "OUT"], vm.Ports.Select(x => x.Label));
        Assert.Single(vm.Edges);
        Assert.Equal("DH_게이트 밸브 : 300A", Assert.Single(vm.InlineFittings, x => !x.IsBypassSet).FamilyTypeName);
        Assert.Single(vm.BypassSetItems);
        Assert.All(vm.Ports, p => Assert.True(p.UserEdited));   // 프리셋 이격은 사용자 값으로 취급(docs/172 § 0-4 A5)
    }

    [Fact]
    public void ApplyTemplate_MudRoom_RunStopsAtMiddleWall_NoOutPort()
    {
        // 표준도(2026-10-28): 이토관은 중간벽 방류실 쪽 면까지, 방류관 없음. 중간벽 없는 외곽이면 중앙에서 끝.
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4500, 2500));
        vm.ValveRoomZone = "이토밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Equal(["IN"], vm.Ports.Select(x => x.Label));
        Assert.Equal(2250, Assert.Single(vm.Edges).LengthMm, 1);
        Assert.Single(vm.InlineFittings, x => !x.IsBypassSet);
    }

    [Fact]
    public void ApplyTemplate_RelativeMode_OverwritesOffsetWith600()
    {
        var level = new LevelInfo(1, "1층", 1000);
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: [level]));
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.ElevationLevel = level;
        vm.ElevationOffsetMm = 100;
        vm.ValveRoomZone = "제수밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Equal(600, vm.ElevationOffsetMm);
        Assert.Equal(1600, vm.Elevation);
    }

    [Fact]
    public void ApplyTemplate_AbsoluteMode_KeepsElevation()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.IsAbsoluteElevation = true;
        vm.Elevation = 2000;
        vm.ValveRoomZone = "제수밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Equal(2000, vm.Elevation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyTemplate_AirRoom_FollowsMainPipeVoid(bool alongX)
    {
        // 공기밸브실: 기초 관통 Void 자리·관경·높이를 따름. Offset 600 규칙 예외(2026-10-29).
        var level = new LevelInfo(1, "1층", 1000);
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: [level]));
        var walls = CreateOutline(2000, 3000).Walls;
        vm.ApplyOutline(new ValveRoomOutline(walls, null, new MainPipeVoid(new Point2D(700, 800), alongX, 1500, -1200)));
        vm.ElevationLevel = level;
        vm.ValveRoomZone = "공기밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        var edge = Assert.Single(vm.Edges);
        Assert.Equal(1500, edge.DiameterMm, 6);
        Assert.Equal(alongX ? 2000 : 3000, edge.LengthMm, 1);
        Assert.Equal(1500, vm.DiameterMm, 6);
        Assert.Equal(-1200, vm.Elevation, 6);
        Assert.Equal(-2200, vm.ElevationOffsetMm, 6);   // 상대 모드 Offset = Void 표고 − 레벨
        Assert.Contains("본관 Void DN1500", vm.Status);
    }

    [Fact]
    public void ApplyTemplate_AirRoom_VoidAtOuterDiameter_UsesNominalDiameter()
    {
        // Void = 외경(DN300 주철관 326) → 관경 칸은 호칭지름 300(2026-10-29).
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(new ValveRoomOutline(CreateOutline(2000, 2000).Walls, null, new MainPipeVoid(new Point2D(1000, 1000), true, 326, 0)));
        vm.ValveRoomZone = "공기밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Equal(300, Assert.Single(vm.Edges).DiameterMm, 6);
        Assert.Equal(300, vm.DiameterMm, 6);
        Assert.DoesNotContain("맞는 DN 없음", vm.Status);
    }

    [Fact]
    public void ApplyTemplate_AirRoomWithFoundation_RunAndArrowsReachFoundationFaces()
    {
        // 공기밸브실만 IN/OUT 접점 = 기초 외측면(2026-10-29). 벽 내측 2000 → 기초 -500~2500 = 3000.
        var vm = CreateDrawingViewModel();
        var outline = new ValveRoomOutline(CreateOutline(2000, 2000).Walls, null, new MainPipeVoid(new Point2D(1000, 1000), true, 326, 0),
            [new OutlineFoundation(99, -500, -500, 2500, 2500)]);
        vm.ApplyOutline(outline);
        vm.ValveRoomZone = "공기밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        var edge = Assert.Single(vm.Edges);
        Assert.Equal(3000, edge.LengthMm, 1);
        var inArrow = vm.OutlineArrows.Single(x => x.Label == "IN");
        var outArrow = vm.OutlineArrows.Single(x => x.Label == "OUT");
        Assert.Equal(inArrow.X2, edge.X1, 1);
        Assert.Equal(outArrow.X1, edge.X2, 1);
        Assert.Contains(99L, vm.ToFamilySettings().OutlineFoundationElementIds!);
    }

    [Fact]
    public void ApplyTemplate_OtherRoomWithFoundation_StaysOnWallFaces()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(new ValveRoomOutline(CreateOutline(4000, 3000).Walls, null, null, [new OutlineFoundation(99, -500, -500, 4500, 3500)]));
        vm.ValveRoomZone = "제수밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Equal(4000, Assert.Single(vm.Edges).LengthMm, 1);
    }

    [Fact]
    public void ApplyTemplate_AirRoomWithoutVoid_KeepsElevationAndWarns()
    {
        var level = new LevelInfo(1, "1층", 1000);
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: [level]));
        vm.ApplyOutline(CreateOutline(2000, 2000));
        vm.ElevationLevel = level;
        vm.ElevationOffsetMm = 100;
        vm.ValveRoomZone = "공기밸브실";

        vm.ApplyTemplateCommand.Execute(null);

        Assert.Equal(100, vm.ElevationOffsetMm);
        Assert.Contains("Void를 못 찾아", vm.Status);
    }

    [Fact]
    public void ApplyTemplate_ReplacesExistingNetwork_AndUndoRestoresEdgesAndPorts()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.ApplyOutline(CreateOutline(4000, 3000));
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));
        vm.ValveRoomZone = "제수밸브실";

        vm.ApplyTemplateCommand.Execute(null);
        Assert.Equal(4000, Assert.Single(vm.Edges).LengthMm, 6);

        vm.UndoCommand.Execute(null);

        var edge = Assert.Single(vm.Edges);
        Assert.Equal(1000, edge.LengthMm, 6);
        Assert.Equal(["IN", "OUT"], vm.Ports.Select(x => x.Label));
        Assert.Equal(2, vm.OutlineArrows.Count);
    }

    [Fact]
    public void ApplyTemplate_ForgetsImportedSource_AndUndoBringsItBack()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.ApplyPickedPiping(CreateImport([11, 22]));
        Assert.True(vm.HasImportedSource);
        vm.ValveRoomZone = "이토밸브실";

        vm.ApplyTemplateCommand.Execute(null);
        Assert.False(vm.HasImportedSource);

        vm.UndoCommand.Execute(null);
        Assert.True(vm.HasImportedSource);
    }

    [Fact]
    public void ApplyTemplate_Twice_RecordsTwoUndoSteps_BecauseIdsAreFresh()
    {
        // SameNetwork는 Id를 비교하므로 같은 모양이라도 새로 만든 그래프는 "바뀐 것"이다(docs/152 § E-3 규약). 기록만 해 둔다.
        var vm = CreateDrawingViewModel();
        vm.ApplyOutline(CreateOutline(4000, 3000));
        vm.ValveRoomZone = "이토밸브실";

        vm.ApplyTemplateCommand.Execute(null);
        vm.ApplyTemplateCommand.Execute(null);

        vm.UndoCommand.Execute(null);
        Assert.Single(vm.Edges);
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Edges);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    /// <summary>원점에서 시작하는 직사각형 밸브실 외곽(내측면 기준).</summary>
    // ── 밸브실 유형별 격점번호 기본값(docs/177 § B) ──────────────────────────────────

    [Fact]
    public void ValveRoomZone_Set_FillsJunctionIdWithTypeDefault()
    {
        var vm = CreateViewModel();

        vm.ValveRoomZone = "제수밸브실";

        Assert.Equal("GV-01", vm.JunctionId);
    }

    /// <summary>자동값 위에는 자동값이 덮인다 — 사용자가 손댄 적이 없기 때문이다.</summary>
    [Fact]
    public void ValveRoomZone_ChangedTwice_ReplacesAutomaticJunctionId()
    {
        var vm = CreateViewModel();

        vm.ValveRoomZone = "제수밸브실";
        vm.ValveRoomZone = "이토밸브실";

        Assert.Equal("BV-01", vm.JunctionId);
    }

    [Fact]
    public void ValveRoomZone_Changed_KeepsUserTypedJunctionId()
    {
        var vm = CreateViewModel();

        vm.JunctionId = "V-3";
        vm.ValveRoomZone = "이토밸브실";

        Assert.Equal("V-3", vm.JunctionId);
    }

    /// <summary>사용자가 비운 것도 편집이다. 지웠는데 유형을 건드렸다고 다시 채워지면 안 된다.</summary>
    [Fact]
    public void ValveRoomZone_Changed_DoesNotRefillJunctionIdClearedByUser()
    {
        var vm = CreateViewModel();

        vm.JunctionId = "V-3";
        vm.JunctionId = string.Empty;
        vm.ValveRoomZone = "공기밸브실";

        Assert.Equal(string.Empty, vm.JunctionId);
    }

    /// <summary>접두가 없는 유형(빈 항목)은 기존 값을 지우지 않는다.</summary>
    [Fact]
    public void ValveRoomZone_ClearedToBlank_KeepsExistingJunctionId()
    {
        var vm = CreateViewModel();

        vm.ValveRoomZone = "제수밸브실";
        vm.ValveRoomZone = string.Empty;

        Assert.Equal("GV-01", vm.JunctionId);
    }

    [Fact]
    public void ToFamilySettings_StoresDistinctOutlineIds_ReferenceAndPorts()
    {
        var vm = CreateDrawingViewModel();
        var outline = CreateOutline(4000, 3000);
        outline = new ValveRoomOutline(outline.Walls, [
            new OutlineColumn(10, [new Point2D(100, 100)]),
            new OutlineColumn(10, [new Point2D(200, 200)], false)]);
        vm.ApplyOutline(outline);
        vm.ReferenceX = 1234;
        vm.ReferenceY = 5678;
        vm.ArrowFromTop = true;
        vm.Ports.Single(x => x.Flow == PortFlow.In).OffsetMm = 250;

        var settings = vm.ToFamilySettings();

        Assert.Equal([1L, 2, 3, 4], settings.OutlineWallElementIds);
        Assert.Equal([10L], settings.OutlineColumnElementIds);
        Assert.Equal(1234, settings.ReferenceX);
        Assert.Equal(5678, settings.ReferenceY);
        Assert.True(settings.ArrowFromTop);
        Assert.True(Assert.Single(settings.Ports!, x => x.Port.Flow == PortFlow.In).UserEdited);
    }

    [Fact]
    public void ApplyFamilySettings_RestoresEachPortUserEditedAndReference()
    {
        var vm = CreateViewModel();
        var inPort = PipePort.Create(PortFlow.In, OutlineSide.Left, 321, "IN");
        var outPort = PipePort.Create(PortFlow.Out, OutlineSide.Right, 654, "OUT");

        vm.ApplyFamilySettings(new PipeLayoutFamilySettings(
            ReferenceX: 1234, ReferenceY: 5678, ArrowFromTop: true,
            Ports: [new StoredPipePort(inPort, true), new StoredPipePort(outPort, false)]));

        Assert.Equal(1234, vm.ReferenceX);
        Assert.Equal(5678, vm.ReferenceY);
        Assert.True(vm.ArrowFromTop);
        Assert.True(vm.Ports.Single(x => x.Flow == PortFlow.In).UserEdited);
        Assert.False(vm.Ports.Single(x => x.Flow == PortFlow.Out).UserEdited);
    }

    [Fact]
    public void ApplyRestoredOutline_PreservesSavedReferenceAndPortOffsets()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyFamilySettings(new PipeLayoutFamilySettings(
            ReferenceX: 1200, ReferenceY: 900,
            Ports: [new StoredPipePort(PipePort.Create(PortFlow.In, OutlineSide.Left, 333, "IN"), true)]));

        vm.ApplyRestoredOutline(CreateOutline(4000, 3000), 0);

        Assert.Equal(1200, vm.ReferenceX);
        Assert.Equal(900, vm.ReferenceY);
        Assert.Equal(333, Assert.Single(vm.Ports).OffsetMm);
    }

    [Fact]
    public void ClearOutline_RemovesStoredOutlineIdsFromSettings()
    {
        var vm = CreateDrawingViewModel();
        vm.ApplyFamilySettings(new PipeLayoutFamilySettings(OutlineWallElementIds: [1, 2]));
        vm.ApplyOutline(CreateOutline(4000, 3000));

        vm.ClearOutlineCommand.Execute(null);

        Assert.Empty(vm.ToFamilySettings().OutlineWallElementIds!);
    }

    [Fact]
    public void PipeLayoutFamilySettings_WithoutLayoutFields_UsesCompatibleDefaults()
    {
        var settings = JsonSerializer.Deserialize<PipeLayoutFamilySettings>("{}")!;

        Assert.Empty(settings.OutlineWallElementIds ?? []);
        Assert.Empty(settings.OutlineColumnElementIds ?? []);
        Assert.Null(settings.Ports);
    }

    private static ValveRoomOutline CreateOutline(double widthMm, double heightMm, params string[] zones)
    {
        const double thickness = 200;
        OutlineWall Wall(long id, Point2D a, Point2D b, Point2D oa, Point2D ob)
            => new(id, a, b, oa, ob, thickness, zones.Length >= id ? zones[id - 1] : string.Empty);
        var bl = new Point2D(0, 0);
        var br = new Point2D(widthMm, 0);
        var tr = new Point2D(widthMm, heightMm);
        var tl = new Point2D(0, heightMm);
        return new ValveRoomOutline(
        [
            Wall(1, bl, br, new Point2D(0, -thickness), new Point2D(widthMm, -thickness)),
            Wall(2, br, tr, new Point2D(widthMm + thickness, 0), new Point2D(widthMm + thickness, heightMm)),
            Wall(3, tr, tl, new Point2D(widthMm, heightMm + thickness), new Point2D(0, heightMm + thickness)),
            Wall(4, tl, bl, new Point2D(-thickness, heightMm), new Point2D(-thickness, 0)),
        ]);
    }

    private static PipeLayoutViewModel CreateViewModel(IEnumerable<string>? accessoryNames = null)
        => PipeLayoutTestDefaults.UseAbsoluteElevation(new(new StubElementTypeQueryRepo(accessoryNames: accessoryNames)));

    [Fact]
    public void ElevationMode_DefaultsToRelativeAndRequiresLevel()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo());

        Assert.Equal(PipeElevationMode.Relative, vm.ElevationMode);
        Assert.Contains("기준 레벨 선택 필요", vm.ValidationSummary);
    }

    [Fact]
    public void RelativeElevation_UsesLevelAndOffset_AndRetainsOffsetOnLevelChange()
    {
        var lower = new LevelInfo(1, "1층", 1200);
        var upper = new LevelInfo(2, "2층", 4200);
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: [lower, upper]));
        vm.ElevationLevel = lower;
        vm.ElevationOffsetM = 0.8;

        Assert.Equal(2000, vm.Elevation);
        vm.ElevationLevel = upper;
        Assert.Equal(5000, vm.Elevation);
        Assert.Equal(0.8, vm.ElevationOffsetM);
    }

    [Fact]
    public void AbsoluteToRelative_PreservesElevationAndReversesOffset()
    {
        var level = new LevelInfo(1, "1층", 1200);
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: [level]));
        vm.IsAbsoluteElevation = true;
        vm.ElevationM = 2;
        vm.ElevationLevel = level;
        vm.IsRelativeElevation = true;

        Assert.Equal(2000, vm.Elevation);
        Assert.Equal(0.8, vm.ElevationOffsetM);
    }

    [Fact]
    public void ApplyOutline_AutoSelectsMostCommonBaseLevelAndReportsMixedLevels()
    {
        var levels = new[] { new LevelInfo(1, "1층", 0), new LevelInfo(2, "2층", 3000) };
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: levels));
        var baseOutline = CreateOutline(4000, 3000);
        var walls = baseOutline.Walls.Select((wall, index) => wall with { BaseLevelId = index == 3 ? 2 : 1 }).ToList();

        vm.ApplyOutline(new ValveRoomOutline(walls));

        Assert.Equal(1, vm.ElevationLevel?.Id);
        Assert.Contains("벽 하단 레벨: 1층", vm.ElevationLevelHint);
        Assert.Contains("하단 레벨 혼재 — 확인 필요", vm.Status);
    }

    [Fact]
    public void ApplyPickedPiping_ReversesRelativeOffset()
    {
        var level = new LevelInfo(1, "1층", 1000);
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: [level]));
        vm.ElevationLevel = level;
        vm.ApplyPickedPiping(CreateImport([11]));

        Assert.Equal(200, vm.ElevationOffsetMm);
    }

    [Fact]
    public void ElevationSettings_RoundTripRestoresModeLevelAndOffset()
    {
        var level = new LevelInfo(9, "기준", 1500);
        var source = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: [level]));
        source.ElevationLevel = level;
        source.ElevationOffsetMm = 800;
        var saved = source.ToFamilySettings();
        var restored = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: [level with { ElevationMm = 1700 }]));

        restored.ApplyFamilySettings(saved);

        Assert.Equal(PipeElevationMode.Relative, restored.ElevationMode);
        Assert.Equal(9, restored.ElevationLevel?.Id);
        Assert.Equal(800, restored.ElevationOffsetMm);
        Assert.Equal(2500, restored.Elevation);
    }

    [Fact]
    public void ApplyRestoredOutline_PreservesSavedLevel_AndStaleSavedIdStaysUnselected()
    {
        var levels = new[] { new LevelInfo(1, "1층", 0), new LevelInfo(2, "2층", 3000) };
        var outline = CreateOutline(4000, 3000);
        var walls = outline.Walls.Select(wall => wall with { BaseLevelId = 1 }).ToList();
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: levels));
        vm.ApplyFamilySettings(new PipeLayoutFamilySettings(ElevationMode: PipeElevationMode.Relative, ElevationLevelId: 2, ElevationOffsetMm: 400));
        vm.ApplyRestoredOutline(new ValveRoomOutline(walls), 0);

        Assert.Equal(2, vm.ElevationLevel?.Id);
        Assert.Equal(3400, vm.Elevation);

        var stale = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: levels));
        stale.ApplyFamilySettings(new PipeLayoutFamilySettings(ElevationMode: PipeElevationMode.Relative, ElevationLevelId: 99, ElevationOffsetMm: 400));
        stale.ApplyRestoredOutline(new ValveRoomOutline(walls), 0);

        Assert.Null(stale.ElevationLevel);
        Assert.Contains("저장된 기준 레벨 없음 — 다시 선택", stale.Status);
        // 콤보 비었으면 상태줄도 미선택. 벽 레벨명(1층)을 기준 레벨로 쓰면 안 됨.
        Assert.Contains("기준 레벨 미선택", stale.Status);
        Assert.DoesNotContain("기준 레벨 1층", stale.Status);
    }

    [Fact]
    public void ElevationOffsetMm_RaisesMeterPropertyChanged()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: [new LevelInfo(1, "1층", 0)]));
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ElevationOffsetMm = 1200;

        Assert.Contains(nameof(PipeLayoutViewModel.ElevationOffsetM), changed);
    }

    [Fact]
    public void ApplyFamilySettings_WithoutElevationFields_KeepsCurrentElevationState()
    {
        var levels = new[] { new LevelInfo(1, "1층", 0), new LevelInfo(2, "2층", 3000) };
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: levels));
        vm.ElevationLevel = levels[1];
        vm.ElevationOffsetMm = 500;

        vm.ApplyFamilySettings(new PipeLayoutFamilySettings());

        Assert.Equal(PipeElevationMode.Relative, vm.ElevationMode);
        Assert.Equal(2, vm.ElevationLevel?.Id);
        Assert.Equal(500, vm.ElevationOffsetMm);
        Assert.Equal(3500, vm.Elevation);
    }

    [Fact]
    public void ElevationSettings_MissingSavedLevelRequestsReselection()
    {
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(levels: []));

        vm.ApplyFamilySettings(new PipeLayoutFamilySettings(ElevationMode: PipeElevationMode.Relative, ElevationLevelId: 9, ElevationOffsetMm: 800));

        Assert.Null(vm.ElevationLevel);
        Assert.Contains("저장된 기준 레벨 없음 — 다시 선택", vm.Status);
    }

    private static PipeLayoutViewModel CreateParameterSelectionViewModel()
    {
        var parameterNames = new Dictionary<string, IEnumerable<string>>
        {
            ["밸브A : DN100"] = ["자동 DN", "사용자 DN"],
            ["밸브A : DN150"] = ["자동 DN", "사용자 DN"],
            ["밸브B : DN100"] = ["대체 DN"]
        };
        return PipeLayoutTestDefaults.UseAbsoluteElevation(new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            accessoryNames: ["밸브A : DN100", "밸브A : DN150", "밸브B : DN100"],
            parameterNamesByFamily: parameterNames)));
    }

    private sealed class StubElementTypeQueryRepo : IElementTypeQueryRepo
    {
        private readonly IReadOnlyList<string> _segmentParameterNames;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _parameterNamesByFamily;
        private readonly IReadOnlyList<string> _accessoryNames;
        private readonly IReadOnlyList<string> _fittingNames;
        private readonly IReadOnlyList<LevelInfo> _levels;
        private readonly IReadOnlyDictionary<string, IEnumerable<string>>? _numericParameterNamesByFamily;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto>>? _typeLengthParametersByType;

        public StubElementTypeQueryRepo(
            IEnumerable<string>? segmentParameterNames = null,
            IEnumerable<string>? accessoryNames = null,
            IEnumerable<string>? fittingNames = null,
            IReadOnlyDictionary<string, IEnumerable<string>>? parameterNamesByFamily = null,
            IReadOnlyDictionary<string, IEnumerable<string>>? numericParameterNamesByFamily = null,
            IReadOnlyDictionary<string, IReadOnlyList<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto>>? typeLengthParametersByType = null,
            IEnumerable<LevelInfo>? levels = null)
        {
            _levels = levels?.ToList() ?? [];
            _numericParameterNamesByFamily = numericParameterNamesByFamily;
            _typeLengthParametersByType = typeLengthParametersByType;
            _segmentParameterNames = segmentParameterNames?.ToList() ?? ["길이", "DN"];
            _parameterNamesByFamily = parameterNamesByFamily?.ToDictionary(
                x => x.Key, x => (IReadOnlyList<string>)x.Value.ToList())
                ?? new Dictionary<string, IReadOnlyList<string>>();
            _accessoryNames = accessoryNames?.ToList() ?? ["밸브 : DN100"];
            _fittingNames = fittingNames?.ToList()
                ?? ["직관 : DN100", "단관 : DN100", "곡관90 : DN100", "곡관45 : DN100", "T형 : DN100"];
        }

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
        public IReadOnlyList<LevelInfo> GetLevels() => _levels;
        public IEnumerable<string> GetPipeAccessoryTypeNames() => _accessoryNames;
        public IEnumerable<string> GetPipeFittingTypeNames() => _fittingNames;
        public IEnumerable<string> GetPipeAccessoryInstanceParameterNames(string familyTypeName)
            => _parameterNamesByFamily.TryGetValue(familyTypeName, out var names) ? names : _segmentParameterNames;
        /// <summary>실수형 후보를 따로 주지 않으면 전체 목록과 같다(docs/233 § B-1).</summary>
        public IEnumerable<string> GetPipeAccessoryNumericInstanceParameterNames(string familyTypeName)
            => _numericParameterNamesByFamily is not null && _numericParameterNamesByFamily.TryGetValue(familyTypeName, out var numeric)
                ? numeric
                : GetPipeAccessoryInstanceParameterNames(familyTypeName);
        public IReadOnlyList<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto> GetPipeAccessoryTypeLengthParameters(string familyTypeName)
            => _typeLengthParametersByType is not null && _typeLengthParametersByType.TryGetValue(familyTypeName, out var parameters) ? parameters : [];
        public IEnumerable<string> GetGenericModelTypeNames() => [];
        public IEnumerable<string> GetFoundationTypeNames() => [];
        public IEnumerable<string> GetBeamInstanceParameterNames(string beamTypeName) => [];
        public double GetStructuralFramingSectionWidthMm(string familyTypeName) => 400;
        public (double WidthX, double DepthY) GetStructuralColumnSectionSizeMm(string familyTypeName) => (400, 400);
        public void InvalidateFamilyCaches() { }
    }
}
