using DHBIMWATER.Application.Interfaces;
using System.Windows;
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
    private const string GateFamily = "DH_게이트 밸브";

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
        // 밸브는 패밀리명만, 관경 0 = 모델 생성 때 밸브부관 B가 정한다(docs/244 § D-2).
        Assert.Equal(new BypassSetSpec(GateFamily, 0, 1), set.Bypass);
        Assert.NotNull(set.BypassLine);
        Assert.Contains("세트 1", vm.NetworkSummary);
    }

    [Fact]
    public void SelectingBypassSet_AllowsDragDelete_AndUsesDistinctColor()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.Mode = PipeLayoutMode.Selection;
        var fitting = Assert.Single(vm.BypassSetItems);

        Assert.True(vm.TryBeginFittingDrag(new Point(fitting.X, fitting.Y), fitting.Id));
        Assert.Contains(fitting.Id, vm.SelectedFittingIds);
        Assert.Equal("#00B8D4", vm.InlineFittings.Single().Fill);
        vm.CommitDrag(new Point(fitting.X + 20, fitting.Y));
        Assert.NotEqual(fitting.T, vm.BypassSetItems.Single().T);

        vm.DeleteSelectedFittingCommand.Execute(null);

        Assert.Empty(vm.BypassSetItems);
    }

    [Fact]
    public void CrossingSelection_IntersectsBypassSetDrawingBounds()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.Mode = PipeLayoutMode.Selection;
        var fitting = Assert.Single(vm.BypassSetItems);
        var bounds = fitting.BypassMarkers!.Bounds;
        var start = new Point(bounds.Right + 20, bounds.Top - 2);
        var end = new Point(bounds.Right - 1, bounds.Bottom + 2);

        vm.BeginSelectionBox(start);
        vm.CommitSelectionBox(end);

        Assert.Contains(fitting.Id, vm.SelectedFittingIds);
    }

    [Fact]
    public void WindowSelection_ContainsBypassSetDrawingBounds()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.Mode = PipeLayoutMode.Selection;
        var fitting = Assert.Single(vm.BypassSetItems);
        var bounds = fitting.BypassMarkers!.Bounds;

        vm.BeginSelectionBox(new Point(bounds.Left - 5, bounds.Top - 5));
        vm.CommitSelectionBox(new Point(bounds.Right + 5, bounds.Bottom + 5));

        Assert.Contains(fitting.Id, vm.SelectedFittingIds);
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
    public void NewSet_ShowsAutoDiameter_InSummaryAndTemplateText()
    {
        // docs/244 § D-2 — 바이패스 관경 입력이 없어졌다. 새 세트는 관경 0(자동)이며 "관경 자동"으로 보이고, 관경 검사 대상이 아니다.
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));

        // docs/256 — 관경은 핸드북 규격표가 정한다. 기본 관종(덕타일주철관)은 DN1200까지라 본관 1650은 규격 없음이다.
        var set = Assert.Single(vm.BypassSetItems);
        Assert.Contains("핸드북 규격 없음", set.BypassSummary);
        Assert.Contains("핸드북 규격 없음 — 생성 시 건너뜀", vm.BuildTemplateText());
        Assert.DoesNotContain("바이패스 관경 ≥ 본관", vm.ValidationSummary);

        // 강관 F12는 1650×300 규격이 있다.
        vm.PipeMaterial = DHBIMWATER.Core.Piping.PipeMaterial.Steel;
        Assert.Contains("DN300(핸드북)", vm.BypassSetItems.Single().BypassSummary);
        Assert.Contains("DN300(핸드북)", vm.BuildTemplateText());
        // F20은 1500까지다.
        vm.SteelFittingClassLabel = "F20";
        Assert.Contains("핸드북 규격 없음", vm.BypassSetItems.Single().BypassSummary);
    }

    [Fact]
    public void SteelFittingClass_EnabledOnlyForSteel_AndRoundTripsThroughSettings()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsSteelFittingClassEnabled);
        Assert.Equal("F12", vm.SteelFittingClassLabel);

        vm.PipeMaterial = DHBIMWATER.Core.Piping.PipeMaterial.Steel;
        Assert.True(vm.IsSteelFittingClassEnabled);
        vm.SteelFittingClassLabel = "F15";
        vm.SteelFittingClassLabel = "없는등급";   // 못 되돌리는 라벨은 무시한다.
        Assert.Equal(DHBIMWATER.Core.Piping.SteelFittingClass.F15, vm.SteelFittingClass);

        var settings = vm.ToFamilySettings();
        Assert.Equal(DHBIMWATER.Core.Piping.SteelFittingClass.F15, settings.SteelFittingClass);

        var restored = CreateViewModel();
        restored.ApplyFamilySettings(settings);
        Assert.Equal(DHBIMWATER.Core.Piping.SteelFittingClass.F15, restored.SteelFittingClass);
    }

    [Fact]
    public void DuctileMain300_ShowsHandbookBranch100()
    {
        var vm = CreateMainPipeViewModel(300);
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));

        var set = Assert.Single(vm.BypassSetItems);
        Assert.Equal(300, set.MainDiameterMm);
        Assert.Contains("DN100(핸드북)", set.BypassSummary);
    }

    [Fact]
    public void PlacingWithoutBypassValveFamily_IsRejected()
    {
        var vm = CreateMainPipeViewModel();
        vm.BypassValveFamilyName = null;
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));

        Assert.Empty(vm.BypassSetItems);
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
        Assert.Equal(GateFamily, set.Bypass!.BypassValveFamilyTypeName);
        Assert.Equal(0, set.Bypass.BypassDiameterMm);
    }

    [Fact]
    public void ValveBendFamilyWithoutKeyword_WarnsReimportLoss()
    {
        // 되가져오기는 패밀리명의 '밸브부관'으로 A·B를 가린다 — 키워드 없는 패밀리는 만들 수는 있어도 세트로 돌아오지 않는다.
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.IsPlacingBypassSet = false;
        vm.ValveTeeFamilyTypeName = "DH_밸브부관A : 1650A";
        Assert.DoesNotContain("되가져오기 시", vm.ValidationSummary);

        vm.ValveTeeFamilyTypeName = "DH_T형관 : 1650A";
        vm.ValveBendFamilyTypeName = "DH_곡관 : 300A";

        Assert.Contains("밸브부관 A 패밀리명에 '밸브부관'가 없어", vm.ValidationSummary);
        Assert.Contains("밸브부관 B 패밀리명에 '밸브부관'가 없어", vm.ValidationSummary);
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
        // 되가져온 세트는 모델 실제값(300)을 표시용으로 든다(docs/244 § D-2).
        Assert.Contains("D300", set.BypassSummary);
        Assert.Contains("DN300", vm.BuildTemplateText());
        Assert.DoesNotContain("바이패스 밸브 패밀리가 비어 있는", vm.ValidationSummary);
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
        Assert.Contains("바이패스 밸브 패밀리가 비어 있는 세트 1개", vm.ValidationSummary);

        vm.ApplyBypassValvesCommand.Execute(vm.BypassSetItems.Single());

        // [밸브 적용]은 패밀리명·관경 0(자동)을 넣는다(docs/244 § D-2).
        var set = vm.BypassSetItems.Single();
        Assert.Equal(GateFamily, set.Bypass!.BypassValveFamilyTypeName);
        Assert.Equal(0, set.Bypass.BypassDiameterMm);
        Assert.Equal(-1, set.Bypass.Side);
        Assert.Contains("핸드북 규격 없음", set.BypassSummary);   // 덕타일주철관 DN1650은 표 범위 밖(docs/256)
        Assert.DoesNotContain("바이패스 밸브 패밀리가 비어 있는", vm.ValidationSummary);
    }

    // ── docs/255 — 밸브 2개 + 사이 단관 ──────────────────────────────────────

    private const string ButterflyFamily = "DH_버터플라이 밸브";

    [Fact]
    public void SecondValve_DefaultsToFirstValveFamily_AndSingleValveIsDefault()
    {
        var vm = CreateViewModel();

        Assert.True(vm.IsBypassSingleValve);
        Assert.Equal(GateFamily, vm.BypassSecondValveFamilyName);
        Assert.Equal(BypassSet.DefaultSpoolLengthMm, vm.BypassSpoolLengthMm);
    }

    [Fact]
    public void PlacingTwoValveSet_SendsSecondValveAndSpool_AndSwapExchangesThem()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsBypassTwoValves = true;
        vm.BypassSecondValveFamilyName = ButterflyFamily;
        vm.BypassSpoolLengthMm = 400;
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.IsPlacingBypassSet = false;

        var set = Assert.Single(vm.BypassSetItems);
        Assert.Equal(new BypassSetSpec(GateFamily, 0, 1, ButterflyFamily, 400), set.Bypass);
        Assert.True(set.HasTwoBypassValves);
        Assert.Contains("게이트 밸브 / 버터플라이 밸브 · 단관 400mm", set.BypassSummary);
        Assert.Contains($"{GateFamily} → {ButterflyFamily} · 단관 400mm", vm.BuildTemplateText());

        vm.SwapBypassValvesCommand.Execute(set);

        Assert.Equal(new BypassSetSpec(ButterflyFamily, 0, 1, GateFamily, 400), vm.BypassSetItems.Single().Bypass);
        vm.UndoCommand.Execute(null);
        Assert.Equal(new BypassSetSpec(GateFamily, 0, 1, ButterflyFamily, 400), vm.BypassSetItems.Single().Bypass);
    }

    [Fact]
    public void ApplyBypassValves_SwitchesBetweenOneAndTwoValves()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, -50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, -50)));
        vm.IsPlacingBypassSet = false;
        Assert.False(vm.BypassSetItems.Single().HasTwoBypassValves);

        vm.IsBypassTwoValves = true;
        vm.BypassSpoolLengthMm = 0;   // 직결 허용(docs/255 G-2)
        vm.ApplyBypassValvesCommand.Execute(vm.BypassSetItems.Single());
        Assert.Equal(new BypassSetSpec(GateFamily, 0, -1, GateFamily, 0), vm.BypassSetItems.Single().Bypass);

        vm.IsBypassSingleValve = true;
        vm.ApplyBypassValvesCommand.Execute(vm.BypassSetItems.Single());
        Assert.Equal(new BypassSetSpec(GateFamily, 0, -1), vm.BypassSetItems.Single().Bypass);
    }

    [Fact]
    public void TwoValvesWithoutSecondFamily_IsRejected()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsBypassTwoValves = true;
        vm.BypassSecondValveFamilyName = null;
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));

        Assert.Empty(vm.BypassSetItems);
        Assert.Equal("바이패스 밸브 2 패밀리 선택 필요", vm.Status);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-10)]
    public void SpoolLength_BelowMinSegment_IsRejectedByPanel(double spool)
    {
        var vm = CreateViewModel();

        Assert.Throws<ArgumentOutOfRangeException>(() => vm.BypassSpoolLengthMm = spool);
        Assert.Equal(BypassSet.DefaultSpoolLengthMm, vm.BypassSpoolLengthMm);
    }

    [Fact]
    public void ApplyPickedPiping_TwoValveSet_OrdersValvesByEdgeDirection_AndFlagsShortSpool()
    {
        // 같은 세트를 밸브 순서만 바꿔 두 번 가져와도 V1(엣지 시작 쪽)은 위치로 정해져 같아야 한다(docs/255 § D-5).
        var first = ImportTwoValveSet(GateValve300, new Point2D(600, 700), "DH_버터플라이 밸브 : 300A", new Point2D(1400, 700), 400);
        var second = ImportTwoValveSet("DH_버터플라이 밸브 : 300A", new Point2D(1400, 700), GateValve300, new Point2D(600, 700), 400);

        var a = first.BypassSetItems.Single().Bypass!;
        var b = second.BypassSetItems.Single().Bypass!;
        Assert.True(a.HasTwoValves);
        Assert.Equal(a.BypassValveFamilyTypeName, b.BypassValveFamilyTypeName);
        Assert.Equal(a.SecondValveFamilyTypeName, b.SecondValveFamilyTypeName);
        Assert.Equal(400, a.SpoolLengthMm);
        Assert.DoesNotContain("단관이", first.ValidationSummary);

        var tooShort = ImportTwoValveSet(GateValve300, new Point2D(600, 700), "DH_버터플라이 밸브 : 300A", new Point2D(1400, 700), 5);
        Assert.Contains("바이패스 밸브 사이 단관이 10mm 미만인 세트 1개", tooShort.ValidationSummary);
    }

    private static PipeLayoutViewModel ImportTwoValveSet(string valve0, Point2D position0, string valve1, Point2D position1, double spoolMm)
    {
        var vm = CreateViewModel();
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.ApplyPickedPiping(new PipeLayoutImport(
            [new ImportedEdge(new Point2D(-200, 0), new Point2D(2200, 0), 1650)],
            [new ImportedFitting(0, "", 0.5)
            {
                Bypass = new ImportedBypass(valve0, 300, new Point2D(0, 1), [1, 2, 3, 4, 22, 25])
                {
                    SecondValveFamilyTypeName = valve1, SpoolLengthMm = spoolMm, ValvePositions = [position0, position1]
                }
            }],
            0, [1, 2, 3, 4, 22, 25], []));
        return vm;
    }

    // ── docs/259 — 바이패스 세트 배치 기준점 OSNAP ─────────────────────────────

    [Fact]
    public void BypassPlacement_SnapsToMidpoint_InsteadOfGrid()
    {
        // 본관 4050 — 중간점 2025는 격자(10mm)로는 나올 수 없는 자리다(커서 2031 → 격자 2030).
        var vm = CreateLineViewModel(new Point2D(0, 0), new Point2D(4050, 0));
        vm.SnapMidpoint = true;
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2031, 50)));

        Assert.True(vm.IsSnapMarkerVisible);
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2031, 50)));

        Assert.Equal(0.5, vm.BypassSetItems.Single().T, 6);
    }

    [Fact]
    public void ValvePlacement_StillUsesGrid_WhenMidpointSnapIsOn()
    {
        var vm = CreateLineViewModel(new Point2D(0, 0), new Point2D(4050, 0));
        vm.SnapMidpoint = true;
        vm.SelectedFamilyTypeName = MainValve1650;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2031, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2031, 50)));

        Assert.Equal(2030.0 / 4050, vm.InlineFittings.Single().T, 6);
    }

    [Fact]
    public void BypassPlacement_OffPipeSnap_IsProjectedOntoPipe()
    {
        // 기준점 (0,0)은 배관(y=40) 밖이다 — 배관 위 수직 투영점 (0,40)에 세트 중심이 온다.
        var vm = CreateLineViewModel(new Point2D(-1000, 40), new Point2D(3000, 40));
        vm.SnapReferencePoint = true;
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(13, 70)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(13, 70)));

        Assert.Equal(0.25, vm.BypassSetItems.Single().T, 6);
    }

    [Fact]
    public void BypassPlacement_WithoutSnap_KeepsGrid()
    {
        var vm = CreateLineViewModel(new Point2D(0, 0), new Point2D(4050, 0));
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2031, 50)));

        Assert.False(vm.IsSnapMarkerVisible);
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2031, 50)));

        Assert.Equal(2030.0 / 4050, vm.BypassSetItems.Single().T, 6);
    }

    /// <summary>스냅을 모두 끈 채 선 하나를 그린다. 테스트가 필요한 스냅만 켠다.</summary>
    private static PipeLayoutViewModel CreateLineViewModel(Point2D start, Point2D end)
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
        vm.HandleCanvasClick(vm.Transform.ToScreen(start));
        vm.HandleCanvasClick(vm.Transform.ToScreen(end));
        return vm;
    }

    [Fact]
    public void GrabbingBypassMarker_DragsByOffset_WithoutCenterJump()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.Mode = PipeLayoutMode.Selection;
        var fitting = Assert.Single(vm.BypassSetItems);
        var marker = fitting.BypassMarkers!.Bounds;
        var grab = new Point(marker.Left + 2, marker.Top + 2);   // 세트 중심에서 먼 표식(A·B)을 잡는다.

        Assert.True(vm.TryBeginFittingDrag(grab, fitting.Id));
        vm.CommitDrag(new Point(grab.X + 20, grab.Y));

        // 잡은 자리가 아니라 이동량만큼만 움직인다 — 중심이 커서 밑으로 튀면 T가 크게 어긋난다.
        var expectedShift = 20 / vm.Transform.PixelsPerMillimeter / 4000;
        Assert.Equal(fitting.T + expectedShift, vm.BypassSetItems.Single().T, 3);
    }

    [Fact]
    public void SnappingBypassSetToTee_PlacesItOnMainEdge_NotBranch()
    {
        var vm = CreateMainPipeViewModel();
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 0)));      // 본관 중간 → T 접점
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 2000)));   // 분기
        vm.SnapEndpoint = true;
        vm.SnapIntersection = true;
        vm.IsPlacingBypassSet = true;

        // 커서가 분기 엣지에 더 가깝다(5mm) — 종전에는 동률·근접 판정으로 분기 엣지가 뽑혔다.
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2005, 20)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2005, 20)));

        var set = Assert.Single(vm.BypassSetItems);
        var edge = vm.Edges.Single(x => x.Id == set.EdgeId);
        Assert.Equal(edge.Y1, edge.Y2, 6);   // 본관(수평) 엣지다.
        Assert.True(set.T < 1e-6 || set.T > 1 - 1e-6);   // T 절점(엣지 끝)에 놓였다.
    }

    [Fact]
    public void FailedBypassSet_WhenSelected_IsDistinctFromUnselectedFailure()
    {
        var vm = CreateMainPipeViewModel();
        vm.IsPlacingBypassSet = true;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        var id = Assert.Single(vm.BypassSetItems).Id;
        vm.ApplyModelCreationResult(false, "실패", [
            new PipeConnectionFailure(new Point2D(2000, 0), 0, BypassSet.TypeKey, "DH_게이트 밸브", 0, "테스트") { FittingId = id }]);
        var unselected = vm.BypassSetItems.Single();
        vm.Mode = PipeLayoutMode.Selection;

        vm.SelectFitting(id);
        var selected = vm.BypassSetItems.Single();

        Assert.True(unselected.IsError);
        Assert.True(selected.IsError);
        Assert.NotEqual(unselected.BypassStroke, selected.BypassStroke);
        Assert.NotEqual(unselected.Fill, selected.Fill);
    }

    /// <summary>실창 — 부속 탭 바이패스 칸 [세트 배치] 누르면 배치 모드가 켜지고 본관 클릭이 세트를 놓아야 한다(UI 개편 회귀).</summary>
    [Fact]
    public void RealWindow_BypassPanelButton_EntersPlacement()
    {
        string? report = null; Exception? err = null;
        var t = new System.Threading.Thread(() =>
        {
            try
            {
                var vm = CreateMainPipeViewModel();
                if (System.Windows.Application.Current == null) _ = new System.Windows.Application();
                var view = new DHBIMWATER.UI.Views.Modeling.PipeLayoutView(vm) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, ShowInTaskbar = false, ShowActivated = false };
                void Pump() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                view.Show(); Pump();
                var log = new List<string>();
                string St(string s) => $"{s}: bypass={vm.IsPlacingBypassSet} placing={vm.IsPlacingFitting} mode={vm.Mode} text={vm.InteractionModeText}";
                ((System.Windows.Controls.TabControl)view.FindName("StepTabs")).SelectedIndex = 2; Pump();
                ((System.Windows.Controls.RadioButton)view.FindName("FittingToolBypass")).IsChecked = true; Pump();
                log.Add(St("panel shown"));
                var button = FindButton(view, b => b.Content as string == vm.BypassPlacementButtonText);
                Assert.NotNull(button);
                ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(button!).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
                Pump();
                log.Add(St("after button"));
                // 툴바 경로: 켜진 상태에서 툴바 눌러도 꺼지면 안 됨(토글 함정)
                var tool = FindButton(view, b => System.Windows.Automation.AutomationProperties.GetName(b) == "바이패스 도구");
                Assert.NotNull(tool);
                ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(tool!).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
                Pump();
                log.Add(St("after toolbar"));
                vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
                vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
                log.Add($"sets={vm.BypassSetItems.Count}");
                view.Close();
                report = string.Join(" ;; ", log);
            }
            catch (Exception ex) { err = ex; }
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA); t.Start(); t.Join();
        Assert.Null(err);
        Assert.Contains("after button: bypass=True placing=True", report);
        Assert.Contains("after toolbar: bypass=True placing=True", report);
        Assert.Contains("sets=1", report);
    }

    /// <summary>바이패스 밸브 패밀리가 비어도 세트 배치 중엔 캔버스 클릭이 선을 그리지 않는다.</summary>
    [Fact]
    public void BypassPlacement_WithoutValveFamily_DoesNotDrawLine()
    {
        var vm = CreateMainPipeViewModel();
        vm.BypassValveFamilyName = null;
        var edges = vm.Edges.Count;
        vm.IsPlacingBypassSet = true;

        Assert.True(vm.IsPlacingFitting);
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 50)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 3000)));

        Assert.False(vm.IsDrawing);
        Assert.Equal(edges, vm.Edges.Count);
        Assert.Empty(vm.BypassSetItems);
        Assert.Contains("바이패스", vm.Status);
    }

    private static System.Windows.Controls.Button? FindButton(DependencyObject root, Func<System.Windows.Controls.Button, bool> match)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.Button b && b.IsVisible && match(b)) return b;
            if (FindButton(child, match) is { } found) return found;
        }
        return null;
    }

    private static PipeLayoutViewModel CreateMainPipeViewModel(double diameterMm = 1650)
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
        vm.DiameterMm = diameterMm;
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(0, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(4000, 0)));
        return vm;
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
