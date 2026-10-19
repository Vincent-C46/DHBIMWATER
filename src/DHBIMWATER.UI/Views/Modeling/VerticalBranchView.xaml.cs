using DHBIMWATER.UI.ViewModels.Modeling;
using System.Windows;

namespace DHBIMWATER.UI.Views.Modeling;

/// <summary>수직분기 입면 창(docs/301 § D). 모달 — [Revit에 배치]면 DialogResult = true.</summary>
public partial class VerticalBranchView : Window
{
    public VerticalBranchView(VerticalBranchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = confirmed => DialogResult = confirmed;
    }
}
