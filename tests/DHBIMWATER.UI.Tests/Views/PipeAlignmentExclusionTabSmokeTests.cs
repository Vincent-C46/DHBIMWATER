using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.UI.Controls;
using DHBIMWATER.UI.ViewModels.Modeling;
using DHBIMWATER.UI.Views.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.Views;

/// <summary>docs/219 — [배치 제외 구간] 탭 XAML 로드·미리보기 렌더 스모크. Revit 없이 StaticResource·iconPacks·컨트롤 렌더 예외를 거른다.</summary>
public sealed class PipeAlignmentExclusionTabSmokeTests
{
    [Fact]
    public void ExclusionTab_RendersWithRowsAndPreview()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null) _ = new System.Windows.Application();
                var vm = CreateViewModel();
                var row = vm.Exclusions.Add();
                row.Name = "밸브실 #1";
                row.StartText = "35"; row.EndText = "45";
                vm.Exclusions.Add().EndText = "10"; // 오류 행도 그린다

                var view = new PipeAlignmentModelingView(vm);
                var tabs = (TabControl)view.FindName("PipeAlignmentTabs");
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(x => Equals(x.Tag, "Exclusion"));
                view.Measure(new Size(960, 900));
                view.Arrange(new Rect(0, 0, 960, 900));
                view.UpdateLayout();
                // 창을 띄우지 않았으므로 지연된 바인딩·템플릿 적용을 한 번 돌린다.
                view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                Assert.Equal("선형·제외 구간 (2)", tabs.Items.OfType<TabItem>().Single(x => Equals(x.Tag, "Exclusion")).Header);
                Assert.NotNull(vm.Exclusions.Preview);
                // 미리보기 두 장을 실제로 그려 본다(OnRender 예외 확인).
                var control = new AlignmentExclusionPreviewControl { Model = vm.Exclusions.Preview, IsDetail = true, Width = 800, Height = 135 };
                control.Measure(new Size(800, 135)); control.Arrange(new Rect(0, 0, 800, 135));
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(800, 135, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(control);
                control.IsDetail = false;
                bitmap.Render(control);

                // docs/220 — 선형 전체 개요도 렌더(선·화살표·띠·라벨).
                var target = vm.Exclusions.Targets[0];
                var line = new AlignmentDirectionRow
                {
                    FileKey = target.FileKey, FileName = target.FileName, RecordNumber = target.RecordNumber,
                    SourceStart = target.Feature.Vertices[0], SourceEnd = target.Feature.Vertices[^1], VertexCount = 3, LengthM = 100, Feature = target.Feature
                };
                var model = new AlignmentOverviewModel(
                    [new AlignmentOverviewLine(line, target.Feature.Vertices.Select(v => new Point(v.X, v.Y)).ToList(), true, "0+000.000")],
                    [new AlignmentOverviewZone(row, line, [new Point(35, 0), new Point(45, 0)], row.DisplayName, true)]);
                var overview = new AlignmentOverviewControl { Model = model, Width = 640, Height = 320 };
                overview.Measure(new Size(640, 320)); overview.Arrange(new Rect(0, 0, 640, 320));
                var overviewBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(640, 320, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                overviewBitmap.Render(overview);
                view.Close();
            }
            catch (Exception ex) { captured = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(captured);
    }

    private static PipeAlignmentModelingViewModel CreateViewModel()
    {
        var vm = (PipeAlignmentModelingViewModel)RuntimeHelpers.GetUninitializedObject(typeof(PipeAlignmentModelingViewModel));
        typeof(PipeAlignmentModelingViewModel).GetField("_phaseStopwatch", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, new Stopwatch());
        foreach (var (name, value) in new (string, object)[]
        {
            ("Files", new ObservableCollection<PipeAlignmentSourceFileItem>()), ("DiameterMappings", new ObservableCollection<DiameterMappingRow>()),
            ("PreflightIssues", new ObservableCollection<PreflightIssue>()), ("DirectionRows", new ObservableCollection<AlignmentDirectionRow>()),
            ("Attention", new ObservableCollection<PipeNetworkNodeReport>()), ("DiagnosisWarnings", new ObservableCollection<string>()),
            ("ResultWarnings", new ObservableCollection<string>()), ("Exclusions", new AlignmentExclusionViewModel { IntervalM = 6 })
        })
            typeof(PipeAlignmentModelingViewModel).GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, value);
        var feature = new PipeAlignment([new Point3D(0, 0, 0), new Point3D(40, 0, 0), new Point3D(100, 0, 0)], string.Empty, 0, "a.shp", "1",
            new Dictionary<string, string>(), VertexBendKinds: [null, "22.5", null]);
        vm.Exclusions.RebuildTargets([AlignmentExclusionTargetOption.Create("a.shp\n", "a.shp", feature)]);
        return vm;
    }
}
