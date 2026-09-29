using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.UI.ViewModels.Modeling;
using DHBIMWATER.UI.Views.Modeling;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace DHBIMWATER.UI.Tests.Views;

/// <summary>
/// XAML 로드 스모크 테스트.
/// BAML 컴파일만으로는 StaticResource 키 누락·컨버터 미등록이 드러나지 않고 실행 시점에야 터진다.
/// Revit에서 창을 띄워야 알 수 있는 문제를 여기서 먼저 걸러낸다.
/// </summary>
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
