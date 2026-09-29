using DHBIMWATER.UI.ViewModels.Modeling;
using System.Windows;
using System.Windows.Controls;
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
    }
}
