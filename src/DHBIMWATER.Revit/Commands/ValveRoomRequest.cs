using System.Threading;

namespace DHBIMWATER.Revit.Commands;

// PickDrainPitLocation은 화면 미연결이라 제거했다(docs/238 § F).
internal enum ValveRoomRequestId { None, CreateModel }

internal sealed class ValveRoomRequest
{
    private int _requestId;
    public ValveRoomRequestId Take() => (ValveRoomRequestId)Interlocked.Exchange(ref _requestId, (int)ValveRoomRequestId.None);
    public void Make(ValveRoomRequestId requestId) => Interlocked.Exchange(ref _requestId, (int)requestId);
}
