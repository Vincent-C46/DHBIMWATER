using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

/// <summary>이음관 점 배치(docs/175 § C-3)와 패밀리 설정 저장·적용(§ E-4), 십자 검증(§ B-5).</summary>
public sealed class PipeLayoutJointTests
{
    private const string ValveFamily = "제수밸브 : DN100";
    private const string JointFamily = "DH_이음관 : DN100";

    // ── 이음관 목록 ────────────────────────────────────────

    [Fact]
    public void JointFamilyTypeNames_KeepsOnlyNameKeywordMatches()
    {
        var vm = CreateViewModelWithValve(["직관 : DN100", "DH_이음관 : DN100", "신축이음관 : 300", "곡관 : DN100"]);

        Assert.Equal(["DH_이음관 : DN100", "신축이음관 : 300"], vm.JointFamilyTypeNames);
        Assert.False(vm.HasNoJointFamily);
    }

    [Fact]
    public void JointFamilyTypeNames_EmptyWhenProjectHasNoJointFamily()
    {
        var vm = CreateViewModelWithValve(["직관 : DN100", "곡관 : DN100"]);

        Assert.Empty(vm.JointFamilyTypeNames);
        Assert.True(vm.HasNoJointFamily);
    }

    // 1단 콤보는 패밀리명만, 2단 리스트는 고른 패밀리의 유형만 본다(docs/200).
    [Fact]
    public void JointFamilyNames_GroupsByFamily_WithoutTypeName()
    {
        var vm = CreateViewModelWithValve(["DH_이음관 : DN100", "DH_이음관 : DN150", "신축이음관 : 300"]);

        Assert.Equal(["DH_이음관", "신축이음관"], vm.JointFamilyNames);
    }

    [Fact]
    public void JointTypeNames_KeepsOnlySelectedFamilyTypes()
    {
        var vm = CreateViewModelWithValve(["DH_이음관 : DN100", "DH_이음관 : DN150", "신축이음관 : 300"]);

        Assert.Equal(["DH_이음관 : DN100", "DH_이음관 : DN150"], vm.JointTypeNames);

        vm.SelectedJointFamilyName = "신축이음관";

        Assert.Equal(["신축이음관 : 300"], vm.JointTypeNames);
    }

    [Fact]
    public void ChangingJointFamily_ClearsTypeSelection()
    {
        var vm = CreateViewModelWithValve(["DH_이음관 : DN100", "DH_이음관 : DN150", "신축이음관 : 300"]);
        vm.SelectedJointFamilyTypeName = "DH_이음관 : DN100";

        vm.SelectedJointFamilyName = "신축이음관";

        Assert.Null(vm.SelectedJointFamilyTypeName);
        Assert.False(vm.IsPlacingFitting);
    }

    // ── 점 배치 ──────────────────────────────────────────

    [Fact]
    public void SelectingJoint_EntersPlacementMode_AndClearsValveSelection()
    {
        var vm = CreateViewModelWithValve();
        vm.SelectedFamilyTypeName = ValveFamily;

        vm.SelectedJointFamilyTypeName = JointFamily;

        Assert.True(vm.IsPlacingFitting);
        Assert.Null(vm.SelectedFamilyTypeName);
        Assert.Equal("이음관 배치", vm.InteractionModeText);
    }

    [Fact]
    public void SelectingValve_ClearsJointSelection()
    {
        var vm = CreateViewModelWithValve();
        vm.SelectedJointFamilyTypeName = JointFamily;

        vm.SelectedFamilyTypeName = ValveFamily;

        Assert.Null(vm.SelectedJointFamilyTypeName);
        Assert.Equal("부속 배치", vm.InteractionModeText);
    }

