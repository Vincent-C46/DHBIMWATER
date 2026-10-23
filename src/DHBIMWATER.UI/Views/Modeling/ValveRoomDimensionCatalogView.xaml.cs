using System.Windows;
using DHBIMWATER.UI.ViewModels.Modeling;

namespace DHBIMWATER.UI.Views.Modeling;

/// <summary>밸브실 제원표 편집 창(docs/312). 모달로 띄우고 DialogResult로 확인 여부를 돌려준다.</summary>
public partial class ValveRoomDimensionCatalogView : Window
{
    public ValveRoomDimensionCatalogView(ValveRoomDimensionCatalogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = result => DialogResult = result;
        Closed += (_, _) => viewModel.CloseAction = null;
    }

    /// <summary>호스트 창 코드비하인드가 <see cref="ValveRoomDimensionCatalogLauncher.ShowDialog"/>에 연결한다.</summary>
    public static bool Show(Window owner, ValveRoomDimensionCatalogViewModel viewModel)
    {
        var dialog = new ValveRoomDimensionCatalogView(viewModel) { Owner = owner };
        var result = dialog.ShowDialog() == true;
        owner.Activate();
        return result;
    }
}
