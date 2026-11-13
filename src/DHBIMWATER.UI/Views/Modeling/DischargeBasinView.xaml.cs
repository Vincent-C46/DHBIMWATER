using DHBIMWATER.UI.ViewModels.Modeling;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DHBIMWATER.UI.Views.Modeling
{
    /// <summary>
    /// DischargeBasinView.xaml에 대한 상호 작용 논리.
    /// PumpingStationView 안에 호스팅되는 UserControl이며, DataContext는 DischargeBasinViewModel이다.
    /// </summary>
    public partial class DischargeBasinView : UserControl
    {
        public DischargeBasinView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 포커스 받은 요소에서 시각 트리를 따라 올라가며 string Tag(힌트 키)를 찾는다.
        /// PumpingStationView.xaml.cs 의 같은 이름 핸들러와 동일한 방식.
        /// </summary>
        private void OnParameterFocused(object sender, RoutedEventArgs e)
        {
            if (DataContext is not DischargeBasinViewModel vm) return;

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

        /// <summary>입력칸을 벗어나면 도해 강조 해제. 다음 칸 GotFocus가 뒤따라 다시 설정.</summary>
        private void OnParameterLostFocus(object sender, RoutedEventArgs e)
            => (DataContext as DischargeBasinViewModel)?.ClearFocusedParameter();

        /// <summary>
        /// 도해 클릭 → 그 자리 매개변수 글자 찾아 같은 탭의 같은 Tag 입력칸에 포커스·전체 선택(펌프장과 같은 동작).
        /// 도해 Grid Tag: "Plan"/"Profile"/"Opening". 좌표 = 도해 Grid(1800폭) = 경로표 단위.
        /// 입력칸 대부분은 ItemsControl 템플릿(ValueRowTemplate)이 만든 것이라 행(DischargeBasinValueRow)으로 찾고,
        /// 접힌 Expander면 펼쳐 그린 뒤 컨테이너 안 TextBox에 포커스.
        /// </summary>
        private void OnDiagramMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (FindDiagramTarget(sender, e) is not { } target) return;

            e.Handled = true;
            if (ExpandAncestors(target.Owner))
            {
                // 방금 펼친 칸은 아직 안 그려져 바로 Focus() 안 먹음 → 그린 뒤 포커스
                Dispatcher.BeginInvoke(() => FocusInput(target.Resolve()), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            FocusInput(target.Resolve());
        }

        /// <summary>입력 가능한 매개변수 글자 위면 손 모양 커서.</summary>
        private void OnDiagramMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement diagram)
                diagram.Cursor = FindDiagramTarget(sender, e) != null ? Cursors.Hand : null;
        }

        /// <summary>찾은 입력칸. Owner = 펼칠 Expander 찾는 기준 요소, Resolve = 포커스 시점에 실제 입력칸 반환.</summary>
        private sealed record InputTarget(DependencyObject Owner, Func<Control?> Resolve);

        private InputTarget? FindDiagramTarget(object sender, MouseEventArgs e)
        {
            if (DataContext is not DischargeBasinViewModel vm || sender is not FrameworkElement diagram) return null;
            if (vm.FindParameterAt(diagram.Tag as string ?? string.Empty, e.GetPosition(diagram)) is not { } key) return null;
            return TabRoot(diagram) is { } tab ? FindTarget(tab, key) : null;
        }

        /// <summary>도해가 든 탭의 내용 루트(부모가 TabItem인 요소).</summary>
        private static DependencyObject? TabRoot(DependencyObject element)
        {
            for (var node = element; node != null; node = LogicalTreeHelper.GetParent(node))
                if (LogicalTreeHelper.GetParent(node) is TabItem) return node;
            return null;
        }

        private static void FocusInput(Control? input)
        {
            if (input == null) return;
            input.Focus();
            if (input is TextBox box) box.SelectAll();
        }

        /// <summary>
        /// 논리 트리에서 key 입력칸 찾음. 직접 놓인 TextBox·ComboBox는 Tag로, ItemsControl은 보이는 행의 Key로.
        /// 숨긴 칸(자기·조상 Visibility ≠ Visible)은 제외. 접힌 Expander 안도 찾음.
        /// </summary>
        private static InputTarget? FindTarget(DependencyObject root, string key)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is UIElement { Visibility: not Visibility.Visible }) continue;
                if (child is Control c && (c is TextBox || c is ComboBox) && c.Tag as string == key)
                    return new InputTarget(c, () => c);
                if (child is ItemsControl items && items is not ComboBox)
                {
                    var row = items.Items.OfType<DischargeBasinValueRow>().FirstOrDefault(r => r.Key == key && r.IsVisible);
                    if (row != null)
                        return new InputTarget(items, () =>
                            items.ItemContainerGenerator.ContainerFromItem(row) is DependencyObject container
                                ? FindVisualTextBox(container, key) : null);
                    continue;
                }
                if (FindTarget(child, key) is { } found) return found;
            }
            return null;
        }

        private static TextBox? FindVisualTextBox(DependencyObject root, string key)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBox box && box.Tag as string == key) return box;
                if (FindVisualTextBox(child, key) is { } found) return found;
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
