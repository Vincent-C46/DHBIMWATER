using System.Windows;
using DHBIMWATER.UI.ViewModels.Documentation;

namespace DHBIMWATER.UI.Views.Documentation;

public partial class PipeMaterialRefreshView : Window
{
    public PipeMaterialRefreshView(PipeMaterialRefreshViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
