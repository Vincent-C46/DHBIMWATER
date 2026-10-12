using System.Windows;
using System.Windows.Input;
using DHBIMWATER.UI.ViewModels.Documentation;

namespace DHBIMWATER.UI.Views.Documentation;

public partial class JunctionDetailView : Window
{
    private const double MinZoom = 0.5, MaxZoom = 8, ZoomStep = 1.15;
    private bool _isPanning;
    private Point _panAnchor;

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

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
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