    [Fact]
    public void ClickingLine_PlacesJointAsIndependentFitting()
    {
        var vm = CreateViewModelWithValve();

        PlaceJoint(vm, new Point2D(1000, 0));

        var joint = Assert.Single(vm.InlineFittings, x => x.IsJoint);
        Assert.Equal(JointFamily, joint.FamilyTypeName);
        Assert.Equal(InlineJoint.TypeKey, joint.TypeKey);
        Assert.Equal("이음관", joint.KindBadge);
        Assert.Equal("#16A085", joint.Fill);
        Assert.Equal(2, vm.InlineFittings.Count);   // 밸브 1 + 이음관 1
        // 배치 정의도 독립 부속 하나로 나간다.
        PipeNetworkDefinition? request = null;
        vm.CreateModelAction = x => request = x;
        vm.CreateModelCommand.Execute(null);
        Assert.NotNull(request);
        Assert.Equal(2, request!.InlineFittings.Count);
        Assert.Single(request.InlineFittings, x => x.IsJoint);
    }

    [Fact]
    public void PlacingJoint_IsUndoable()
    {
        var vm = CreateViewModelWithValve();
        PlaceJoint(vm, new Point2D(1000, 0));
        Assert.Contains(vm.InlineFittings, x => x.IsJoint);

        vm.UndoCommand.Execute(null);

        Assert.DoesNotContain(vm.InlineFittings, x => x.IsJoint);
        Assert.Single(vm.InlineFittings);
    }

    [Fact]
    public void Escape_CancelsJointPlacement()
    {
        var vm = CreateViewModelWithValve();
        vm.SelectedJointFamilyTypeName = JointFamily;

        vm.HandleEscape();

        Assert.Null(vm.SelectedJointFamilyTypeName);
        Assert.False(vm.IsPlacingFitting);
    }

    [Fact]
    public void ImportedJoint_ComesBackAsJointFitting()
    {
        var vm = CreateViewModelWithValve();
        vm.ClearCommand.Execute(null);

        vm.ApplyPickedPiping(new PipeLayoutImport(
            [new ImportedEdge(new Point2D(0, 0), new Point2D(4000, 0), 100)],
            [new ImportedFitting(0, ValveFamily, 0.5), new ImportedFitting(0, JointFamily, 0.3, IsJoint: true)],
            0, [11, 12, 13], []));

        Assert.Equal(2, vm.InlineFittings.Count);
        var joint = Assert.Single(vm.InlineFittings, x => x.IsJoint);
        Assert.Equal(JointFamily, joint.FamilyTypeName);
        Assert.Equal(0.3, joint.T, 2);
    }

    // ── 십자 검증(docs/175 § B-5) ────────────────────────────

    [Fact]
    public void Validation_RequiresCrossFamily_WhenCrossNodeExists()
    {
        var vm = CreateViewModelWithValve();
        DrawCross(vm, orthogonal: true);

        Assert.Contains("십자관 패밀리 선택 필요", vm.ValidationSummary);
        Assert.DoesNotContain("직교 십자가 아닌", vm.ValidationSummary);
        Assert.DoesNotContain("아직 지원하지 않습니다", vm.ValidationSummary);

        vm.CrossFamilyTypeName = "DH_십자관 : DN100";
        Assert.DoesNotContain("십자관 패밀리 선택 필요", vm.ValidationSummary);
    }

    [Fact]
    public void Validation_RejectsNonOrthogonalCross()
    {
        var vm = CreateViewModelWithValve();
        DrawCross(vm, orthogonal: false);

        Assert.True(vm.ValidationSummary.Contains("직교 십자가 아닌 4방향 분기 1개", StringComparison.Ordinal),
            $"summary: {vm.ValidationSummary} / nodes: {vm.NetworkSummary}");
    }

    [Fact]
    public void CrossFamily_IsGuessedFromFittingNames()
    {
        var vm = CreateViewModelWithValve(["직관 : DN100", "DH_십자관 : DN100", "이경관 : DN100x80", "편락관 : DN100x80"]);

        Assert.Equal("DH_십자관 : DN100", vm.CrossFamilyTypeName);
        // 동심·편심은 docs/188부터 한 칸이다. 키워드 순서상 `이경관`이 먼저 걸린다.
        Assert.Equal("이경관 : DN100x80", vm.ReducerFamilyTypeName);
        Assert.Contains("십자 DH_십자관 : DN100", vm.FamilySettingsSummary);
    }

