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
    public class WaterTankCommand : CommandBase
    {
        // 재실행 시 새 창 대신 열린 창 활성화(docs/320 § B)
        private static WaterTankView? _openView;

        protected override Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (_openView is not null)
            {
                _openView.Activate();
                return Result.Succeeded;
            }

            var view = ServiceContainer.GetService<WaterTankView>();
            var viewModel = (WaterTankViewModel)view.DataContext;
            var runner = new ModelessRevitRunner(commandData.Application, "WaterTank", "배수지", view);
            viewModel.RunInRevitContext = runner.RunCreate;
            viewModel.ConcreteLauncher.RunInRevitContext = runner.Run;

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
