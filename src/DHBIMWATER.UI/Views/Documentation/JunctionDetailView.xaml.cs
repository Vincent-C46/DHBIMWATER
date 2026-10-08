using System.Windows;
using DHBIMWATER.UI.ViewModels.Documentation;

namespace DHBIMWATER.UI.Views.Documentation;

public partial class JunctionDetailView : Window
{
    public JunctionDetailView(JunctionDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = Close;
        Closed += (_, _) => viewModel.CloseAction = null;
    }
}
