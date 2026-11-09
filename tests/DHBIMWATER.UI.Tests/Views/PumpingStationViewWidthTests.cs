using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.UI.ViewModels.Modeling;
using DHBIMWATER.UI.Views.Modeling;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace DHBIMWATER.UI.Tests.Views;

/// <summary>
/// 토출조 도해 접기와 창 폭 연동 확인(docs/321).
/// 사용자 끌기는 Width 속성을 거치지 않으므로 SetWindowPos로 재현한다.
/// DHBIM_RENDER_DIR 지정 시 접힌 세 탭 캡처 PNG 저장.
/// </summary>
[Collection("WpfApplication")]
public class PumpingStationViewWidthTests
{
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

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
    public void DiagramToggle_ShrinksWindowAndRestoresDraggedWidth()
    {
        var error = RunOnStaThread(() =>
        {
            // 다른 테스트 클래스가 병렬로 먼저 만들 수 있어 충돌 무시
            try { if (System.Windows.Application.Current == null) _ = new System.Windows.Application(); }
            catch (InvalidOperationException) { }

            var dialog = new StubDialogService();
            var basin = new DischargeBasinViewModel(dialog, new StubCreateUseCase());
            var vm = new PumpingStationViewModel(null!, dialog, null!, null!, null!, null!, basin);
            var view = new PumpingStationView(vm);
            view.Show();
            try
            {
                Pump();
                Assert.Equal(1300, view.ActualWidth, 0);

                vm.SelectedMode = PumpingStationViewModel.ModeDischargeBasin;
                Pump();
                Assert.Equal(1500, view.ActualWidth, 0);

                Drag(view, 1700);
                Assert.Equal(1700, view.ActualWidth, 0);

                basin.IsDiagramExpanded = false;
                Pump();
                Assert.Equal(900, view.ActualWidth, 0);
                Assert.Equal(900, view.MinWidth);
                SaveCollapsedTabs(view);

                basin.IsDiagramExpanded = true;
                Pump();
                Assert.Equal(1700, view.ActualWidth, 0);
                Assert.Equal(1150, view.MinWidth);

                // 접은 채 모드 왕복
                basin.IsDiagramExpanded = false;
                Pump();
                vm.SelectedMode = PumpingStationViewModel.ModePumpingStation;
                Pump();
                Assert.Equal(1300, view.ActualWidth, 0);
                vm.SelectedMode = PumpingStationViewModel.ModeDischargeBasin;
                Pump();
                Assert.Equal(900, view.ActualWidth, 0);

                // 접힌 상태에서 끌어도 펼친 폭은 그대로
                Drag(view, 1000);
                basin.IsDiagramExpanded = true;
                Pump();
                Assert.Equal(1700, view.ActualWidth, 0);

                // 끌어서 줄인 뒤 다시 같은 접힘 폭으로 가는지
                basin.IsDiagramExpanded = false;
                Pump();
                Drag(view, 1000);
                vm.SelectedMode = PumpingStationViewModel.ModePumpingStation;
                Pump();
                vm.SelectedMode = PumpingStationViewModel.ModeDischargeBasin;
                Pump();
                Assert.Equal(900, view.ActualWidth, 0);
            }
            finally
            {
                view.Close();
            }
        });

        Assert.Null(error);
    }

    private static void Drag(Window view, double width)
    {
        var scale = VisualTreeHelper.GetDpi(view).DpiScaleX;
        var height = (int)Math.Round(view.ActualHeight * VisualTreeHelper.GetDpi(view).DpiScaleY);
        SetWindowPos(new WindowInteropHelper(view).Handle, IntPtr.Zero, 0, 0,
            (int)Math.Round(width * scale), height, SwpNoMove | SwpNoZOrder | SwpNoActivate);
        Pump();
    }

    private static void SaveCollapsedTabs(Window view)
    {
        var dir = Environment.GetEnvironmentVariable("DHBIM_RENDER_DIR");
        if (string.IsNullOrEmpty(dir)) return;

        var tabs = FindDescendant<DischargeBasinView>(view) is { } basinView ? FindDescendant<TabControl>(basinView) : null;
        Assert.NotNull(tabs);
        for (var i = 0; i < tabs!.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            Pump();
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(dir, $"basin-collapsed-{i}.png"));
            encoder.Save(stream);
        }
        tabs.SelectedIndex = 0;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static Exception? RunOnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error;
    }
}
