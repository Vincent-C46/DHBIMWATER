using Autodesk.Revit.UI;
using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Application.UseCases.AutoGenerator;

namespace DHBIMWATER.Revit.Commands;

internal sealed class ValveRoomRequestHandler : IExternalEventHandler
{
    private readonly CreateValveRoomUseCase _useCase;
    public ValveRoomRequest Request { get; } = new();
    public ValveRoomRequestDto? PendingRequest { get; set; }
    /// <summary>창을 연 문서(docs/237 § B). 다른 문서가 활성이면 요청을 실행하지 않는다.</summary>
    public Autodesk.Revit.DB.Document? OwnerDocument { get; set; }
    // 드레인 피트 위치 피킹(IDrainPitLocationPickRepo) 분기는 화면 미연결이라 제거했다(docs/238 § F).
    public ValveRoomRequestHandler(CreateValveRoomUseCase useCase) { _useCase = useCase; }
    public void Execute(UIApplication uiapp)
    {
        var requestId = Request.Take();
        try
        {
            if (requestId != ValveRoomRequestId.None) DocumentGuard.EnsureOwner(OwnerDocument, uiapp);
            if (requestId == ValveRoomRequestId.CreateModel && PendingRequest is { } request) { PendingRequest = null; _useCase.Execute(request); }
        }
        catch (Exception ex)
        {
            ErrorPresenter.Show("밸브실 생성 오류", "ValveRoom", ex);
        }
    }
    public string GetName() => "ValveRoomRequest";
}
