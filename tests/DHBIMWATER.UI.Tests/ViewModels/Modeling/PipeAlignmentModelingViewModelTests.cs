using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.UseCases.Gis;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.UI.Commands;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

public sealed class PipeAlignmentModelingViewModelTests
{
    [Fact]
    public void ApplyProgress_ShowsEtaOnlyAfterMinimumSamplesAndResetsForNewPhase()
    {
        var vm = CreateWithoutServices();

        vm.ApplyProgress(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, 0, 10));
        Assert.Equal("0 / 10개 · 계산 중…", vm.ProgressDetail);

        vm.ApplyProgress(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, 5, 10));
        Assert.StartsWith("5 / 10개 · 약 ", vm.ProgressDetail);
        Assert.EndsWith(" 남음", vm.ProgressDetail);

        vm.ApplyProgress(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, 10, 10));
        Assert.Equal("10 / 10개", vm.ProgressDetail);

        vm.ApplyProgress(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingBends, 0, 4));
        Assert.Equal("0 / 4개 · 계산 중…", vm.ProgressDetail);
    }

    [Fact]
    public void ApplyProgress_HidesDetailForPhaseWithoutTotal()
    {
        var vm = CreateWithoutServices();

        vm.ApplyProgress(new PipeAlignmentProgress(PipeAlignmentPhase.Committing, 0, 0));

        Assert.Equal("모델 저장 중", vm.PhaseText);
        Assert.Empty(vm.ProgressDetail);
    }

    [Theory]
    [InlineData(0, 30, "30초")]
    [InlineData(0, 0, "1초")]
    [InlineData(12, 34, "12분 34초")]
    [InlineData(120, 0, "2시간 0분")]
    public void FormatRemaining_FormatsSecondsMinutesAndHours(int minutes, int seconds, string expected)
    {
        var method = typeof(PipeAlignmentModelingViewModel).GetMethod("FormatRemaining", BindingFlags.NonPublic | BindingFlags.Static)!;

        var actual = (string)method.Invoke(null, new object[] { TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) })!;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SummarizeSuppressedWarnings_GroupsRepeatedDescriptions()
    {
        var method = typeof(ModelPipeAlignmentUseCase).GetMethod("SummarizeSuppressedWarnings", BindingFlags.NonPublic | BindingFlags.Static)!;

        var actual = (IReadOnlyList<string>)method.Invoke(null, new object[] { new[] { "짧은 요소", "겹침", "짧은 요소" } })!;

        var summary = Assert.Single(actual);
        Assert.Contains("Revit 경고 3건", summary);
        Assert.Contains("짧은 요소 2건", summary);
        Assert.Contains("겹침 1건", summary);
    }

    [Fact]
    public void OutputMode_DirectShapeAndAdaptiveUseStraightAndBendSettings()
    {
        var vm = CreateWithoutServices();

        vm.OutputMode = PipeAlignmentOutputMode.DirectShape;
        Assert.True(vm.UsesStraightAndBendSettings);

        vm.OutputMode = PipeAlignmentOutputMode.Adaptive;
        Assert.True(vm.UsesStraightAndBendSettings);

        vm.OutputMode = PipeAlignmentOutputMode.PipingSystem;
        Assert.False(vm.UsesStraightAndBendSettings);
    }

    [Fact]
    public void ApplyPreviewRetained_ShowsCancelledFinalModelAndKeepsWarnings()
    {
        var vm = CreateWithoutServices();
        typeof(PipeAlignmentModelingViewModel).GetField("<ResultWarnings>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<string>());

        vm.ApplyPreviewRetained(new PipeAlignmentModelingResult(PipeAlignmentOutputMode.DirectShape, 3, 1, new[] { "미리보기 경고" }));

        Assert.Equal("미리보기 완료 · 최종 생성 취소", vm.PhaseText);
        Assert.Contains("선형 미리보기 3개 유지", vm.ResultSummary);
        Assert.Equal(new[] { "미리보기 경고" }, vm.ResultWarnings);
        Assert.Equal("미리보기 경고", vm.ResultWarningsText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public void ApplyPreviewReady_ShowsInlineConfirmationAndFinalResultClearsIt()
    {
        var vm = CreateWithoutServices();
        typeof(PipeAlignmentModelingViewModel).GetField("<ResultWarnings>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<string>());

        vm.ApplyPreviewReady(new PipeAlignmentModelingResult(PipeAlignmentOutputMode.DirectShape, 4, 1, new[] { "미리보기 경고" }));

        Assert.True(vm.IsAwaitingPreviewConfirm);
        Assert.Equal("선형 미리보기 확인 대기", vm.PhaseText);
        Assert.Contains("선형 미리보기 4개", vm.PreviewConfirmMessage);
        Assert.Equal(new[] { "미리보기 경고" }, vm.ResultWarnings);

        vm.ApplyResult(new PipeAlignmentModelingResult(PipeAlignmentOutputMode.DirectShape, 8, 2, Array.Empty<string>()));

        Assert.False(vm.IsAwaitingPreviewConfirm);
    }

    [Fact]
    public void CancelPreview_ReenablesCreateModelAndNotifiesBindings()
    {
        var vm = CreatePreviewTestViewModel();
        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        vm.ApplyPreviewReady(new PipeAlignmentModelingResult(PipeAlignmentOutputMode.DirectShape, 3, 0, []));
        Assert.False(vm.CanCreateModel);
        changes.Clear();

        typeof(PipeAlignmentModelingViewModel).GetMethod("CancelPreview", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);

        Assert.True(vm.CanCreateModel);
        Assert.True(vm.CreateCommand.CanExecute(null));
        Assert.Equal("미리보기 완료 · 최종 생성 취소", vm.PhaseText);
        Assert.Contains(nameof(PipeAlignmentModelingViewModel.CanCreateModel), changes);
        Assert.Contains(nameof(PipeAlignmentModelingViewModel.IsInputEnabled), changes);
    }

    [Fact]
    public void AwaitingPreviewConfirm_LocksInputAndNotifiesBindings()
    {
        var vm = CreatePreviewTestViewModel();
        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        vm.ApplyPreviewReady(new PipeAlignmentModelingResult(PipeAlignmentOutputMode.DirectShape, 3, 0, []));

        Assert.False(vm.IsInputEnabled);
        Assert.Contains(nameof(PipeAlignmentModelingViewModel.CanCreateModel), changes);
        Assert.Contains(nameof(PipeAlignmentModelingViewModel.IsInputEnabled), changes);

        typeof(PipeAlignmentModelingViewModel).GetMethod("CancelPreview", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);

        Assert.True(vm.IsInputEnabled);
    }

    [Fact]
    public void ApplyResult_WhenAlreadyNotBusy_NotifiesCreateModel()
    {
        var vm = CreatePreviewTestViewModel();
        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);

        vm.ApplyResult(new PipeAlignmentModelingResult(PipeAlignmentOutputMode.DirectShape, 3, 0, []));

        Assert.True(vm.CanCreateModel);
        Assert.Contains(nameof(PipeAlignmentModelingViewModel.CanCreateModel), changes);
    }

    [Fact]
    public void ApplyFailure_WhenDirectShapeIsOutsideDesignLimits_ShowsShortSummary()
    {
        var vm = CreatePreviewTestViewModel();
        const string error = "DirectShape 생성 실패: 내부원점으로부터 너무 멀리 떨어진 곳에 모델링을 시도했습니다.";

        vm.ApplyFailure(error);

        Assert.Equal("내부원점에서 너무 멀어 생성할 수 없습니다.", vm.ResultSummary);
        Assert.Equal(new[] { error }, vm.ResultWarnings);
    }

    [Fact]
    public void ApplyFailure_WhenNotDesignLimitFailure_ShowsGenericSummary()
    {
        var vm = CreatePreviewTestViewModel();

        vm.ApplyFailure("직관 DirectShape 생성 실패: 테스트");

        Assert.Equal("모델 생성에 실패했습니다.", vm.ResultSummary);
    }

    [Fact]
    public void DiameterMappingRow_DefaultPipeKindIsNotUserModified()
    {
        var row = new DiameterMappingRow { RawValue = "DN300", KindRawValue = "(값 없음)", FileName = "sample.shp", FileKey = "sample", Count = 1 };

        row.InitializeValues(Array.Empty<DiameterMappingOption>(), 300, PipeKindCatalog.Water1, isUserModified: false, isDefaultPipeKind: true);

        Assert.False(row.IsUserModified);
        Assert.True(row.IsDefaultPipeKind);
        Assert.Equal(PipeKindCatalog.Water1, row.PipeKind);
    }

    [Fact]
    public void RebuildDiameterMappings_ExcelVertexDiameterOnly_DoesNotAddMissingValueRow()
    {
        var feature = new PipeAlignment(
            [new Point3D(0, 0, 0), new Point3D(1, 0, 0)], string.Empty, 0, "sample.xlsx", "1",
            new Dictionary<string, string>(), VertexDiameterRawValues: ["DN300", "DN300"]);
        var read = new ShapefileReadResult([feature], [], ShapefileExtent.Empty, null, null, string.Empty, 1, 2, 1, [], null);
        var mapping = new ExcelAlignmentMapping("Sheet1", 1, 2, 0, 1, 2, null, VertexDiameterColumnIndex: 3);
        var item = new PipeAlignmentSourceFileItem
        {
            Path = "sample.xlsx", SheetName = "Sheet1", ExcelMapping = mapping, ReadResult = read,
            DiameterField = PipeAlignmentSourceFileItem.ManualFieldOption, KindField = PipeAlignmentSourceFileItem.ManualFieldOption
        };
        var vm = CreateForMappingTest(item);

        typeof(PipeAlignmentModelingViewModel).GetMethod("RebuildDiameterMappings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);

        var row = Assert.Single(vm.DiameterMappings);
        Assert.Equal("DN300", row.RawValue);
        Assert.Equal(2, row.Count);
    }

    [Fact]
    public void GuessDiameterField_NormalizesExcelHeaderWhitespace()
        => Assert.Equal("호칭지름\n(DN)", AlignmentAttributeParser.GuessDiameterField(["호칭지름\n(DN)"]));

    private static PipeAlignmentModelingViewModel CreateWithoutServices()
    {
        var vm = (PipeAlignmentModelingViewModel)RuntimeHelpers.GetUninitializedObject(typeof(PipeAlignmentModelingViewModel));
        typeof(PipeAlignmentModelingViewModel).GetField("_phaseStopwatch", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, new Stopwatch());
        return vm;
    }

    private static PipeAlignmentModelingViewModel CreatePreviewTestViewModel()
    {
        var vm = CreateWithoutServices();
        SetAutoProperty(vm, "PreflightIssues", new ObservableCollection<PreflightIssue>());
        SetAutoProperty(vm, "ResultWarnings", new ObservableCollection<string>());
        SetAutoProperty(vm, "CreateCommand", new RelayCommand(_ => { }, _ => vm.CanCreateModel));
        return vm;
    }

    private static PipeAlignmentModelingViewModel CreateForMappingTest(PipeAlignmentSourceFileItem item)
    {
        var vm = CreateWithoutServices();
        SetAutoProperty(vm, "Files", new ObservableCollection<PipeAlignmentSourceFileItem> { item });
        SetAutoProperty(vm, "DiameterMappings", new ObservableCollection<DiameterMappingRow>());
        SetAutoProperty(vm, "PreflightIssues", new ObservableCollection<PreflightIssue>());
        SetAutoProperty(vm, "DiameterOptions", new[] { new DiameterMappingOption("(지정 안 함)", null), new DiameterMappingOption("DN300", 300) });
        typeof(PipeAlignmentModelingViewModel).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, BendSettings.Default);
        return vm;
    }

    private static void SetAutoProperty<T>(PipeAlignmentModelingViewModel vm, string propertyName, T value)
        => typeof(PipeAlignmentModelingViewModel).GetField($"<{propertyName}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, value);
}
