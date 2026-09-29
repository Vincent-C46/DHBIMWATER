using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

/// <summary>docs/219 § D — [배치 제외 구간] 탭 행 편집·검증·전달.</summary>
public sealed class AlignmentExclusionViewModelTests
{
    [Fact]
    public void Add_PlacesDefaultRangeOnSelectedTarget()
    {
        var vm = CreateWithTarget();

        var row = vm.Add();

        Assert.Same(vm.Targets[0], row.Target);
        Assert.Equal("0+050.000", row.StartText);
        Assert.Equal("0+060.000", row.EndText);
        Assert.Equal(ExclusionRowStatus.Ok, row.Status);
        Assert.Same(row, vm.SelectedRow);
        Assert.Equal("선형·제외 구간 (1)", vm.HeaderText);
    }

    [Fact]
    public void StationInput_IsNormalizedAndWarnsSkippedBend()
    {
        var vm = CreateWithTarget();
        var row = vm.Add();

        row.StartText = "STA.0+35";
        row.EndText = "45";

        Assert.Equal("0+035.000", row.StartText);
        Assert.Equal("0+045.000", row.EndText);
        Assert.Equal("10.000", row.LengthText);
        Assert.Equal(ExclusionRowStatus.Warning, row.Status);
        Assert.Contains(row.Warnings, x => x.StartsWith("구간 안 곡관 1개 생략", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("abc", "60", "시작 Sta. 형식 오류")]
    [InlineData("60", "50", "끝 Sta.가 시작 Sta.보다 커야")]
    [InlineData("90", "150", "선형 측점 범위")]
    public void InvalidInput_IsError(string start, string end, string expected)
    {
        var vm = CreateWithTarget();
        var row = vm.Add();

        row.StartText = start;
        row.EndText = end;

        Assert.Equal(ExclusionRowStatus.Error, row.Status);
        Assert.Contains(row.Errors, x => x.StartsWith(expected, StringComparison.Ordinal));
        Assert.Empty(vm.RangesFor("a.shp\n"));
    }

    [Fact]
    public void OverlappingRowsOnSameTarget_AreErrors()
    {
        var vm = CreateWithTarget();
        var first = vm.Add();
        first.StartText = "10"; first.EndText = "30";
        var second = vm.Add();
        second.StartText = "25"; second.EndText = "50";

        Assert.Contains(first.Errors, x => x.Contains("겹칩니다", StringComparison.Ordinal));
        Assert.Contains(second.Errors, x => x.Contains("겹칩니다", StringComparison.Ordinal));
        Assert.True(vm.HasErrors);
    }

    [Fact]
    public void ShortCutPipeBeforeBoundary_IsWarning()
    {
        var vm = CreateWithTarget();
        var row = vm.Add();

        // 6 m 간격: 0~6~12 다음 12~12.5 조각이 시작 경계 앞 절단관이 된다.
        row.StartText = "12.5";
        row.EndText = "20";

        Assert.Contains(row.Warnings, x => x.StartsWith("시작 경계 앞 절단관 0.500 m", StringComparison.Ordinal));
        Assert.Contains(vm.Preview!.Pipes, x => x.IsCut && Math.Abs(x.EndStation - 12.5) < 1e-9);
        Assert.Contains(vm.Preview!.Pipes, x => Math.Abs(x.StartStation - 20) < 1e-9 && !x.IsCut);
    }

    [Fact]
    public void RangesFor_ReturnsValidRowsOfFile()
    {
        var vm = CreateWithTarget();
        var row = vm.Add();
        row.Name = "밸브실 #1";
        row.StartText = "0+035"; row.EndText = "0+045";

        var range = Assert.Single(vm.RangesFor("a.shp\n"));

        Assert.Equal(new AlignmentExclusionRange("1", 35, 45, "밸브실 #1"), range);
        Assert.Empty(vm.RangesFor("other\n"));
    }

    [Fact]
    public void Paste_AddsRowsFromTabSeparatedText()
    {
        var vm = CreateWithTarget();

        var count = vm.Paste("밸브실\t0+010\t0+020\r\n\t30\t35\r\n");

        Assert.Equal(2, count);
        Assert.Equal("밸브실", vm.Rows[0].Name);
        Assert.Equal("0+030.000", vm.Rows[1].StartText);
        Assert.All(vm.Rows, row => Assert.Same(vm.Targets[0], row.Target));
        Assert.Equal(0, vm.Paste("not a table"));
    }

    [Fact]
    public void Remove_WithoutSelectionItems_RemovesCurrentRow()
    {
        var vm = CreateWithTarget();
        vm.Add();
        var second = vm.Add();

        vm.Remove(null);

        Assert.DoesNotContain(second, vm.Rows);
        Assert.Single(vm.Rows);
    }

    [Fact]
    public void RebuildTargets_KeepsRowsOfSameRecordAndFlagsMissing()
    {
        var vm = CreateWithTarget();
        var row = vm.Add();

        vm.RebuildTargets([AlignmentExclusionTargetOption.Create("a.shp\n", "a.shp", Feature())]);
        Assert.Same(vm.Targets[0], row.Target);
        Assert.Equal(ExclusionRowStatus.Ok, row.Status);

        vm.RebuildTargets([]);
        Assert.Null(row.Target);
        Assert.Equal(ExclusionRowStatus.Error, row.Status);
    }

    [Fact]
    public void MainViewModel_BlocksModelingOnExclusionError()
    {
        var main = CreateMainWithoutServices();
        var exclusions = main.Exclusions;
        exclusions.RebuildTargets([AlignmentExclusionTargetOption.Create("a.shp\n", "a.shp", Feature())]);
        var row = exclusions.Add();
        row.StartText = "60"; row.EndText = "50";

        typeof(PipeAlignmentModelingViewModel).GetMethod("RefreshPreflightIssues", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [null]);

        var issue = Assert.Single(main.PreflightIssues, x => x.TargetTab == PipeAlignmentTab.Exclusion);
        Assert.Equal(PreflightSeverity.Blocking, issue.Severity);
        Assert.Equal(row.Id.ToString(), issue.HighlightKey);
        Assert.True(main.HasBlockingPreflightIssues);
    }

    [Fact]
    public void VisibleRows_ShowOnlyFocusedTargets_UnlessShowAll()
    {
        // docs/220 § D — 선택 선형의 구간만 표에 보인다.
        var vm = new AlignmentExclusionViewModel { IntervalM = 6 };
        var other = Feature() with { RecordNumber = "2" };
        vm.RebuildTargets([AlignmentExclusionTargetOption.Create("a.shp\n", "a.shp", Feature()), AlignmentExclusionTargetOption.Create("a.shp\n", "a.shp", other)]);
        vm.PreferredTarget = () => vm.Targets[1];
        var onSecond = vm.Add();
        vm.PreferredTarget = null;
        vm.SelectedRow = null;
        var onFirst = vm.Add();

        Assert.Same(vm.Targets[1], onSecond.Target);
        Assert.Equal(2, vm.VisibleRows.Count);

        vm.SetFocusTargets([("a.shp\n", "2")]);
        Assert.Equal(new[] { onSecond }, vm.VisibleRows);

        vm.ShowAllRows = true;
        Assert.Equal(new[] { onSecond, onFirst }, vm.VisibleRows);
    }

    [Fact]
    public void Overview_ReversedExcelStationLine_KeepsZoneAtSameLocation()
    {
        // docs/220 § 0-5 — 엑셀 Station 선형은 반전해도 측점이 위치에 고정. 화살표(점 순서)만 뒤집힌다.
        var vm = CreateMainWithoutServices();
        var feature = Feature() with { VertexStationM = [0d, 40d, 100d] };
        var direction = new AlignmentDirectionRow
        {
            FileKey = "a.shp\n", FileName = "a.shp", RecordNumber = "1", SourceStart = feature.Vertices[0], SourceEnd = feature.Vertices[^1],
            VertexCount = 3, LengthM = 100, Feature = feature, IsSelected = true
        };
        SetAutoProperty(vm, "DirectionRows", new ObservableCollection<AlignmentDirectionRow> { direction });
        vm.Exclusions.RebuildTargets([AlignmentExclusionTargetOption.Create("a.shp\n", "a.shp", feature)]);
        var zone = vm.Exclusions.Add();
        zone.StartText = "10"; zone.EndText = "20";
        var refresh = typeof(PipeAlignmentModelingViewModel).GetMethod("RefreshOverview", BindingFlags.NonPublic | BindingFlags.Instance)!;

        refresh.Invoke(vm, null);
        var forward = vm.Overview!;
        direction.IsReversed = true;
        refresh.Invoke(vm, null);
        var reversed = vm.Overview!;

        Assert.Equal(0, forward.Lines[0].Points[0].X);
        Assert.Equal(100, reversed.Lines[0].Points[0].X);
        Assert.Equal("0+100.000", reversed.Lines[0].StartStationText);
        Assert.Equal(new[] { 10d, 20d }, forward.Zones[0].Points.Select(p => p.X).OrderBy(x => x));
        Assert.Equal(new[] { 10d, 20d }, reversed.Zones[0].Points.Select(p => p.X).OrderBy(x => x).Select(x => Math.Round(x, 6)));
    }

    [Fact]
    public void Overview_ReversedLineWithoutStation_MeasuresFromNewStart()
    {
        // Station 열이 없으면 진행 방향 시점부터 잰다(현행) — 반전하면 같은 Sta.가 반대편에 놓인다.
        var vm = CreateMainWithoutServices();
        var feature = Feature();
        var direction = new AlignmentDirectionRow
        {
            FileKey = "a.shp\n", FileName = "a.shp", RecordNumber = "1", SourceStart = feature.Vertices[0], SourceEnd = feature.Vertices[^1],
            VertexCount = 3, LengthM = 100, Feature = feature, IsReversed = true
        };
        SetAutoProperty(vm, "DirectionRows", new ObservableCollection<AlignmentDirectionRow> { direction });
        vm.Exclusions.RebuildTargets([AlignmentExclusionTargetOption.Create("a.shp\n", "a.shp", feature)]);
        var zone = vm.Exclusions.Add();
        zone.StartText = "10"; zone.EndText = "20";

        typeof(PipeAlignmentModelingViewModel).GetMethod("RefreshOverview", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);

        Assert.Equal("0+000.000", vm.Overview!.Lines[0].StartStationText);
        Assert.Equal(new[] { 80d, 90d }, vm.Overview.Zones[0].Points.Select(p => Math.Round(p.X, 6)).OrderBy(x => x));
    }

    private static AlignmentExclusionViewModel CreateWithTarget()
    {
        var vm = new AlignmentExclusionViewModel { IntervalM = 6 };
        vm.RebuildTargets([AlignmentExclusionTargetOption.Create("a.shp\n", "a.shp", Feature())]);
        return vm;
    }

    /// <summary>0~100 m 직선, 40 m에 곡관유형 지정 정점.</summary>
    private static PipeAlignment Feature() => new(
        [new Point3D(0, 0, 0), new Point3D(40, 0, 0), new Point3D(100, 0, 0)], string.Empty, 0, "a.shp", "1",
        new Dictionary<string, string>(), VertexBendKinds: [null, "22.5", null]);

    private static PipeAlignmentModelingViewModel CreateMainWithoutServices()
    {
        var vm = (PipeAlignmentModelingViewModel)RuntimeHelpers.GetUninitializedObject(typeof(PipeAlignmentModelingViewModel));
        typeof(PipeAlignmentModelingViewModel).GetField("_phaseStopwatch", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, new Stopwatch());
        SetAutoProperty(vm, "Files", new ObservableCollection<PipeAlignmentSourceFileItem>());
        SetAutoProperty(vm, "DiameterMappings", new ObservableCollection<DiameterMappingRow>());
        SetAutoProperty(vm, "PreflightIssues", new ObservableCollection<PreflightIssue>());
        SetAutoProperty(vm, "Exclusions", new AlignmentExclusionViewModel());
        typeof(PipeAlignmentModelingViewModel).GetField("_intervalM", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, 6d);
        typeof(PipeAlignmentModelingViewModel).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, BendSettings.Default);
        return vm;
    }

    private static void SetAutoProperty<T>(PipeAlignmentModelingViewModel vm, string propertyName, T value)
        => typeof(PipeAlignmentModelingViewModel).GetField($"<{propertyName}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, value);
}
