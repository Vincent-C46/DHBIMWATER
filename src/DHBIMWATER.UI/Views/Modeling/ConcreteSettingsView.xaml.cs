using System.Windows;
using DHBIMWATER.UI.ViewModels.Modeling;

namespace DHBIMWATER.UI.Views.Modeling;

/// <summary>공용 콘크리트 강도 설정창(docs/249 § E). 모달로 띄우고 DialogResult로 확인 여부를 돌려준다.</summary>
public partial class ConcreteSettingsView : Window
{
    public ConcreteSettingsView(ConcreteSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = result => DialogResult = result;
        Closed += (_, _) => viewModel.CloseAction = null;
    }

    /// <summary>호스트 창 코드비하인드가 <see cref="ConcreteSettingsLauncher.ShowDialog"/>에 연결한다.</summary>
    public static bool Show(Window owner, ConcreteSettingsViewModel viewModel)
    {
        var dialog = new ConcreteSettingsView(viewModel) { Owner = owner };
        var result = dialog.ShowDialog() == true;
        owner.Activate();
        return result;
    }
}
