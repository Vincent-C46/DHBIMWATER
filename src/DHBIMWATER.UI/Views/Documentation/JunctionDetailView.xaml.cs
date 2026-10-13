using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DHBIMWATER.UI.ViewModels.Documentation;

namespace DHBIMWATER.UI.Views.Documentation;

public partial class JunctionDetailView : Window
{
    private const double MinZoom = 0.5, MaxZoom = 8, ZoomStep = 1.15;
    private bool _isPanning;
    private Point _panAnchor;
    // 빈 곳 첫 클릭이 해제한 세트 Id. 더블클릭이면 복원(docs/290)
    private long _selectionBeforeClick;

    public JunctionDetailView(JunctionDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = Close;
        Closed += (_, _) => viewModel.CloseAction = null;
    }

    // 미리보기 확대·이동은 보기 상태만 바꿈 → ViewModel 아닌 code-behind. 좌표: 부모 = 배율 × 로컬 + 이동
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var local = e.GetPosition(PreviewCanvas);
        var oldScale = PreviewScale.ScaleX;
        var newScale = Math.Clamp(e.Delta > 0 ? oldScale * ZoomStep : oldScale / ZoomStep, MinZoom, MaxZoom);
        // 커서 아래 점 고정
        PreviewTranslate.X += (oldScale - newScale) * local.X;
        PreviewTranslate.Y += (oldScale - newScale) * local.Y;
        PreviewScale.ScaleX = PreviewScale.ScaleY = newScale;
        e.Handled = true;
    }

    // 세트 카드 클릭 = 세트 선택(docs/290). 입력 컨트롤(토글·간격 칸) 클릭은 항상 선택, 그 외 재클릭은 해제.
    // Handled 안 함 → 토글·텍스트박스 기본 동작 유지
    private void OnSetCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not JunctionDetailViewModel vm || (sender as FrameworkElement)?.DataContext is not JunctionOffsetSetViewModel set) return;
        vm.SelectSet(set, toggle: !IsInsideInput(e.OriginalSource as DependencyObject, (DependencyObject)sender));
    }

    // Tab으로 간격 칸 들어가도 선택
    private void OnSetCardKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is not JunctionDetailViewModel vm || (sender as FrameworkElement)?.DataContext is not JunctionOffsetSetViewModel set) return;
        if (set.SetId != vm.SelectedSetId) vm.SelectSet(set, toggle: false);
    }

    private static bool IsInsideInput(DependencyObject? source, DependencyObject stop)
    {
        // Run 등 비시각 요소는 VisualTreeHelper 못 씀 → 논리 부모로 올라감
        for (var node = source; node is not null && node != stop;
             node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is TextBoxBase or ButtonBase) return true;
        return false;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = DataContext as JunctionDetailViewModel;
        // 캔버스 빈 곳 한 번 클릭 = 세트 선택 해제(docs/290). 해제 전 선택 기억 → 더블클릭이면 되돌림
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
        {
            // 부재 위 클릭이면 기억 지움 → 이전 클릭의 묵은 값으로 복원 안 되게
            var onEmpty = ReferenceEquals(e.OriginalSource, PreviewCanvas) && vm is not null;
            _selectionBeforeClick = onEmpty ? vm!.SelectedSetId : 0;
            if (onEmpty) vm!.ClearSetSelection();
        }
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            // 더블클릭 첫 클릭이 지운 선택 복원. 맞춤만 하고 하이라이트는 유지
            if (_selectionBeforeClick != 0) vm?.SelectSet(_selectionBeforeClick);
            _selectionBeforeClick = 0;
            // 더블클릭 = 화면 맞춤
            PreviewScale.ScaleX = PreviewScale.ScaleY = 1;
            PreviewTranslate.X = PreviewTranslate.Y = 0;
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Middle) return;
        _isPanning = true;
        _panAnchor = e.GetPosition(PreviewCanvas);
        PreviewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        if (e.MiddleButton != MouseButtonState.Pressed) { EndPan(); return; }
        // 잡은 로컬 점이 다시 커서 아래 오도록 이동. 이동 후 같은 점이라 기준점 갱신 불필요
        var local = e.GetPosition(PreviewCanvas);
        PreviewTranslate.X += (local.X - _panAnchor.X) * PreviewScale.ScaleX;
        PreviewTranslate.Y += (local.Y - _panAnchor.Y) * PreviewScale.ScaleY;
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        EndPan();
        e.Handled = true;
    }

    private void EndPan()
    {
        _isPanning = false;
        PreviewCanvas.ReleaseMouseCapture();
    }
}
