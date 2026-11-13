using DHBIMWATER.UI.ViewModels.Modeling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DHBIMWATER.UI.Views.Modeling
{
    /// <summary>
    /// PumpingStationView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class PumpingStationView : Window
    {
        private const double PumpWidth = 1300;
        private const double PumpMinWidth = 900;
        private const double BasinWidth = 1500;
        private const double BasinMinWidth = 1150;
        // 접힌 폭. 입력·토글·안내 합 약 820에 여유(docs/321)
        private const double BasinCollapsedWidth = 900;

        // 도해 펼친 토출조 폭. 사용자가 끌어 바꾼 값도 여기 보관
        private double _basinExpandedWidth = BasinWidth;
        private bool _isBasinExpandedLayout;

        public PumpingStationView(PumpingStationViewModel pumpingStationViewModel)
        {
            InitializeComponent();
            DataContext = pumpingStationViewModel;
            pumpingStationViewModel.CloseAction = Close;
            pumpingStationViewModel.ConcreteLauncher.ShowDialog = vm => ConcreteSettingsView.Show(this, vm);   // docs/249 § E
            ContentRendered += (s, e) =>
            {
                SizeToContent = SizeToContent.Manual;
                SizeToContent = SizeToContent.Height;
            };

            // VM도 창마다 새로 생성(Transient)이라 구독 해제 불필요
            pumpingStationViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PumpingStationViewModel.IsBasinMode)) ApplyWindowWidth();
            };
            pumpingStationViewModel.DischargeBasin.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DischargeBasinViewModel.IsDiagramExpanded)) ApplyWindowWidth();
            };
            ApplyWindowWidth();
        }

        /// <summary>모드·도해 접힘 상태에 맞춰 창 폭 적용. 펼친 토출조에서 벗어날 때 그 폭 기억(docs/321).</summary>
        private void ApplyWindowWidth()
        {
            if (DataContext is not PumpingStationViewModel vm) return;

            // 폭 바꾸기 전이라 ActualWidth는 아직 끌어 놓은 값. 최대화 폭은 기억 안 함
            if (_isBasinExpandedLayout && WindowState == WindowState.Normal)
                _basinExpandedWidth = ActualWidth;

            var isBasin = vm.IsBasinMode;
            var isExpanded = vm.DischargeBasin.IsDiagramExpanded;
            _isBasinExpandedLayout = isBasin && isExpanded;

            // 최소 폭 먼저. 뒤로 두면 옛 최소 폭에 막혀 안 줄어듦
            MinWidth = !isBasin ? PumpMinWidth : isExpanded ? BasinMinWidth : BasinCollapsedWidth;
            Width = !isBasin ? PumpWidth : isExpanded ? _basinExpandedWidth : BasinCollapsedWidth;
        }

        private void OnParameterFocused(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PumpingStationViewModel vm) return;

            // 포커스 받은 요소에서 시각 트리를 따라 올라가며 string Tag(힌트 키)를 찾음
            // → TextBox 뿐 아니라 ComboBox(θ 등)도 호환
            var element = e.OriginalSource as DependencyObject;
            while (element != null)
            {
                if (element is FrameworkElement fe && fe.Tag is string key && !string.IsNullOrEmpty(key))
                {
                    vm.SetHint(key);
                    return;
                }
                element = VisualTreeHelper.GetParent(element);
            }
        }

        /// <summary>입력칸 벗어나면 도해 강조 해제. 다음 칸 GotFocus가 뒤따라 다시 설정(토출조와 같음).</summary>
        private void OnParameterLostFocus(object sender, RoutedEventArgs e)
            => (DataContext as PumpingStationViewModel)?.ClearFocusedParameter();

        /// <summary>
        /// 도해 클릭 → 그 자리 매개변수 글자 찾아 같은 탭의 같은 Tag 입력칸에 포커스·전체 선택.
        /// 포커스가 GotFocus(OnParameterFocused) 태워 강조·안내도 같이 바뀜. 좌표 = 도해 Grid = PNG 픽셀.
        /// 도해 Grid Tag: "Profile"(종단제원) / "Plan"(평면제원).
        /// </summary>
        private void OnDiagramMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (FindDiagramInput(sender, e) is not { } input) return;

            e.Handled = true;
            if (ExpandAncestors(input))
            {
                // 방금 펼친 칸은 아직 안 그려져 바로 Focus() 안 먹음 → 그린 뒤 포커스
                Dispatcher.BeginInvoke(() => FocusInput(input), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            FocusInput(input);
        }

        private static void FocusInput(Control input)
        {
            input.Focus();
            if (input is TextBox box) box.SelectAll();
        }

        /// <summary>입력 가능한 매개변수 글자 위면 손 모양 커서.</summary>
        private void OnDiagramMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement diagram)
                diagram.Cursor = FindDiagramInput(sender, e) != null ? Cursors.Hand : null;
        }

        /// <summary>도해 위 마우스 점의 매개변수 → 같은 탭에서 보이는 입력칸. 글자 아니거나 칸 없으면 null.</summary>
        private Control? FindDiagramInput(object sender, MouseEventArgs e)
        {
            if (DataContext is not PumpingStationViewModel vm || sender is not FrameworkElement diagram) return null;
            var point = e.GetPosition(diagram);
            var (key, tab) = diagram.Tag as string == "Plan"
                ? (vm.FindPlanParameterAt(point), (DependencyObject)PlanTabGrid)
                : (vm.FindProfileParameterAt(point), ProfileTabGrid);
            return key == null ? null : FindTaggedInput(tab, key);
        }

        /// <summary>
        /// 논리 트리에서 Tag가 key인 입력칸(TextBox·ComboBox) 찾음. 접힌 Expander 안도 찾음(시각 트리엔 없음).
        /// 유형별로 숨긴 칸(자기·조상 Visibility ≠ Visible)은 제외. 읽기 전용 칸도 포커스는 줌.
        /// </summary>
        private static Control? FindTaggedInput(DependencyObject root, string key)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is UIElement { Visibility: not Visibility.Visible }) continue;
                if (child is Control c && (c is TextBox || c is ComboBox) && c.Tag as string == key)
                    return c;
                if (FindTaggedInput(child, key) is { } found) return found;
            }
            return null;
        }

        /// <summary>입력칸을 감싼 접힌 Expander 펼침. 하나라도 펼쳤으면 true.</summary>
        private static bool ExpandAncestors(DependencyObject element)
        {
            var expanded = false;
            for (var node = LogicalTreeHelper.GetParent(element); node != null; node = LogicalTreeHelper.GetParent(node))
                if (node is Expander { IsExpanded: false } expander) { expander.IsExpanded = true; expanded = true; }
            return expanded;
        }
    }
}
