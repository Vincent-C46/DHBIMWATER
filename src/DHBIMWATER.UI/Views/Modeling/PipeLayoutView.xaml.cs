using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.ViewModels.Modeling;
using System.Windows;
using System.Windows.Input;

namespace DHBIMWATER.UI.Views.Modeling;

public partial class PipeLayoutView : Window
{
    private readonly PipeLayoutViewModel _viewModel;
    private bool _isPanning;
    private Point _lastPanPosition;
    public PipeLayoutView(PipeLayoutViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseAction = Close;
        viewModel.EdgeSelectedAction = FocusDiameterEditor;
        // 설정 창(패밀리 · 스냅 · 기준점)은 모달이며 같은 ViewModel에 바인딩된다(docs/175 § E-5, docs/185). 닫히면 본 창을 다시 앞으로.
        viewModel.OpenFamilySettingsAction = () => OpenSettings(0);
        // 수직분기 입면 창(docs/301 § D). 모달 — 닫힌 뒤 배치 ExternalEvent가 돈다.
        viewModel.OpenVerticalBranchDialog = dialogViewModel =>
        {
            var dialog = new VerticalBranchView(dialogViewModel) { Owner = this };
            var confirmed = dialog.ShowDialog() == true;
            Activate();
            return confirmed;
        };
        // 임시 도구(2026-09-18) — 캔버스를 프리셋 제출 양식 텍스트로 보여 준다. 유형별 기본값 확정 후 이 블록도 지운다.
        viewModel.OpenTemplateTextAction = text =>
        {
            var dialog = new PipeLayoutTemplateTextView(text) { Owner = this };
            dialog.ShowDialog();
            Activate();
        };
        // UI 개편 — 통합 부속 목록. 캔버스 ItemsControl과 같은 컬렉션이라 기본 뷰 말고 별도 뷰에 필터를 건다
        _fittingListView = new System.Windows.Data.ListCollectionView(viewModel.InlineFittings) { Filter = FilterFitting };
        FittingList.ItemsSource = _fittingListView;
        viewModel.InlineFittings.CollectionChanged += (_, _) => UpdateFittingFilterCounts();
        UpdateFittingFilterCounts();
        // 창 단위 안전망(docs/235 § B). 창이 열려 있는 동안만 구독한다.
        Dispatcher.UnhandledException += OnDispatcherUnhandledException;
        Closed += (_, _) => Dispatcher.UnhandledException -= OnDispatcherUnhandledException;
    }

