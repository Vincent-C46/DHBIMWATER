using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>타이밸브실 최소 폭 실측(docs/323). 패밀리 매핑은 PipeLayout 저장 설정을 그대로 쓴다 — 배치와 같은 패밀리로 재야 결과가 맞는다.
/// 트랜잭션은 열지 않는다(UseCase가 열고 되돌림).</summary>
internal sealed class RevitTieValveRoomProbeRepo : ITieValveRoomProbeRepo
{
    private readonly Func<Document?> _document;
    private readonly IPipeLayoutFamilySettingsRepo _settings;

    public RevitTieValveRoomProbeRepo(Func<Document?> document, IPipeLayoutFamilySettingsRepo settings)
    { _document = document; _settings = settings; }

    public TieValveRoomProbe Measure(double mainDiameterMm, PipeMaterial material, SteelFittingClass steelClass)
    {
        var document = _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var settings = _settings.Load()
            ?? throw new DhbimwaterException(ErrorCodes.PL_SegmentFamilyMissing,
                "배관 배치(PipeLayout) 패밀리 설정이 문서에 없습니다. 배관 배치 창에서 패밀리를 한 번 저장한 뒤 다시 시도하세요.");
        return RevitPipeSegmentCommandRepo.MeasureTieValveRoom(document, settings.ToSelection(), mainDiameterMm, material, steelClass);
    }
}
