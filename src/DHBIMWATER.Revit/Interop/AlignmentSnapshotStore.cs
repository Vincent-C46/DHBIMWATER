using Autodesk.Revit.DB;
using DHBoost.Revit.Interop;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Revit.Interop;

internal sealed class AlignmentSnapshotStore : IAlignmentSnapshotStore
{
    private const int ExpectedContractVersion = 1;
    private readonly Func<Document?> _document;

    public AlignmentSnapshotStore(Func<Document?> document) => _document = document;

    public string EnsureSnapshot(PipeAlignment alignment, AlignmentStationProfile profile, AlignmentPlacementOrigin origin, out string warning)
    {
        warning = string.Empty;
        try
        {
            InteropInitializer.EnsureInitialized();
            if (AlignmentSnapshotInterop.ContractVersion != ExpectedContractVersion)
            {
                warning = $"dll이 오래되었습니다(계약 v{AlignmentSnapshotInterop.ContractVersion} ≠ v{ExpectedContractVersion}).";
                return string.Empty;
            }

            var document = _document(); 
            if (document is null)
            {
                warning = "활성 Revit 문서가 없어 선형 스냅샷을 기록하지 못했습니다.";
                return string.Empty;
            }

            var vertices = alignment.Vertices;
            var eastingsM = vertices.Select(x => x.X).ToArray();
            var northingsM = vertices.Select(x => x.Y).ToArray();
            var elevationsM = vertices.Select(x => x.Z).ToArray();
            // 스냅샷 측점은 엑셀 2D Station 기준이다 — 모델 요소의 DH_*측점과 같은 기준(docs/215 § C).
            var stationsM = Enumerable.Range(0, vertices.Count).Select(profile.StationAtVertex).ToArray();
            var name = $"{Path.GetFileNameWithoutExtension(alignment.SourceFile)}#{alignment.RecordNumber}";

            return AlignmentSnapshotInterop.EnsureSnapshot(
                document, name, alignment.SourceFile, alignment.RecordNumber,
                eastingsM, northingsM, elevationsM, stationsM,
                "OriginTranslate", origin.X, origin.Y, out warning);
        }
        catch (Exception ex)
        {
            warning = ex.Message;
            return string.Empty;
        }
    }
}