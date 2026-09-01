using Autodesk.Revit.UI;
using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Revit.Commands;

internal sealed class ValveRoomRequestHandler : IExternalEventHandler
{
    private readonly CreateValveRoomUseCase _useCase;
    private readonly IDrainPitLocationPickRepo _locationPick;
    public ValveRoomRequest Request { get; } = new();
    public ValveRoomRequestDto? PendingRequest { get; set; }
    public Action<Point2D?>? LocationPicked { get; set; }
    public ValveRoomRequestHandler(CreateValveRoomUseCase useCase, IDrainPitLocationPickRepo locationPick) { _useCase = useCase; _locationPick = locationPick; }
    public void Execute(UIApplication uiapp)
    {
        var requestId = Request.Take();
        try
        {
            if (requestId == ValveRoomRequestId.PickDrainPitLocation) LocationPicked?.Invoke(_locationPick.PickLocation());
            else if (requestId == ValveRoomRequestId.CreateModel && PendingRequest is { } request) { PendingRequest = null; _useCase.Execute(request); }
        }
        catch (Exception ex)
        {
            if (requestId == ValveRoomRequestId.PickDrainPitLocation) LocationPicked?.Invoke(null);
            else TaskDialog.Show("밸브실 생성 오류", ex.Message);
        }
    }
    public string GetName() => "ValveRoomRequest";
}