    // ── 패밀리 설정 저장·적용(docs/175 § E-4) ────────────────────

    [Fact]
    public void SavedSettings_OverrideGuess_OnlyForNonEmptyFields()
    {
        var repo = new StubSettingsRepo
        {
            Saved = new PipeLayoutFamilySettings(SegmentFamilyTypeName: "DH_직관 : DN100", CrossFamilyTypeName: "DH_십자관 : DN100")
        };
        var vm = new PipeLayoutViewModel(new StubTypeRepo(["직관 : DN100", "DH_직관 : DN100", "곡관90 : DN100", "DH_십자관 : DN100"]), repo);

        Assert.Equal("DH_직관 : DN100", vm.SegmentFamilyTypeName);     // 저장값이 추정("직관 : DN100")을 이긴다
        Assert.Equal("곡관90 : DN100", vm.BendFamilyTypeName);         // 빈 칸은 추정 그대로
        Assert.Equal("DH_십자관 : DN100", vm.CrossFamilyTypeName);
    }

    [Fact]
    public void SaveCommand_SendsCurrentSettings()
    {
        var vm = CreateViewModelWithValve();
        vm.CrossFamilyTypeName = "DH_십자관 : DN100";
        PipeLayoutFamilySettings? sent = null;
        vm.SaveFamilySettingsAction = x => sent = x;

        vm.SaveFamilySettingsCommand.Execute(null);

        Assert.NotNull(sent);
        Assert.Equal("DH_십자관 : DN100", sent!.CrossFamilyTypeName);
        Assert.Equal(vm.SegmentFamilyTypeName, sent.SegmentFamilyTypeName);
        Assert.Equal(vm.LengthParameterName, sent.LengthParameterName);
        Assert.False(sent.IsIncomplete);
    }

    /// <summary>모달 설정 창이 떠 있는 동안은 ExternalEvent가 실행되지 않으므로 [저장]은 창을 닫아야 하고,
    /// 상태줄은 요청 단계와 결과 단계를 구분해야 한다.</summary>
    [Fact]
    public void SaveCommand_ClosesDialog_AndReportsResultLater()
    {
        var vm = CreateViewModelWithValve();
        var closed = 0;
        vm.SaveFamilySettingsAction = _ => { };
        vm.CloseFamilySettingsAction = () => closed++;

        vm.SaveFamilySettingsCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Contains("저장하는 중", vm.Status);

        vm.ApplyFamilySettingsSaveResult(true, "패밀리 설정을 프로젝트에 저장했습니다.");
        Assert.Equal("패밀리 설정을 프로젝트에 저장했습니다.", vm.Status);
    }

    [Fact]
    public void ToFamilySettings_RoundTrips_ThroughToSelection()
    {
        var vm = CreateViewModelWithValve();
        vm.CrossFamilyTypeName = "DH_십자관 : DN100";
        vm.ReducerFamilyTypeName = "이경관 : DN100x80";

        var selection = vm.ToFamilySettings().ToSelection();

        Assert.Equal(vm.SegmentFamilyTypeName, selection.SegmentFamilyTypeName);
        Assert.Equal("DH_십자관 : DN100", selection.CrossFamilyTypeName);
        Assert.Equal("이경관 : DN100x80", selection.ReducerFamilyTypeName);
        Assert.Equal(vm.JointDiameterParameterName ?? "", selection.JointDiameterParameterName);
    }

