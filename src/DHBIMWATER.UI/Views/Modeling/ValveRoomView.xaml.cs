using DHBIMWATER.UI.ViewModels.Modeling;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace DHBIMWATER.UI.Views.Modeling;

public partial class ValveRoomView : Window
{
    // 피킹 왕복(ValveRoomCommand:23-24의 Hide→Show)에서 다시 돌면 사용자가 치던 칸에서 캐럿을 뺏는다.
    // 창 수명 동안 1회만 수행한다.
    private bool _imeFocusPrimed;

    public ValveRoomView(ValveRoomViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = Close;
        // Revit 위의 모델리스 WPF 창은 떠도 WPF 키보드 포커스가 비어 있어, 사용자가 처음 클릭한 TextBox가
        // IME(TSF) 컨텍스트를 못 받는다 → 한글 조합이 깨지고 백스페이스가 텍스트 전체를 지운다(docs/182 § 2).
        // 포커스를 한 번 옮겼다 돌아오면 정상화되는 것이 확인됐으므로, 그 왕복을 창이 뜰 때 자동으로 한 번 한다.
        // Loaded가 아니라 ContentRendered인 이유: 창이 실제로 화면에 올라가 활성화된 뒤여야 포커스가 붙는다.
        ContentRendered += OnContentRenderedPrimeIme;

        // 화면(작업 영역)을 넘지 않도록 상한을 건다. 높이는 기존 900 상한과 작업 영역 높이 중 작은 값.
        var workArea = SystemParameters.WorkArea;
        MaxWidth = workArea.Width;
        MaxHeight = Math.Min(MaxHeight, workArea.Height);
        ApplyImagePanel(ImagePanelToggle.IsChecked == true);
    }

    /// <summary>숫자 칸 변환 오류를 VM에 알린다 — 오류가 남아 있으면 [생성]을 막는다(docs/238 § B). PipeLayoutView와 같은 방식.</summary>
    private void OnValidationError(object sender, System.Windows.Controls.ValidationErrorEventArgs e)
    {
        if (DataContext is ValveRoomViewModel viewModel)
            viewModel.UpdateInputValidation(e.Action == System.Windows.Controls.ValidationErrorEventAction.Added);
    }

    // 입력 영역(왼쪽 열) 폭 — 기존 창 1080에서 참고도 340을 뺀 값. 밸브실 종류·용도 버튼이 한 줄에 들어가는 폭.
    private const double InputColumnWidth = 740;
    // 참고도 열 최대 폭 — 기존 340의 2배. 화면이 좁으면 남는 폭만큼만 쓴다.
    private const double ImageColumnMaxWidth = 680;

    private void OnImagePanelToggleChanged(object sender, RoutedEventArgs e)
    {
        // XAML의 IsChecked="True"가 InitializeComponent 도중 Checked를 올린다 — 그때는 생성자가 따로 적용한다.
        if (ImageColumn is null || ImagePanel is null) return;
        ApplyImagePanel(ImagePanelToggle.IsChecked == true);
    }

    /// <summary>참고도를 펼치거나 접고, 창 폭을 작업 영역 안으로 맞춘다.</summary>
    private void ApplyImagePanel(bool expanded)
    {
        var workArea = SystemParameters.WorkArea;
        var imageWidth = expanded
            ? Math.Max(0, Math.Min(ImageColumnMaxWidth, workArea.Width - InputColumnWidth))
            : 0;

        ImagePanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ImageColumn.Width = new GridLength(imageWidth);
        Width = Math.Min(InputColumnWidth + imageWidth, workArea.Width);

        // 창이 이미 떠 있을 때 펼치면 오른쪽으로 늘어나 화면 밖으로 나갈 수 있다 → 왼쪽으로 당긴다.
        // (첫 표시 전에는 Left가 NaN — CenterScreen이 자리를 잡는다.)
        if (!double.IsNaN(Left) && Left + Width > workArea.Right)
            Left = Math.Max(workArea.Left, workArea.Right - Width);
    }

    private void OnContentRenderedPrimeIme(object? sender, EventArgs e)
    {
        if (_imeFocusPrimed) return;
        _imeFocusPrimed = true;
        // 구조 형식이 미구현(철제밸브실)이면 두 칸 모두 IsEnabled=False라 포커스를 못 받는다 — 그럴 땐 건너뛴다.
        if (!PipelineSectionBox.IsEnabled || !StationPointBox.IsEnabled) return;

        Activate();
        Keyboard.Focus(StationPointBox);
        // 같은 디스패처 패스에서 연달아 옮기면 왕복이 한 번으로 합쳐질 수 있어 다음 패스로 미룬다.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Keyboard.Focus(PipelineSectionBox);
            // 복원된 값이 있을 때 캐럿을 끝에 둔다 — 창을 열자마자 타이핑해 기존 값이 날아가지 않도록.
            PipelineSectionBox.CaretIndex = PipelineSectionBox.Text.Length;
        }), DispatcherPriority.Input);
    }
}
