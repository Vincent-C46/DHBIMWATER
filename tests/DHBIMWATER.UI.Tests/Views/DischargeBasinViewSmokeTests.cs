using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.UI.ViewModels.Modeling;
using DHBIMWATER.UI.Views.Modeling;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Xunit;

namespace DHBIMWATER.UI.Tests.Views;

/// <summary>
/// XAML 로드 스모크 테스트.
/// BAML 컴파일만으로는 StaticResource 키 누락·컨버터 미등록이 드러나지 않고 실행 시점에야 터진다.
/// Revit에서 창을 띄워야 알 수 있는 문제를 여기서 먼저 걸러낸다.
/// </summary>
[Collection("WpfApplication")]
public class DischargeBasinViewSmokeTests
{
    private sealed class StubDialogService : IDialogService
    {
        public void Info(string title, string message) { }
        public void Warn(string title, string message) { }
        public bool Confirm(string title, string message) => true;
    }

    private sealed class StubCreateUseCase : ICreateDischargeBasinUseCase
    {
        public string? GetBlockingReason(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry) => null;
        public void Execute(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry) { }
    }

    [Fact]
    public void View_LoadsWithoutResourceErrors()
    {
        var error = RunOnStaThread(() =>
        {
            // Application 인스턴스가 있어야 pack:// 리소스(Styles/Generic.xaml)가 해석된다.
            // DHBIMWATER.Application 네임스페이스와 이름이 겹쳐 정규화해 쓴다.
            if (System.Windows.Application.Current == null) _ = new System.Windows.Application();

            var vm = new DischargeBasinViewModel(new StubDialogService(), new StubCreateUseCase());
            vm.UpdatePumpReference(d: 1500, n: 4, pumpHwl: 5, h3: 1200, h7: 1000, b8PlusT5: 5000);

            var view = new DischargeBasinView { DataContext = vm };

            // 템플릿까지 실제로 펼쳐 봐야 DataTemplate 안의 StaticResource가 검증된다.
            view.Measure(new Size(1200, 900));
            view.Arrange(new Rect(0, 0, 1200, 900));
            view.UpdateLayout();
        });

        Assert.Null(error);
    }

    /// <summary>TB2 포커스 시 강조 Path가 평면 도해 위에 그려지는지 확인(docs/320).
    /// DHBIM_RENDER_DIR 지정 시 캡처 PNG 저장.</summary>
    [Theory]
    [InlineData(DischargeBasinViewModel.PositionRight, "discharge-tb2-right.png")]
    [InlineData(DischargeBasinViewModel.PositionLeft, "discharge-tb2-left.png")]
    public void PlanTab_DrawsTb2Highlight(string position, string fileName)
    {
        var error = RunOnStaThread(() =>
        {
            if (System.Windows.Application.Current == null) _ = new System.Windows.Application();

            var vm = new DischargeBasinViewModel(new StubDialogService(), new StubCreateUseCase());
            vm.UpdatePumpReference(d: 1500, n: 4, pumpHwl: 5, h3: 1200, h7: 1000, b8PlusT5: 5000);
            vm.SelectedPosition = position;
            vm.SetHint("TB2");

            var view = new DischargeBasinView { DataContext = vm };
            view.Measure(new Size(1440, 760));
            view.Arrange(new Rect(0, 0, 1440, 760));
            view.UpdateLayout();

            var tabs = Descendants(view).OfType<TabControl>().First();
            tabs.SelectedIndex = tabs.Items.Cast<TabItem>().ToList().FindIndex(t => (string)t.Header == "평면제원");
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            view.UpdateLayout();

            var path = Descendants(view).OfType<System.Windows.Shapes.Path>().First(p => p.Data is not null && p.Fill is SolidColorBrush { Color: { R: 0xC6, G: 0xFF } });
            Assert.True(path.ActualWidth > 0);

            if (Environment.GetEnvironmentVariable("DHBIM_RENDER_DIR") is { Length: > 0 } dir)
            {
                Capture(view, System.IO.Path.Combine(dir, fileName));

                // 도해 글자 크기 비교용 나머지 탭 캡처.
                foreach (var header in new[] { "종단제원", "설계조건" })
                {
                    tabs.SelectedIndex = tabs.Items.Cast<TabItem>().ToList().FindIndex(t => (string)t.Header == header);
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                    view.UpdateLayout();
                    Capture(view, System.IO.Path.Combine(dir, $"{header}-{fileName}"));
                }

                // 접힘 상태와 최소 창 크기 확인용.
                tabs.SelectedIndex = tabs.Items.Cast<TabItem>().ToList().FindIndex(t => (string)t.Header == "평면제원");
                vm.IsDiagramExpanded = false;
                view.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                view.UpdateLayout();
                Capture(view, System.IO.Path.Combine(dir, $"접힘-{fileName}"));

                vm.IsDiagramExpanded = true;
                view.Measure(new Size(1090, 480));
                view.Arrange(new Rect(0, 0, 1090, 480));
                view.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                view.UpdateLayout();
                Capture(view, System.IO.Path.Combine(dir, $"최소-{fileName}"));
            }
        });

        Assert.Null(error);
    }

    private static void Capture(FrameworkElement view, string path)
    {
        var bitmap = new RenderTargetBitmap(1440, 760, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    /// <summary>STA 스레드에서 실행하고, 예외가 나면 그대로 돌려준다.</summary>
    private static Exception? RunOnStaThread(Action action)
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(30)))
            return new TimeoutException("XAML 로드가 30초 안에 끝나지 않았습니다.");

        return captured;
    }
}
