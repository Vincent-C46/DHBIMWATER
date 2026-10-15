using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DHBIMWATER.UI.ViewModels.Documentation;

namespace DHBIMWATER.UI.Views.Documentation;

public partial class JunctionDetailView : Window
{
    private const double MinZoom = 0.5, MaxZoom = 15, ZoomStep = 1.15;
    private bool _isPanning;
    private Point _panAnchor;
    // 빈 곳 첫 클릭이 해제한 세트 Id. 더블클릭이면 복원(docs/290)
    private long _selectionBeforeClick;
    // 미리보기 선 목표 굵기(화면 px). XAML Stroke.* 초기값과 같게 유지(docs/294)
    private static readonly (string Key, double Px)[] StrokeTargets =
    [
        ("Stroke.Symbol", 2), ("Stroke.SymbolHighlight", 3), ("Stroke.Wall", 1.5),
        ("Stroke.Center", 1.2), ("Stroke.CenterHighlight", 2), ("Stroke.Tag", 1.4), ("Stroke.TagHighlight", 2),
        ("Stroke.Dimension", 1), ("Stroke.DimensionHighlight", 1.6), ("Stroke.Thin", 1),
    ];
    private double _strokeScale = 1;

    public JunctionDetailView(JunctionDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = Close;
        Closed += (_, _) => viewModel.CloseAction = null;
        Loaded += (_, _) => UpdateStrokeScale();
        viewModel.SetFocusRequested += FitToRect;
    }

    // 세트 선택 시 캔버스를 세트 범위에 맞춤(docs/295 후속). 좌표: 부모 = 배율 × 로컬 + 이동.
    // 보이는 영역 = 캔버스 0~Width × 0~Height(Viewbox가 캔버스 전체를 창에 맞춤)
    private void FitToRect(Rect bounds)
    {
        double w = PreviewCanvas.Width, h = PreviewCanvas.Height;
        if (!(w > 0 && h > 0) || bounds.IsEmpty) return;
        // 여백 20%. 점 하나짜리 세트도 너무 확대 안 되게 최소 크기 둠
        var bw = Math.Max(bounds.Width, 40) * 1.2;
        var bh = Math.Max(bounds.Height, 40) * 1.2;
        var scale = Math.Clamp(Math.Min(w / bw, h / bh), MinZoom, MaxZoom);
        var cx = bounds.X + bounds.Width / 2;
        var cy = bounds.Y + bounds.Height / 2;
        PreviewScale.ScaleX = PreviewScale.ScaleY = scale;
        PreviewTranslate.X = w / 2 - scale * cx;
        PreviewTranslate.Y = h / 2 - scale * cy;
        UpdateStrokeScale();
    }

    private void OnPreviewLayoutChanged(object sender, SizeChangedEventArgs e) => UpdateStrokeScale();

    // 캔버스→화면 총 배율 = Viewbox 축소 × 휠 확대. 굵기 = 목표 ÷ 배율 → 화면 굵기 고정(docs/294)
    private void UpdateStrokeScale()
    {
        // 탭 숨김 등 크기 0이면 건너뜀. 다시 보이면 SizeChanged로 재계산
        if (PreviewHost.ActualWidth <= 0 || PreviewHost.ActualHeight <= 0 || PreviewCanvas.Width <= 0 || PreviewCanvas.Height <= 0) return;
        // Viewbox Uniform 배율 = 가로·세로 비 중 작은 쪽. Viewbox가 PreviewHost 꽉 채움
        var fit = Math.Min(PreviewHost.ActualWidth / PreviewCanvas.Width, PreviewHost.ActualHeight / PreviewCanvas.Height);
        var scale = fit * PreviewScale.ScaleX;
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0 || Math.Abs(scale - _strokeScale) < 1e-3) return;
        _strokeScale = scale;
        foreach (var (key, px) in StrokeTargets) Resources[key] = px / scale;
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
        UpdateStrokeScale();
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
            UpdateStrokeScale();
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
