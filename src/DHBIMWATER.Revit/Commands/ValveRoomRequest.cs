using System.Threading;

namespace DHBIMWATER.Revit.Commands;

internal enum ValveRoomRequestId { None, CreateModel, PickDrainPitLocation }

internal sealed class ValveRoomRequest
{
    private int _requestId;
    public ValveRoomRequestId Take() => (ValveRoomRequestId)Interlocked.Exchange(ref _requestId, (int)ValveRoomRequestId.None);
    public void Make(ValveRoomRequestId requestId) => Interlocked.Exchange(ref _requestId, (int)requestId);
}
