using System.Windows;
using DHBIMWATER.UI.ViewModels.Modeling;

namespace DHBIMWATER.UI.Views.Modeling;

/// <summary>PipeLayout 패밀리·파라미터 설정 창(docs/175 § E-5). DataContext는 <see cref="PipeLayoutViewModel"/> 그대로다.</summary>
public partial class PipeLayoutFamilySettingsView : Window
{
    /// <param name="initialTabIndex">처음 열 탭. 0=패밀리, 1=밸브 · 부관, 2=스냅 · 기준점.</param>
    public PipeLayoutFamilySettingsView(PipeLayoutViewModel viewModel, int initialTabIndex = 0)
    {
        InitializeComponent();
        if (initialTabIndex >= 0 && initialTabIndex < SettingsTabs.Items.Count) SettingsTabs.SelectedIndex = initialTabIndex;
        DataContext = viewModel;
        // [저장]은 창을 닫아야 저장이 실행된다(모달 창이 떠 있는 동안 ExternalEvent가 처리되지 않음). 닫히면 위임을 거둔다.
        viewModel.CloseFamilySettingsAction = Close;
        Closed += (_, _) =>
        {
            viewModel.CloseFamilySettingsAction = null;
            viewModel.SettingsInputErrorCount = 0;   // 오류 칸 값은 VM에 들어오지 않았다 — 닫으면 막을 이유가 없다(docs/239 § A)
        };
        _viewModel = viewModel;
    }

    private readonly PipeLayoutViewModel _viewModel;

    /// <summary>숫자 칸 변환 오류를 세어 [지금 저장]을 막는다(docs/239 § A). 종전에는 오류를 알리지 않고 옛 값을 저장했다.</summary>
    private void OnValidationError(object sender, System.Windows.Controls.ValidationErrorEventArgs e)
        => _viewModel.SettingsInputErrorCount += e.Action == System.Windows.Controls.ValidationErrorEventAction.Added ? 1 : -1;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
