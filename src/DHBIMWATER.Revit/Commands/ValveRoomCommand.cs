using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Revit.DependencyInjection;
using DHBIMWATER.UI.ViewModels.Modeling;
using DHBIMWATER.UI.Views.Modeling;
using System.Windows.Interop;

namespace DHBIMWATER.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public class ValveRoomCommand : CommandBase
{
    protected override Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var view = ServiceContainer.GetService<ValveRoomView>();
        var viewModel = (ValveRoomViewModel)view.DataContext;
        var handler = new ValveRoomRequestHandler(ServiceContainer.GetService<CreateValveRoomUseCase>(), ServiceContainer.GetService<IDrainPitLocationPickRepo>());
        var externalEvent = ExternalEvent.Create(handler);
        new WindowInteropHelper(view).Owner = commandData.Application.MainWindowHandle;
        viewModel.PickDrainPitLocationAction = () => { view.Hide(); handler.Request.Make(ValveRoomRequestId.PickDrainPitLocation); externalEvent.Raise(); };
        handler.LocationPicked = point => view.Dispatcher.Invoke(() => { view.Show(); view.Activate(); viewModel.ApplyPickedLocation(point); });
        viewModel.CreateModelAction = request => { handler.PendingRequest = request; handler.Request.Make(ValveRoomRequestId.CreateModel); externalEvent.Raise(); };
        view.Show();
        return Result.Succeeded;
    }
}