    [Fact]
    public void Import_DoesNotOverrideSavedFamily_ButFillsEmptyOne()
    {
        var repo = new StubSettingsRepo { Saved = new PipeLayoutFamilySettings(SegmentFamilyTypeName: "DH_직관 : DN100") };
        // 4방 분기 패밀리 이름에 십자·CROSS 키워드가 없어 추정이 비어 있다 → 가져오기가 채워야 한다.
        var vm = new PipeLayoutViewModel(new StubTypeRepo(["직관 : DN100", "DH_직관 : DN100", "DH_4방분기 : DN100"]), repo);
        vm.HandleCanvasSizeChanged(1000, 1000);
        Assert.Null(vm.CrossFamilyTypeName);

        vm.ApplyPickedPiping(new PipeLayoutImport(
            [new ImportedEdge(new Point2D(0, 0), new Point2D(4000, 0), 100)], [], 0, [1], [],
            SegmentFamilyTypeName: "직관 : DN100", CrossFamilyTypeName: "DH_4방분기 : DN100"));

        Assert.Equal("DH_직관 : DN100", vm.SegmentFamilyTypeName);   // 저장값 유지
        Assert.Equal("DH_4방분기 : DN100", vm.CrossFamilyTypeName);   // 비어 있던 칸은 채워짐
        Assert.Contains("관 패밀리가 저장된 설정과 다릅니다", vm.Status);
    }

    // ── 헬퍼 ───────────────────────────────────────────────

    private static void PlaceJoint(PipeLayoutViewModel vm, Point2D at)
    {
        vm.SelectedJointFamilyTypeName = JointFamily;
        vm.HandleCanvasMove(vm.Transform.ToScreen(at));
        vm.HandleCanvasClick(vm.Transform.ToScreen(at));
    }

    /// <summary>중심 (2000,0)에 4개 선이 모이는 십자. 기존 (0,0)-(4000,0) 선을 중심 클릭으로 T로 쪼갠 뒤 위·아래로 뻗는다.</summary>
    private static void DrawCross(PipeLayoutViewModel vm, bool orthogonal)
    {
        vm.SelectedFamilyTypeName = null;
        vm.SelectedJointFamilyTypeName = null;
        vm.UseAngleSnap = false;
        var center = new Point2D(2000, 0);
        vm.HandleCanvasClick(vm.Transform.ToScreen(center));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 2000)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(center));
        vm.HandleCanvasClick(vm.Transform.ToScreen(orthogonal ? new Point2D(2000, -2000) : new Point2D(3500, -1500)));
    }

    private static PipeLayoutViewModel CreateViewModelWithValve(IEnumerable<string>? fittingNames = null)
    {
        var vm = new PipeLayoutViewModel(new StubTypeRepo(
            fittingNames ?? ["직관 : DN100", "곡관90 : DN100", "T형 : DN100", JointFamily]));
        vm.HandleCanvasSizeChanged(1000, 1000);
        vm.SnapReferencePoint = false;
        vm.SnapEndpoint = false;
        vm.SnapMidpoint = false;
        vm.SnapQuadrant = false;
        vm.SnapIntersection = false;
        vm.SnapPerpendicular = false;
        vm.SnapNearest = false;

        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(0, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(4000, 0)));
        vm.SelectedFamilyTypeName = ValveFamily;
        vm.HandleCanvasMove(vm.Transform.ToScreen(new Point2D(2000, 0)));
        vm.HandleCanvasClick(vm.Transform.ToScreen(new Point2D(2000, 0)));
        vm.SelectedFamilyTypeName = null;
        return vm;
    }

    private sealed class StubSettingsRepo : IPipeLayoutFamilySettingsRepo
    {
        public PipeLayoutFamilySettings? Saved { get; set; }
        public PipeLayoutFamilySettings? Load() => Saved;
        public void Save(PipeLayoutFamilySettings settings) => Saved = settings;
    }

    private sealed class StubTypeRepo(IEnumerable<string> fittingNames) : IElementTypeQueryRepo
    {
        private readonly IReadOnlyList<string> _fittingNames = fittingNames.ToList();

        public IEnumerable<string> GetPipeAccessoryTypeNames() => [ValveFamily];
        public IEnumerable<string> GetPipeFittingTypeNames() => _fittingNames;
        public IEnumerable<string> GetPipeAccessoryInstanceParameterNames(string familyTypeName) => ["길이", "이음관_구경", "DN"];
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
