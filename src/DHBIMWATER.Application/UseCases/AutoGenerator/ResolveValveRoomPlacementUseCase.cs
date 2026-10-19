using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

/// <summary>위치 자동지정 결과. 창은 Placement 값을 입력칸에 넣고 Point·Warnings는 안내에만 쓴다.</summary>
public sealed record ValveRoomPlacementResolution(AlignmentStationPoint Point, ValveRoomPlacementResult Placement, IReadOnlyList<string> Warnings);

/// <summary>엑셀 선형 + 측점 → 밸브실 공유좌표·도북각·기초상부EL·내부높이(docs/300 § B-4).
/// 엑셀만 읽고 Revit 문서를 건드리지 않아 Transaction·ExternalEvent가 없다.</summary>
public sealed class ResolveValveRoomPlacementUseCase
{
    private readonly IExcelAlignmentSourceReader _reader;

    public ResolveValveRoomPlacementUseCase(IExcelAlignmentSourceReader reader) => _reader = reader;

    /// <param name="input">Point만 비워 둔 입력. 측점 점은 이 UseCase가 채운다.</param>
    public ValveRoomPlacementResolution Execute(string filePath, ExcelAlignmentMapping mapping, string stationText,
        Func<AlignmentStationPoint, ValveRoomPlacementInput> input)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(input);
        var station = StationTextParser.Parse(stationText?.Trim(), mapping.StationMode, mapping.NumberChainIntervalM)
            ?? throw new DhbimwaterException(ErrorCodes.VR_PlacementStationInvalid, $"밸브실측점 '{stationText}'을 해석할 수 없습니다. 예) 1+750.00");

        var profile = _reader.ReadProfile(filePath, mapping);
        var point = AlignmentStationLocator.Locate(profile.Rows, station);
        var placement = ValveRoomPlacementCalculator.Calculate(input(point));
        return new ValveRoomPlacementResolution(point, placement, profile.Warnings);
    }
}
