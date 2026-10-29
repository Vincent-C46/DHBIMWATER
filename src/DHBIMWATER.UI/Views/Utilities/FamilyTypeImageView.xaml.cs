using System.Windows;
using DHBIMWATER.UI.ViewModels.Utilities;

namespace DHBIMWATER.UI.Views.Utilities;

public partial class FamilyTypeImageView : Window
{
    public FamilyTypeImageView(FamilyTypeImageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
