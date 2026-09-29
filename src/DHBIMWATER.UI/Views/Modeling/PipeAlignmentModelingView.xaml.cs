using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DHBIMWATER.UI.ViewModels.Modeling;
namespace DHBIMWATER.UI.Views.Modeling;
public partial class PipeAlignmentModelingView : Window
{
    public PipeAlignmentModelingView(PipeAlignmentModelingViewModel viewModel)
    {
        InitializeComponent();
        Loaded += (_, _) => MaxHeight = SystemParameters.WorkArea.Height;
        DataContext = viewModel;
        viewModel.CloseAction = Close;
        viewModel.OwnerWindow = this;
        viewModel.NavigatePreflightIssueAction = NavigatePreflightIssue;
        // 배치 중 창을 닫으면 Revit 스레드의 배치는 계속 돌고 결과만 버려졌다(docs/209 § A-5). 끝나거나 취소한 뒤에만 닫힌다.
        Closing += (_, e) => { if (viewModel.IsBusy) { e.Cancel = true; viewModel.NotifyCloseBlocked(); } };
    }

    private void NavigatePreflightIssue(PreflightIssue issue)
    {
        var tab = PipeAlignmentTabs.Items.OfType<TabItem>().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), issue.TargetTab.ToString(), StringComparison.Ordinal));
        if (tab is not null) PipeAlignmentTabs.SelectedItem = tab;
        if (issue.TargetTab == PipeAlignmentTab.Exclusion && DataContext is PipeAlignmentModelingViewModel { Exclusions.SelectedRow: { } selected })
        {
            Dispatcher.BeginInvoke(() => ExclusionGrid.ScrollIntoView(selected));
            return;
        }
        if (issue.TargetTab != PipeAlignmentTab.SpecMapping || issue.HighlightKey is null) return;
        Dispatcher.BeginInvoke(() =>
        {
            var row = MappingGrid.Items.OfType<DiameterMappingRow>().FirstOrDefault(x => x.MappingKey == issue.HighlightKey);
            if (row is not null) MappingGrid.ScrollIntoView(row);
        });
    }

    /// <summary>
    /// 배치 제외 구간 그리드에 엑셀 행(탭 구분) 붙여넣기(docs/219). 셀 편집 중(TextBox)이면 일반 붙여넣기에 맡긴다.
    /// </summary>
    private void ExclusionGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.OriginalSource is TextBox || DataContext is not PipeAlignmentModelingViewModel vm) return;
        string text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty; }
        catch (System.Runtime.InteropServices.ExternalException) { return; }
        if (vm.Exclusions.Paste(text) > 0) e.Handled = true;
    }
}
