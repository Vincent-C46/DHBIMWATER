using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DHBIMWATER.UI.ViewModels.Modeling;
namespace DHBIMWATER.UI.Views.Modeling;
public partial class PipeAlignmentModelingView : Window
{
    public PipeAlignmentModelingView(PipeAlignmentModelingViewModel viewModel) { InitializeComponent(); Loaded += (_, _) => MaxHeight = SystemParameters.WorkArea.Height; DataContext = viewModel; viewModel.CloseAction = Close; viewModel.OwnerWindow = this; viewModel.NavigatePreflightIssueAction = NavigatePreflightIssue; }

    private void NavigatePreflightIssue(PreflightIssue issue)
    {
        var tab = PipeAlignmentTabs.Items.OfType<TabItem>().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), issue.TargetTab.ToString(), StringComparison.Ordinal));
        if (tab is not null) PipeAlignmentTabs.SelectedItem = tab;
        if (issue.TargetTab != PipeAlignmentTab.SpecMapping || issue.HighlightKey is null) return;
        Dispatcher.BeginInvoke(() =>
        {
            var row = MappingGrid.Items.OfType<DiameterMappingRow>().FirstOrDefault(x => x.MappingKey == issue.HighlightKey);
            if (row is not null) MappingGrid.ScrollIntoView(row);
        });
    }

    /// <summary>[파일 추가 ▾] 드롭다운. WPF는 좌클릭으로 ContextMenu를 여는 기능이 없어 코드로 연다.</summary>
    private void AddKindDropdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is not { } menu) return;
        // ContextMenu는 비주얼 트리 밖이라 DataContext가 자동 상속되지 않는다. 넘겨주지 않으면 MenuItem의 AddCommand 바인딩이 비어 클릭이 먹지 않는다.
        menu.DataContext = button.DataContext;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void DirectionGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source) return;

        var checkBox = FindAncestor<CheckBox>(source);
        if (checkBox?.DataContext is not AlignmentDirectionRow clickedRow) return;

        // DataGrid는 체크박스 Click 전에 선택을 클릭한 한 행으로 축소한다. Preview 단계에서 기존 선택을
        // 사용해 값을 먼저 적용하고 기본 클릭을 막아 Ctrl·Shift 다중 선택도 그대로 유지한다.
        var selectedRows = grid.SelectedItems.OfType<AlignmentDirectionRow>().ToList();
        if (selectedRows.Count <= 1 || !selectedRows.Contains(clickedRow)) return;

        var isReversed = checkBox.IsChecked != true;
        foreach (var row in selectedRows) row.IsReversed = isReversed;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
}
