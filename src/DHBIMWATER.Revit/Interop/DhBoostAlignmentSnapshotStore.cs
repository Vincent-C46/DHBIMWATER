using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using DHBoost.Revit.Interop;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Revit.Interop;

internal sealed class DhBoostAlignmentSnapshotStore : IAlignmentSnapshotStore
{
    private const int ExpectedContractVersion = 1;
    private readonly Func<Document?> _document;

    static DhBoostAlignmentSnapshotStore()
    {
        var assembly = Assembly.Load("DHBoost.Revit");
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
    }

    public DhBoostAlignmentSnapshotStore(Func<Document?> document) => _document = document;

    public string EnsureSnapshot(PipeAlignment alignment, AlignmentStationProfile profile, AlignmentPlacementOrigin origin, out string warning)
    {
        warning = string.Empty;
        try
        {
            if (AlignmentSnapshotInterop.ContractVersion != ExpectedContractVersion)
            {
                warning = $"libs의 DHBoost.Revit.dll이 오래되었습니다(계약 v{AlignmentSnapshotInterop.ContractVersion} ≠ v{ExpectedContractVersion}).";
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
            var stationsM = Enumerable.Range(0, vertices.Count).Select(profile.DistanceAtVertex).ToArray();
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
