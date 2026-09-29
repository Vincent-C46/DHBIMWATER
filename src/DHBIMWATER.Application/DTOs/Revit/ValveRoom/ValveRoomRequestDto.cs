using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Application.DTOs.Revit.ValveRoom;

/// <summary>독립 밸브실 구조물 생성에 필요한 치수는 mm, 공유좌표는 m, 도북각은 degree 단위이다.
/// 타입 전용 입력은 RoomType에 대응하는 MudSpec/SluiceSpec 중 하나만 채운다.</summary>
public record ValveRoomRequestDto
{
    public string RoomType { get; init; } = string.Empty;
    /// <summary>프로젝트 정보 "관로구간"에 기록할 사용자 입력(예: 도수1-1). 비어 있어도 기록한다.</summary>
    public string PipelineSection { get; init; } = string.Empty;
    /// <summary>프로젝트 정보 "밸브실측점"에 기록할 사용자 입력(예: 1+750.00). 비어 있어도 기록한다.</summary>
    public string StationPoint { get; init; } = string.Empty;
    public double ReferenceX { get; init; }
    /// <summary>내부공간 좌하단 내부원점의 동쪽 방향 공유좌표(m).</summary>
    public double SharedCoordinateX { get; init; }
    /// <summary>내부공간 좌하단 내부원점의 북쪽 방향 공유좌표(m).</summary>
    public double SharedCoordinateY { get; init; }
    /// <summary>기초 상부 EL(m). 레벨 표고가 되며 내부원점 표고는 항상 0이다.</summary>
    public double FoundationTopEl { get; init; }
    /// <summary>진북에서 도북(프로젝트 북)까지 시계방향 각도(degree).</summary>
    public double TrueNorthToProjectNorthClockwiseDegrees { get; init; }
    public double ReferenceY { get; init; }
    public double PlainConcreteThickness { get; init; }
    public double FoundationThickness { get; init; }
    public double FoundationToe { get; init; }
    public double OuterWallThickness { get; init; }
    public double UpperSlabThickness { get; init; }
    public double InnerWidth { get; init; }
    public double InnerLength { get; init; }
    public double InnerHeight { get; init; }
    /// <summary>드레인 피트 목록. 비어 있으면 피트를 만들지 않는다.</summary>
    public IReadOnlyList<DrainPitDefinition> DrainPits { get; init; } = Array.Empty<DrainPitDefinition>();
    /// <summary>드레인 피트 Void 패밀리/매개변수 매핑. DrainPits가 비어 있지 않으면 반드시 채워야 한다.</summary>
    public DrainPitVoidFamilyMappingDto? DrainPitVoidMapping { get; init; }
    /// <summary>상부슬래브 하단 헌치 배치 여부.</summary>
    public bool HasUpperHaunch { get; init; }
    /// <summary>기초 상단 헌치 배치 여부.</summary>
    public bool HasLowerHaunch { get; init; }
    /// <summary>헌치 구조 프레임 유형. UI가 "패밀리명 : 타입명" 형식으로 채운다(docs/156 § D-1).
    /// 비어 있으면 이름 기반 자동 탐색(FindHaunchBeamSymbol)을 쓴다.</summary>
    public string HaunchTypeName { get; init; } = string.Empty;
    /// <summary>RoomType이 "공기밸브실"일 때만 값이 채워진다.</summary>
    public AirValveRoomSpecDto? AirSpec { get; init; }
    /// <summary>RoomType이 "이토밸브실"일 때만 값이 채워진다.</summary>
    public MudValveRoomSpecDto? MudSpec { get; init; }
    /// <summary>RoomType이 "제수밸브실"일 때만 값이 채워진다.</summary>
    public SluiceValveRoomSpecDto? SluiceSpec { get; init; }
}
