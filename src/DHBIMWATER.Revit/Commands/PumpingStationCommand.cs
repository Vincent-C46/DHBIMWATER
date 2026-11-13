using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Revit.DependencyInjection;
using DHBIMWATER.UI.ViewModels.Modeling;
using DHBIMWATER.UI.Views.Modeling;
using System.Windows.Interop;

namespace DHBIMWATER.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class PumpingStationCommand : CommandBase
    {
        // 재실행 시 새 창 대신 열린 창 활성화. ModelessWindowGate는 전역 1창 제한이라 안 씀(docs/320 § B)
        private static PumpingStationView? _openView;

        protected override Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (_openView is not null)
            {
                _openView.Activate();
                return Result.Succeeded;
            }

            var view = ServiceContainer.GetService<PumpingStationView>();
            var viewModel = (PumpingStationViewModel)view.DataContext;
            var runner = new ModelessRevitRunner(commandData.Application, "PumpingStation", "펌프장", view);
            viewModel.RunInRevitContext = runner.RunCreate;
            viewModel.DischargeBasin.RunInRevitContext = runner.RunCreate;
            viewModel.ConcreteLauncher.RunInRevitContext = runner.Run;
            // 같은 프로젝트에 저장된 입력값 복원(명령 실행 중 = API 컨텍스트)
            viewModel.RestoreSavedInputs();
            // 닫을 때 바뀐 값 저장. 창 연 문서가 활성일 때만(다른 문서에 잘못 쓰지 않게)
            view.Closing += (_, _) => runner.RunOnOwner(viewModel.SaveInputsIfChanged);

            new WindowInteropHelper(view).Owner = commandData.Application.MainWindowHandle;
            _openView = view;
            view.Closed += (_, _) => _openView = null;
            view.Show();
            // 최초 표시도 활성화 — WPF 포커스/IME 초기화 전제(docs/182 § 2-2)
            view.Activate();
            return Result.Succeeded;
        }
    }
}
