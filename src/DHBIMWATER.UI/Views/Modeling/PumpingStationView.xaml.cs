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
    }
}
