using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.ViewModels.Modeling;
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

    [Fact]
    public void ConfirmedEdge_SubLinesFollowDiameter()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.DiameterMm = 200;
        Draw(vm, new Point2D(0, 0), new Point2D(1000, 0));

        var edge = Assert.Single(vm.Edges);
        var initialOffset = Math.Abs(edge.SubAY1 - edge.Y1);
        Assert.Equal(100 * vm.Transform.PixelsPerMillimeter, initialOffset, 6);

        vm.DiameterMm = 400;

        edge = Assert.Single(vm.Edges);
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
        Assert.Equal("선택한 항목을 삭제했습니다.", vm.Status);
    }

    [Fact]
    public void DrawingLengthLimit_WithinLimit_CreatesEdgeAtClickedPoint()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);

        Draw(vm, new(1000, 1000), new(4000, 1000));

        var edge = Assert.Single(vm.Edges);
        Assert.Equal(3000, edge.LengthMm, 6);
        AssertPoint(new(4000, 1000), EdgeEnd(vm, edge));
    }

    [Fact]
    public void DrawingLengthLimit_OverLimitWithAngleSnap_ClampsLengthAndPreservesDirection()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        var start = new Point2D(1000, 1000);
        var raw = new Point2D(9000, 9000);
        vm.HandleCanvasClick(vm.Transform.ToScreen(start));

        vm.HandleCanvasMove(vm.Transform.ToScreen(raw));

        Assert.Equal("정척 길이 6,000mm를 넘을 수 없어 끝점을 제한했습니다.", vm.Status);
        Assert.Equal("6.000 m", vm.PreviewLength);

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 2000)));
        Assert.Equal("끝점을 클릭하면 배관이 확정됩니다.", vm.Status);

        vm.HandleCanvasClick(vm.Transform.ToScreen(raw));

        var edge = Assert.Single(vm.Edges);
        var end = EdgeEnd(vm, edge);
        Assert.Equal(vm.StraightLengthMm, edge.LengthMm, 6);
        Assert.Equal(end.X - start.X, end.Y - start.Y, 6);
    }

    [Fact]
    public void DrawingLengthLimit_OverLimitWithoutAngleSnap_ClampsLengthAndPreservesDirection()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.UseAngleSnap = false;
        var start = new Point2D(1000, 1000);
        var raw = new Point2D(8000, 5000);

        Draw(vm, start, raw);

        var edge = Assert.Single(vm.Edges);
        var end = EdgeEnd(vm, edge);
        Assert.Equal(vm.StraightLengthMm, edge.LengthMm, 6);
        Assert.Equal(0, (end.X - start.X) * (raw.Y - start.Y) - (end.Y - start.Y) * (raw.X - start.X), 6);
    }

    [Fact]
    public void DrawingLengthLimit_OverLimitExistingEndpoint_IsFilteredAndClamped()
    {
        var vm = CreateDrawingViewModel();
        vm.StraightLengthMm = 20000;
        Draw(vm, new(10000, 0), new(11000, 0));
        vm.StraightLengthMm = 6000;
        vm.SnapNearest = false;
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(0, 0)));

        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(10050, 0)));

        Assert.False(vm.IsSnapMarkerVisible);

        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(10050, 0)));

        var edge = Assert.Single(vm.Edges.Where(x => x.LengthMm > 5000));
        Assert.Equal(6000, edge.LengthMm, 6);
        AssertPoint(new(6000, 0), EdgeEnd(vm, edge));
    }

    [Fact]
    public void DrawingLengthLimit_WithinLimitExistingEndpoint_SnapsExactly()
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
    public void DrawingLengthLimit_CustomStraightLength_UsesUpdatedLimit()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.StraightLengthMm = 3000;

        Draw(vm, new(1000, 1000), new(6000, 1000));

        Assert.Equal(3000, Assert.Single(vm.Edges).LengthMm, 6);
    }

    [Fact]
    public void DrawingLengthLimit_PreviewAndConfirmedEndpoint_AreIdentical()
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
    public void DrawingLengthLimit_FirstClick_IsNotLimited()
    {
        var vm = CreateDrawingViewModel();
        DisableOsnap(vm);
        vm.StraightLengthMm = 3000;
        var start = new Point2D(20000, 20000);

        vm.HandleCanvasClick(vm.Transform.ToScreen(start));

        Assert.True(vm.IsDrawing);
        AssertPoint(start, vm.Transform.ToModel(new Point(vm.PreviewX1, vm.PreviewY1)));
        Assert.Empty(vm.Edges);
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
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        Draw(vm, new(-1000, 0), new(1000, 0));
        Draw(vm, new(0, 0), new(1000, 1000));

        Assert.Contains("직교 T가 아닌 3방향 분기 1개", vm.ValidationSummary);
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
    public void FamilyAndParameterDefaults_PreferDhPrefixAndConfiguredKeywords()
    {
        var parameterNames = new Dictionary<string, IEnumerable<string>>
        {
            ["DH 직관 : DN100"] = ["직경", "공칭지름", "길이"],
            ["DH 곡관 : DN100"] = ["지름", "각도", "공칭구경"],
            ["DH Y형관 : DN100"] = ["호칭", "공칭구경"],
        };
        var vm = new PipeLayoutViewModel(new StubElementTypeQueryRepo(
            fittingNames: ["직관 : DN100", "DH 직관 : DN100", "곡관 : DN100", "DH 곡관 : DN100", "T형관 : DN100", "DH Y형관 : DN100"],
            parameterNamesByFamily: parameterNames));

        Assert.Equal("DH 직관 : DN100", vm.SegmentFamilyTypeName);
        Assert.Equal("DH 곡관 : DN100", vm.BendFamilyTypeName);
        Assert.Equal("DH Y형관 : DN100", vm.TeeFamilyTypeName);
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
        vm.ValveDiameterParameterName = "밸브 직경";
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = value => request = value;

        vm.CreateModelCommand.Execute(null);

        Assert.Equal("곡관 직경", request!.SegmentFamilies!.BendDiameterParameterName);
        Assert.Equal("T형 직경", request.SegmentFamilies.TeeDiameterParameterName);
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
        Assert.Equal(
            ["직관 : DN100", "단관 : DN100", "곡관90 : DN100", "곡관45 : DN100", "T형 : DN100"],
            vm.SegmentFamilyTypeNames);
        Assert.Contains(vm.SegmentFamilyTypeName, vm.SegmentFamilyTypeNames);
        Assert.Contains(vm.BendFamilyTypeName, vm.SegmentFamilyTypeNames);
        Assert.Contains(vm.TeeFamilyTypeName, vm.SegmentFamilyTypeNames);
    }

    [Fact]
    public void ValveFamilySelection_FiltersTypes_AndKeepsFullPlacementKey()
    {
        var vm = CreateViewModel(["게이트밸브 300A : 표준", "게이트밸브 300A : 전동식", "버터플라이밸브 300A : 표준"]);

        vm.SelectedValveFamilyName = "게이트밸브 300A";

        Assert.Equal(["게이트밸브 300A : 표준", "게이트밸브 300A : 전동식"], vm.FamilyTypeNames);
        Assert.Null(vm.SelectedFamilyTypeName);
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
        Assert.Contains("관 패밀리를 선택하세요.", vm.ValidationSummary);

        vm.SegmentFamilyTypeName = "직관 : DN100";
        vm.LengthParameterName = null;
        Assert.Contains("관 길이 파라미터를 선택하세요.", vm.ValidationSummary);
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

    private static PipeLayoutViewModel CreateDrawingViewModel()
    {
        var vm = CreateViewModel();
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

    /// <summary>원점에서 시작하는 직사각형 밸브실 외곽(내측면 기준).</summary>
    private static ValveRoomOutline CreateOutline(double widthMm, double heightMm)
    {
        const double thickness = 200;
        OutlineWall Wall(long id, Point2D a, Point2D b, Point2D oa, Point2D ob) => new(id, a, b, oa, ob, thickness);
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

    private static PipeLayoutViewModel CreateViewModel(IEnumerable<string>? accessoryNames = null) => new(new StubElementTypeQueryRepo(accessoryNames: accessoryNames));

    private sealed class StubElementTypeQueryRepo : IElementTypeQueryRepo
    {
        private readonly IReadOnlyList<string> _segmentParameterNames;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _parameterNamesByFamily;
        private readonly IReadOnlyList<string> _accessoryNames;
        private readonly IReadOnlyList<string> _fittingNames;

        public StubElementTypeQueryRepo(
            IEnumerable<string>? segmentParameterNames = null,
            IEnumerable<string>? accessoryNames = null,
            IEnumerable<string>? fittingNames = null,
            IReadOnlyDictionary<string, IEnumerable<string>>? parameterNamesByFamily = null)
        {
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
        public IEnumerable<string> GetBeamTypeNames() => [];
        public IEnumerable<string> GetAdaptiveComponentTypeNames() => [];
        public IEnumerable<string> GetAdaptiveInstanceParameterNames(string familyTypeName) => [];
        public int GetAdaptiveBendPointCount(string familyTypeName) => -1;
        public IEnumerable<string> GetPipingSystemTypeNames() => ["급수"];
        public IEnumerable<string> GetPipeTypeNames() => ["배관"];
        public IEnumerable<string> GetLevelNames() => ["1층"];
        public IEnumerable<string> GetPipeAccessoryTypeNames() => _accessoryNames;
        public IEnumerable<string> GetPipeFittingTypeNames() => _fittingNames;
        public IEnumerable<string> GetPipeAccessoryInstanceParameterNames(string familyTypeName)
            => _parameterNamesByFamily.TryGetValue(familyTypeName, out var names) ? names : _segmentParameterNames;
        public IEnumerable<string> GetGenericModelTypeNames() => [];
        public IEnumerable<string> GetFoundationTypeNames() => [];
        public IEnumerable<string> GetBeamInstanceParameterNames(string beamTypeName) => [];
    }
}