    /// <summary>이벤트 핸들러에서 잡히지 않은 예외가 Revit 메시지 루프로 새는 것을 막는다(docs/235 § B).
    /// <para>이 디스패처는 Revit 메인 스레드의 것이라 Revit 자체 WPF와 공유한다 — 스택에 우리 UI 코드(<c>DHBIMWATER.UI</c>)가
    /// 있을 때만 처리하고, 나머지는 손대지 않고 흘려보낸다. 여러 DHBIMWATER 창이 동시에 구독해도 먼저 받은 창이 Handled로 끝낸다.</para></summary>
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Handled || e.Exception.StackTrace?.Contains("DHBIMWATER.UI", StringComparison.Ordinal) != true) return;
        e.Handled = true;
        DHBIMWATER.Infrastructure.Logging.LogManager.Logger.Error($"PipeLayout {DHBIMWATER.Shared.Errors.ErrorText.Format(e.Exception)}", e.Exception);
        try { _viewModel.ReportUnhandledError(e.Exception); }
        catch (Exception) { /* 상태 정리 중 또 실패해도 창은 살려 둔다 — 원래 예외는 이미 로그에 있다. */ }
    }
    private void OnLayoutCanvasSizeChanged(object sender, SizeChangedEventArgs e) => _viewModel.HandleCanvasSizeChanged(e.NewSize.Width, e.NewSize.Height);

    /// <summary>설정 창(모달, 같은 VM). <paramref name="tabIndex"/> 0=패밀리, 1=밸브 · 부관, 2=스냅 · 기준점.</summary>
    private void OpenSettings(int tabIndex)
    {
        var dialog = new PipeLayoutFamilySettingsView(_viewModel, tabIndex) { Owner = this };
        dialog.ShowDialog();
        Activate();
    }

    /// <summary>[부속] 탭 요약줄 "설정에서 변경" — 설정창 밸브 · 부관 탭으로 연다. 생성 중엔 명령과 같이 막는다.</summary>
    private void OnOpenFittingSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.OpenFamilySettingsCommand.CanExecute(null)) OpenSettings(1);
    }

    private const int FittingTabIndex = 2;
    private void OnValveToolClick(object sender, RoutedEventArgs e) { StepTabs.SelectedIndex = FittingTabIndex; FittingToolValve.IsChecked = true; }
    private void OnJointToolClick(object sender, RoutedEventArgs e) { StepTabs.SelectedIndex = FittingTabIndex; FittingToolJoint.IsChecked = true; }
    /// <summary>툴바 바이패스 — 칸 펼치고 세트 배치 켬. 토글 아님: 켜진 채 패널 버튼 누르면 꺼져 선 그리기로 빠지던 함정 막음. 끄기는 Esc·패널 버튼.</summary>
    private void OnBypassToolClick(object sender, RoutedEventArgs e)
    {
        StepTabs.SelectedIndex = FittingTabIndex;
        FittingToolBypass.IsChecked = true;
        if (_viewModel.ToggleBypassPlacementCommand.CanExecute(null)) _viewModel.IsPlacingBypassSet = true;
        LayoutCanvas.Focus();
    }

    /// <summary>⋯ 버튼 왼쪽 클릭으로 ContextMenu를 연다. 수동으로 열면 DataContext가 안 따라올 수 있어 버튼 것을 직접 넣는다.</summary>
    private void OnOpenContextMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } button) return;
        menu.DataContext = button.DataContext;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>부속 목록 행 ⋯ 메뉴 — 종전 행 버튼과 같은 VM 명령에 그 행 항목을 넘긴다.</summary>
    private void ExecuteFittingCommand(object sender, ICommand command)
    {
        if ((sender as FrameworkElement)?.DataContext is not InlineFittingItem item) return;
        if (command.CanExecute(item)) command.Execute(item);
    }
    private void OnFlipBypassClick(object sender, RoutedEventArgs e) => ExecuteFittingCommand(sender, _viewModel.FlipBypassSetCommand);
    private void OnSwapBypassValvesClick(object sender, RoutedEventArgs e) => ExecuteFittingCommand(sender, _viewModel.SwapBypassValvesCommand);
    private void OnApplyBypassValvesClick(object sender, RoutedEventArgs e) => ExecuteFittingCommand(sender, _viewModel.ApplyBypassValvesCommand);
    private void OnRemoveFittingClick(object sender, RoutedEventArgs e) => ExecuteFittingCommand(sender, _viewModel.RemoveFittingCommand);

    private readonly System.ComponentModel.ICollectionView _fittingListView;

    private bool FilterFitting(object obj)
    {
        if (obj is not InlineFittingItem item) return false;
        if (FittingFilterValve.IsChecked == true) return !item.IsJoint && !item.IsBypassSet;
        if (FittingFilterJoint.IsChecked == true) return item.IsJoint;
        if (FittingFilterBypass.IsChecked == true) return item.IsBypassSet;
        return true;
    }

    /// <summary>XAML 파싱 중(IsChecked="True")에도 불린다 — 뷰 만들기 전이면 건너뜀.</summary>
    private void OnFittingFilterChecked(object sender, RoutedEventArgs e) => _fittingListView?.Refresh();

    /// <summary>필터 칩에 개수 표시.</summary>
    private void UpdateFittingFilterCounts()
    {
        var items = _viewModel.InlineFittings;
        FittingFilterAll.Content = $"전체 {items.Count}";
        FittingFilterValve.Content = $"밸브 {items.Count(x => !x.IsJoint && !x.IsBypassSet)}";
        FittingFilterJoint.Content = $"이음관 {items.Count(x => x.IsJoint)}";
        FittingFilterBypass.Content = $"바이패스 {items.Count(x => x.IsBypassSet)}";
    }

    /// <summary>선택 카드 [삭제] — Delete 키와 같은 동작.</summary>
    private void OnDeleteSelectionClick(object sender, RoutedEventArgs e)
    {
        HandleCanvasShortcut(Key.Delete);
        LayoutCanvas.Focus();
    }

    /// <summary>선이 선택되면 캔버스 선택 카드의 관경 칸에 포커스·전체 선택(docs/233 § A-3). 종전엔 "3. 배관 기준값" 섹션을 펼쳤다.
    /// Dispatcher로 미루는 이유: 선 클릭 핸들러(<see cref="OnEdgeMouseLeftButtonDown"/>)가 VM 호출 뒤 <c>LayoutCanvas.Focus()</c>를 부르므로
    /// 동기로 옮기면 곧바로 빼앗긴다. 드래그 중에도 마우스는 캔버스가 캡처하고 있어 포커스 위치와 무관하다.
    /// 섹션 펼침은 기존 <see cref="OnSectionExpanded"/>가 나머지를 접는다.</summary>
    private void FocusDiameterEditor()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            // 카드는 HasCanvasSelection 바인딩으로 이미 보임(DataBind가 Input보다 먼저 처리)
            DiameterTextBox.Focus();
            DiameterTextBox.SelectAll();
            _diameterTextAtAutoFocus = DiameterTextBox.Text;
        });
    }

    /// <summary>자동 포커스 직후의 관경 칸 값(docs/239 § B). null이면 사용자가 직접 칸을 누른 것이다.</summary>
    private string? _diameterTextAtAutoFocus;

    /// <summary>자동 포커스된 관경 칸을 아직 고치지 않았으면 Delete·Ctrl+Z를 캔버스 동작(선 삭제·되돌리기)으로 넘긴다(docs/239 § B).
    /// 종전에는 선을 고르자마자 Delete가 관경 문자를 지워, 포커스가 빠지면 입력 오류로 생성이 막혔다. 숫자를 치기 시작하면 칸이 받는다.</summary>
    private void OnDiameterTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_diameterTextAtAutoFocus is null || DiameterTextBox.Text != _diameterTextAtAutoFocus) return;
        var isUndo = e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key != Key.Delete && !isUndo) return;
        _diameterTextAtAutoFocus = null;
        LayoutCanvas.Focus();
        HandleCanvasShortcut(e.Key);
        e.Handled = true;
    }

    private void OnDiameterTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _diameterTextAtAutoFocus = null;

    /// <summary>관경 칸에서 Enter = 적용 후 캔버스로 복귀, Esc = 입력 취소 후 복귀(docs/233 § A-2).
    /// 창 전체 <see cref="OnPreviewKeyDown"/>은 TextBox 원본이면 손대지 않으므로 여기서만 처리한다 — Esc가 선택 해제(HandleEscape)로 새지 않는다.</summary>
    private void OnDiameterTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Escape) return;
        var binding = System.Windows.Data.BindingOperations.GetBindingExpression(DiameterTextBox, System.Windows.Controls.TextBox.TextProperty);
        if (e.Key == Key.Enter) binding?.UpdateSource();   // UpdateSourceTrigger=LostFocus라 포커스 이동만으로도 적용되지만, 검증 오류 표시를 위해 명시 호출
        else binding?.UpdateTarget();                       // VM 값으로 되돌린다
        LayoutCanvas.Focus();
        e.Handled = true;
    }
    private void OnCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        LayoutCanvas.Focus();
        var position = e.GetPosition(LayoutCanvas);
        // 분할점 클릭은 선택 박스보다 먼저 가져간다 — 분할은 선택 모드에서 시작한다(docs/179 § H-7).
        if (_viewModel.IsSplittingEdge)
        {
            _viewModel.HandleCanvasClick(position);
            e.Handled = true;
            return;
        }
        if (_viewModel.IsSelectionMode)
        {
            _viewModel.BeginSelectionBox(position);
            Mouse.Capture(LayoutCanvas);
            e.Handled = true;
            return;
        }
        _viewModel.HandleCanvasClick(position);
    }
    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(LayoutCanvas);
        if (_isPanning)
        {
            if (e.MiddleButton != MouseButtonState.Pressed)
            {
                EndPan();
                return;
            }
            _viewModel.HandleCanvasPan(position.X - _lastPanPosition.X, position.Y - _lastPanPosition.Y);
            _lastPanPosition = position;
            return;
        }
        if (_viewModel.IsDraggingCandidate)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _viewModel.CancelDrag();
                EndSelectionCapture();
                return;
            }
            // Shift: 끝점을 끌 때 선을 수평·수직에 맞춘다(docs/181 § B-3). 입력 장치는 View만 본다.
            _viewModel.UpdateDrag(position, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }
        if (_viewModel.IsSelectionBoxVisible)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _viewModel.CancelSelectionBox();
                EndSelectionCapture();
                return;
            }
            _viewModel.UpdateSelectionBox(position);
            return;
        }
        _viewModel.HandleCanvasMove(position);
    }
    private void OnCanvasMouseLeave(object sender, MouseEventArgs e) => _viewModel.HandleCanvasLeave();
    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _viewModel.HandleCanvasZoom(e.GetPosition(LayoutCanvas), e.Delta);
        e.Handled = true;
    }
    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _isPanning = true;
        _lastPanPosition = e.GetPosition(LayoutCanvas);
        Mouse.Capture(LayoutCanvas);
        e.Handled = true;
    }
    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _viewModel.IsDraggingCandidate)
        {
            _viewModel.CommitDrag(e.GetPosition(LayoutCanvas), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            EndSelectionCapture();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && _viewModel.IsSelectionBoxVisible)
        {
            _viewModel.CommitSelectionBox(e.GetPosition(LayoutCanvas));
            EndSelectionCapture();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            EndPan();
            e.Handled = true;
        }
    }
    private void EndPan()
    {
        _isPanning = false;
        if (Mouse.Captured == LayoutCanvas) Mouse.Capture(null);
    }
    private void EndSelectionCapture()
    {
        if (!_isPanning && Mouse.Captured == LayoutCanvas) Mouse.Capture(null);
    }
    private void OnEdgeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 선택 여부는 "지금 그리는 중인가"가 아니라 모드로 판정한다.
        // 그리기 모드의 선 클릭은 T 접점 생성을 위해 캔버스 클릭으로 그대로 넘긴다.
        if (_viewModel.IsSelectionMode)
        {
            if (((FrameworkElement)sender).DataContext is PipeEdgeItem edge
                && _viewModel.TryBeginEdgeDrag(e.GetPosition(LayoutCanvas), edge.Id))
                Mouse.Capture(LayoutCanvas);
        }
        else _viewModel.HandleCanvasClick(e.GetPosition(LayoutCanvas));
        LayoutCanvas.Focus();
        e.Handled = true;
    }
    private void OnFittingMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is InlineFittingItem fitting
            && _viewModel.TryBeginFittingDrag(e.GetPosition(LayoutCanvas), fitting.Id))
            Mouse.Capture(LayoutCanvas);
        LayoutCanvas.Focus();
        e.Handled = true;
    }

    /// <summary>절점(끝점) 그립. 선택 모드에서만 hit test가 켜진다(docs/152 § E-5).</summary>
    private void OnNodeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PipeNodeItem node
            && _viewModel.TryBeginNodeDrag(e.GetPosition(LayoutCanvas), node.Id))
            Mouse.Capture(LayoutCanvas);
        LayoutCanvas.Focus();
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox) return;
        if (e.Key == Key.Escape) { _viewModel.HandleEscape(); EndSelectionCapture(); e.Handled = true; }
        else if (HandleCanvasShortcut(e.Key)) e.Handled = true;
    }

    /// <summary>캔버스 단축키(Ctrl+Z 되돌리기, Delete 삭제). 관경 칸에서 넘겨받을 때도 같은 동작을 쓴다(docs/239 § B).</summary>
    private bool HandleCanvasShortcut(Key key)
    {
        if (key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (_viewModel.UndoCommand.CanExecute(null)) _viewModel.UndoCommand.Execute(null);
            return true;
        }
        if (key == Key.Delete)
        {
            if (_viewModel.DeleteSelectedFittingCommand.CanExecute(null)) _viewModel.DeleteSelectedFittingCommand.Execute(null);
            else _viewModel.DeleteSelectedEdge();
            return true;
        }
        return false;
    }

    private void OnValidationError(object sender, System.Windows.Controls.ValidationErrorEventArgs e)
        => _viewModel.UpdateInputValidation(e.Action == System.Windows.Controls.ValidationErrorEventAction.Added);

    /// <summary>우측 패널에서 닫힌 콤보 위에 마우스를 두고 휠을 굴리면 WPF 기본 동작은 (포커스가 남아 있을 때) 콤보 선택을 한 칸 옮긴다 —
    /// 직경 파라미터가 소리 없이 이웃 항목(`주석 축척 사용` 등)으로 바뀌어 저장되던 원인이다(docs/232 § D).
    /// 닫힌 콤보의 휠은 콤보 대신 패널 ScrollViewer로 넘긴다. 드롭다운이 열려 있으면 목록 스크롤이므로 건드리지 않는다.
    /// 이 창만 적용한다 — 공용 PrimaryComboBoxStyle과 설정 창(PipeLayoutFamilySettingsView)은 범위 밖(같은 위험은 있다).</summary>
    private void OnPanelPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not System.Windows.Controls.ScrollViewer panel) return;
        var combo = FindAncestorComboBox(e.OriginalSource as DependencyObject);
        if (combo is null || combo.IsDropDownOpen) return;
        e.Handled = true;
        panel.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = panel });
    }

    private static System.Windows.Controls.ComboBox? FindAncestorComboBox(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is System.Windows.Controls.ComboBox combo) return combo;
            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }
}
