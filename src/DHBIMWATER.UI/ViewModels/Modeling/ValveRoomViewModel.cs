using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.Services;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.UI.Utilities;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>
/// 독립 밸브실 모델링 입력 화면의 상태를 관리한다.
/// Revit 모델 생성은 후속 UseCase 연결 시점에 구현한다.
/// </summary>
public class ValveRoomViewModel : ViewModelBase
{
    private const string NotSelected = "(선택 안 함)";
    /// <summary>기둥·보를 쓰지 않을 때의 표시값. DTO로 나갈 때 빈 문자열로 바뀐다.</summary>
    private const string NotUsed = "(사용 안 함)";
    /// <summary>헌치 유형을 고르지 않았을 때의 표시값. 이 값이면 FindHaunchBeamSymbol의 이름 기반 자동 탐색을 쓴다.</summary>
    private const string HaunchTypeAuto = "(자동 탐색)";
    // 헌치 기본 유형 추정 키워드. 1순위는 패밀리명 완전일치, 2순위는 패밀리명 부분일치다(docs/156 § D-2).
    private const string HaunchPrimaryFamilyName = "DH_헌치";
    private const string HaunchFamilyKeyword = "헌치";
    // 드레인 피트 Void 패밀리·매개변수 자동 추정 후보. 앞에서부터 먼저 걸리는 것을 기본값으로 쓴다.
    /// <summary>용도 유형 토글이 가진 값. 프로젝트 정보에서 복원할 때 유효성 검사에 쓴다(docs/180).</summary>
    private static readonly string[] ValveRoomUsageTypes = ["이토밸브실", "공기밸브실", "타이밸브실", "제수밸브실"];
    // "피트"가 1순위다 — 피트 전용으로 만든 패밀리가 있으면 그것이 정답이고, 없을 때만 범용 보이드로 내려간다.
    private static readonly string[] DrainPitVoidFamilyDefaultKeywords = ["피트", "보이드_사각형", "보이드_사각"];
    // 1순위 키워드가 빗나갔을 때의 2순위. "보이드" 단독 키워드는 보이드_원형 등을 집을 수 있어
    // 사각형만 지원하는 형상 계산과 어긋나므로 아래 제외 키워드로 원형류를 걸러낸다.
    // "피트"도 형상을 가리지 않는 이름이라 1순위에도 같은 제외 키워드를 적용한다.
    private static readonly string[] DrainPitVoidFamilyFallbackKeywords = ["보이드", "Void"];
    private static readonly string[] DrainPitVoidFamilyExcludeKeywords = ["원형", "원통", "Circle", "Round"];
    // 피트 패밀리는 평면 치수를 "가로/세로"로 쓰는 경우가 많아 이를 1순위로 둔다.
    // "가로폭"/"세로폭"은 "가로"/"세로" 부분일치보다 먼저 완전일치로 잡히도록 맨 앞에 둔다.
    private static readonly string[] DrainPitLengthParameterDefaultKeywords = ["가로폭", "가로", "길이", "Length", "L"];
    private static readonly string[] DrainPitWidthParameterDefaultKeywords = ["세로폭", "세로", "폭", "너비", "Width", "W"];
    private static readonly string[] DrainPitDepthParameterDefaultKeywords = ["깊이", "높이", "Depth", "Height", "H"];

    private readonly IDialogService _dialogService;
    private readonly IElementTypeQueryRepo _typeQueryRepo;
    private readonly IProjectInformationQueryRepo _projectInfoQueryRepo;
    // 위치 자동지정(docs/300). 셋 중 하나라도 없으면(테스트·구형 호출) 버튼이 경고만 띄운다.
    private readonly IExcelAlignmentSourceReader? _excelReader;
    private readonly IFileDialogService? _fileDialog;
    private readonly ResolveValveRoomPlacementUseCase? _resolvePlacement;

    private string _selectedValveRoomStructure = "현타밸브실";
    private string _selectedValveRoomType = "이토밸브실";
    private string _pipelineSection = string.Empty;
    private string _stationPoint = string.Empty;
    private string _selectedColumnTypeName = NotUsed;
    private string _selectedBeamTypeName = NotUsed;
    private string _selectedDrainPitVoidFamily = NotSelected;
    private string _selectedDrainPitLengthParam = NotSelected;
    private string _selectedDrainPitWidthParam = NotSelected;
    private string _selectedDrainPitDepthParam = NotSelected;
    private bool _hasDrainPit = true;
    private bool _hasUpperHaunch = true;
    private bool _hasLowerHaunch = true;
    private string _selectedHaunchTypeName = HaunchTypeAuto;
    // 사용자가 콤보에서 직접 고른 매개변수는 pin 해두고, 패밀리를 바꿔도 그 이름이 있으면 유지한다.
    private bool _drainPitLengthParamPinned;
    private bool _drainPitWidthParamPinned;
    private bool _drainPitDepthParamPinned;
    // 기둥·보·헌치도 같은 사상이다. 단 드레인 피트 매개변수와 달리 "(사용 안 함)"·"(자동 탐색)"을 고른 것도
    // 유효한 최종 선택이라(제수밸브실이 아니면 보·기둥을 쓰지 않는다) sentinel을 골라도 pin한다(docs/156 § F-2).
    private bool _columnTypePinned;
    private bool _beamTypePinned;
    private bool _haunchTypePinned;
    // 자동 추정으로 값을 넣는 동안에는 pin을 세우지 않기 위한 가드.
    private bool _isRefreshingDrainPitParameters;
    private bool _isApplyingTypeDefaults;
    private double _plainConcreteThickness = 100;
    private double _foundationThickness = 500;
    private double _foundationToe = 300;
    private double _mainPipeDiameter = 100;
    private double _foundationTopToPipeCenterDepth = 250;
    private string _selectedVoidAxis = "X";
    private double _outerWallThickness = 400;
    private double _intermediateWallThickness = 300;
    private double _upperSlabThickness = 400;
    private double _intermediateSlabThickness = 300;
    private double _innerWidth = 2500;
    private double _innerLength = 4000;
    private double _innerHeight = 2500;
    private bool _hasIntermediateWall = true;
    private double _intermediateWallOffset = 2000;
    private bool _hasIntermediateSlab;
    private double _floor1InnerHeight = 2500;
    private double _floor2InnerHeight = 2500;
    private int _beamCountX = 1;
    private double _beamOffsetX = 1000;
    private double _beamSpacingX = 1000;
    private int _beamCountY = 1;
    private double _beamOffsetY = 1000;
    private double _beamSpacingY = 1000;
    // 사용자가 직접 고친 칸. true면 자동 계산(보 균등·중간벽 중앙·H1=H2)이 안 덮음. 유형 변경·[기본값]에서 해제.
    private bool _beamOffsetXPinned, _beamSpacingXPinned, _beamOffsetYPinned, _beamSpacingYPinned;
    private bool _intermediateWallOffsetPinned, _floor1InnerHeightPinned, _floor2InnerHeightPinned;

    private double _sharedCoordinateX;
    private double _sharedCoordinateY;
    private double _foundationTopEl;
    private double _trueNorthToProjectNorthClockwiseDegrees;

    // 위치 자동지정 입력(docs/300 § D-2, docs/304). 관저고·원지반 m, 관경 mm. 프리셋이 리셋하지 않는다.
    private double _pipeInvertEl;
    private double _groundEl;
    private double _placementPipeDiameter = 300;
    private string _selectedSluiceVariant = ValveRoomStandardDimensionCatalog.TypeB;
    // 마지막으로 고른 밸브실 엑셀 행 번호. [엑셀 다시 지정] 때 매핑창 목록에서 미리 선택한다.
    private int? _placementRowNumber;
    // 고른 행의 측점 원문(docs/310). 측점 칸을 손으로 바꾸면 행 표고와 어긋나므로 자동지정 전에 대조한다.
    private string? _placementStationText;
    // 자동지정 중심(공유좌표 m)·도북각. L·W 바뀌면 이 중심 기준으로 좌하단 재계산. 좌표·도북각 손 수정하면 null.
    private (double X, double Y, double TrueNorth)? _placementCenter;
    // 관저고 → 기초 상면 거리 H1(mm, docs/310). 사용자가 조정하는 유일한 높이 입력. 프리셋이 리셋하지 않는다.
    private double _invertToFoundationTopMm = ValveRoomPlacementRules.DefaultInvertToFoundationTopMm;
    // 엑셀 매핑은 창이 열려 있는 동안만 기억한다. 저장 안 함.
    private string? _placementExcelPath;
    private ExcelAlignmentMapping? _placementExcelMapping;
    // 지금 쓰는 제원표 = 문서 저장본, 없으면 내장 표준표(docs/312). 제원표 창에서 저장하면 바뀐다.
    private ValveRoomDimensionTable _dimensionTable;
    public ValveRoomViewModel(IDialogService dialogService, IElementTypeQueryRepo typeQueryRepo,
        IProjectInformationQueryRepo projectInfoQueryRepo,
        IConcreteSettingsRepo? concreteSettingsRepo = null, DHBIMWATER.Application.UseCases.AutoGenerator.SaveConcreteSettingsUseCase? saveConcreteSettings = null,
        IExcelAlignmentSourceReader? excelReader = null, IFileDialogService? fileDialog = null, ResolveValveRoomPlacementUseCase? resolvePlacement = null,
        IValveRoomDimensionCatalogRepo? dimensionCatalogRepo = null, SaveValveRoomDimensionCatalogUseCase? saveDimensionCatalog = null)
    {
        _dialogService = dialogService;
        // 제원표 창도 콘크리트 창과 같은 ExternalEvent 경로(docs/312). 저장되면 프리셋·자동지정이 새 표를 쓴다.
        DimensionCatalogLauncher = new ValveRoomDimensionCatalogLauncher(dimensionCatalogRepo, saveDimensionCatalog, dialogService)
        {
            InitialRoomKind = () => SelectedValveRoomType,
            Saved = table => _dimensionTable = table,
        };
        // 생성자는 ValveRoomCommand(API 컨텍스트) 안에서 돈다 — 문서 저장본을 바로 읽는다. 실패하면 내장 표.
        try { _dimensionTable = DimensionCatalogLauncher.LoadOrDefault(); }
        catch { _dimensionTable = ValveRoomStandardDimensionCatalog.Default; }
        _excelReader = excelReader;
        _fileDialog = fileDialog;
        _resolvePlacement = resolvePlacement;
        // 모델리스 창이라 읽기·저장은 ValveRoomCommand가 RunInRevitContext를 연결해 ExternalEvent로 돈다(docs/249 § E).
        ConcreteLauncher = new ConcreteSettingsLauncher(concreteSettingsRepo, saveConcreteSettings, dialogService);
        _typeQueryRepo = typeQueryRepo;
        _projectInfoQueryRepo = projectInfoQueryRepo;

        // 구조 형식은 콤보, 용도 유형(이토·공기·타이·제수)은 View의 라디오 버튼 4개가 고정으로 들고 있다.
        ValveRoomStructures = new ObservableCollection<string>(new[] { "현타밸브실", "철제밸브실(미구현)" });
        ColumnTypeNames = new ObservableCollection<string>(new[] { NotUsed });
        BeamTypeNames = new ObservableCollection<string>(new[] { NotUsed });
        VoidAxes = new ObservableCollection<string>(new[] { "X", "Y" });
        DrainPitCorners = new ObservableCollection<string>(new[] { "좌상단", "좌하단", "우상단", "우하단" });
        DrainPitVoidFamilyNames = new ObservableCollection<string>(new[] { NotSelected });
        DrainPitVoidParameterNames = new ObservableCollection<string>(new[] { NotSelected });
        HaunchTypeNames = new ObservableCollection<string>(new[] { HaunchTypeAuto });
        LoadTypeNames();

        ResetCommand = new RelayCommand(_ => ApplyPreset());
        AutoPlaceCommand = new RelayCommand(_ => AutoPlace());
        RemapPlacementExcelCommand = new RelayCommand(_ => { if (SelectPlacementExcel(reuseMapping: true)) AutoPlace(); }, _ => _placementExcelPath is not null);
        // 철제밸브실은 형상 계산이 없으므로 생성을 막는다. RelayCommand는 CommandManager.RequerySuggested
        // 기반이라 IsStructureImplemented가 바뀌면 별도 갱신 호출 없이 버튼 상태가 따라온다.
        // 숫자 칸에 변환 오류가 남아 있으면 옛 값으로 생성되지 않게 막는다(docs/238 § B).
        CreateCommand = new RelayCommand(_ => RequestCreate(), _ => IsStructureImplemented && _inputValidationErrorCount == 0);
        // 프로젝트 정보에 남아 있는 값을 먼저 복원한다. 유형이 바뀌면 아래 ApplyPreset이 그 유형의 치수 프리셋을 깐다.
        LoadProjectInformation();
        ApplyPreset();
    }

    public ObservableCollection<string> ValveRoomStructures { get; }
    public ObservableCollection<string> ColumnTypeNames { get; }
    public ObservableCollection<string> BeamTypeNames { get; }
    public ObservableCollection<string> VoidAxes { get; }
    public ObservableCollection<string> DrainPitCorners { get; }
    public ObservableCollection<string> DrainPitVoidFamilyNames { get; }
    public ObservableCollection<string> DrainPitVoidParameterNames { get; }
    /// <summary>헌치 구조 프레임 유형 후보. 첫 항목은 자동 탐색이고, 나머지는 "패밀리명 : 타입명" 표기다(docs/156 § D-1).</summary>
    public ObservableCollection<string> HaunchTypeNames { get; }
    public ICommand ResetCommand { get; }
    /// <summary>[위치 자동지정] 엑셀 선형 + 밸브실측점으로 좌표·도북각·기초상부EL·내부높이를 채운다(docs/300).</summary>
    public ICommand AutoPlaceCommand { get; }
    /// <summary>[엑셀 다시 지정] 기억한 파일의 매핑창을 다시 연다.</summary>
    public ICommand RemapPlacementExcelCommand { get; }
    /// <summary>엑셀 매핑창 표시 훅. View가 Owner를 붙여 모달로 띄우고, 확정했으면 true. VM은 View 타입을 모른다.</summary>
    public Func<ExcelAlignmentMappingViewModel, bool>? ShowExcelMappingDialog { get; set; }
    public ICommand CreateCommand { get; }
    /// <summary>밸브실당 드레인 피트는 1개다. 생성 여부는 <see cref="HasDrainPit"/>가 결정한다.</summary>
    public DrainPitItemViewModel DrainPit { get; } = new();
    public Action<ValveRoomRequestDto>? CreateModelAction { get; set; }
    /// <summary>Revit API 컨텍스트에서 동작을 실행하는 훅(docs/238 § E). 창의 ExternalEvent로 연결된다.
    /// 드레인 피트 Void 매개변수 조회(EditFamily)를 WPF 디스패처에서 부르지 않기 위함이다. null이면(테스트) 바로 실행한다.</summary>
    public Action<Action>? RunInRevitContext { get; set; }
    /// <summary>[콘크리트 강도] 버튼(docs/249 § E). 밸브실 생성 UseCase가 문서 설정을 직접 읽는다.</summary>
    public ConcreteSettingsLauncher ConcreteLauncher { get; }
    public ValveRoomDimensionCatalogLauncher DimensionCatalogLauncher { get; }

    // 숫자 칸 변환 오류 수(docs/238 § B). View의 Validation.Error가 올리고 내린다 — PipeLayoutViewModel과 같은 방식.
    private int _inputValidationErrorCount;

    public void UpdateInputValidation(bool errorAdded)
    {
        _inputValidationErrorCount = Math.Max(0, _inputValidationErrorCount + (errorAdded ? 1 : -1));
        CommandManager.InvalidateRequerySuggested();
    }
    public Action? CloseAction { get; set; }
    public ValveRoomRequestDto? RequestedCreate { get; private set; }

    /// <summary>구조 형식. 현재 "현타밸브실"만 형상 계산이 구현돼 있다.</summary>
    public string SelectedValveRoomStructure
    {
        get => _selectedValveRoomStructure;
        set
        {
            if (!SetProperty(ref _selectedValveRoomStructure, value)) return;
            OnPropertyChanged(nameof(IsStructureImplemented));
        }
    }

    /// <summary>철제밸브실은 미구현이라 입력 탭과 생성 버튼을 막는다.</summary>
    public bool IsStructureImplemented => SelectedValveRoomStructure == "현타밸브실";

    /// <summary>용도 유형. 이토·공기·타이·제수 4종이며 DH_Zone에 이 값이 그대로 기록된다(docs/155 § C).</summary>
    public string SelectedValveRoomType
    {
        get => _selectedValveRoomType;
        set
        {
            if (!SetProperty(ref _selectedValveRoomType, value)) return;
            ApplyPreset();
            OnPropertyChanged(nameof(MudVisibility));
            OnPropertyChanged(nameof(SluiceVisibility));
            OnPropertyChanged(nameof(FrameVisibility));
            OnPropertyChanged(nameof(IntermediateWallVisibility));
            OnPropertyChanged(nameof(IntermediateSlabVisibility));
            OnPropertyChanged(nameof(SingleHeightVisibility));
            OnPropertyChanged(nameof(AirValveVisibility));
            OnPropertyChanged(nameof(InvertToFoundationVisibility));
            OnPropertyChanged(nameof(PlainConcreteVisibility));
            OnPropertyChanged(nameof(DrainPitVisibility));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(PlanImagePath));
            OnPropertyChanged(nameof(SectionImagePath));
        }
    }

    /// <summary>프로젝트 정보 "관로구간"에 기록할 값. 예: 도수1-1</summary>
    public string PipelineSection
    {
        get => _pipelineSection;
        set => SetProperty(ref _pipelineSection, value ?? string.Empty);
    }

    /// <summary>프로젝트 정보 "밸브실측점"에 기록할 값. 예: 1+750.00</summary>
    public string StationPoint
    {
        get => _stationPoint;
        set => SetProperty(ref _stationPoint, value ?? string.Empty);
    }

    // ItemsSource를 Clear하면 ComboBox가 SelectedItem을 null로 만들고 TwoWay 기본값이라 여기로 null이 역기록된다.
    // null이 남으면 RequestCreate의 "== NotSelected" 검사를 통과해 DTO에 null이 실리므로 세터에서 기본값으로 되돌린다(docs/154 § B-3).
    public string SelectedColumnTypeName
    {
        get => _selectedColumnTypeName;
        set
        {
            if (!SetProperty(ref _selectedColumnTypeName, value ?? NotUsed)) return;
            if (!_isApplyingTypeDefaults) _columnTypePinned = true;
        }
    }
    public string SelectedBeamTypeName
    {
        get => _selectedBeamTypeName;
        set
        {
            if (!SetProperty(ref _selectedBeamTypeName, value ?? NotUsed)) return;
            if (!_isApplyingTypeDefaults) _beamTypePinned = true;
        }
    }
    public string SelectedDrainPitVoidFamily
    {
        get => _selectedDrainPitVoidFamily;
        set
        {
            if (!SetProperty(ref _selectedDrainPitVoidFamily, value ?? NotSelected)) return;
            // 초기 적재·재적재(가드가 켜진 구간)는 이미 API 컨텍스트다. 사용자가 콤보를 바꾼 경우만 ExternalEvent로 보낸다(docs/238 § E).
            if (_isApplyingTypeDefaults || RunInRevitContext is null) RefreshDrainPitVoidParameterNames();
            else RunInRevitContext(RefreshDrainPitVoidParameterNames);
        }
    }
    public string SelectedDrainPitLengthParam
    {
        get => _selectedDrainPitLengthParam;
        set
        {
            var name = value ?? NotSelected;
            if (!SetProperty(ref _selectedDrainPitLengthParam, name)) return;
            if (!_isRefreshingDrainPitParameters && name != NotSelected) _drainPitLengthParamPinned = true;
        }
    }
    public string SelectedDrainPitWidthParam
    {
        get => _selectedDrainPitWidthParam;
        set
        {
            var name = value ?? NotSelected;
            if (!SetProperty(ref _selectedDrainPitWidthParam, name)) return;
            if (!_isRefreshingDrainPitParameters && name != NotSelected) _drainPitWidthParamPinned = true;
        }
    }
    public string SelectedDrainPitDepthParam
    {
        get => _selectedDrainPitDepthParam;
        set
        {
            var name = value ?? NotSelected;
            if (!SetProperty(ref _selectedDrainPitDepthParam, name)) return;
            if (!_isRefreshingDrainPitParameters && name != NotSelected) _drainPitDepthParamPinned = true;
        }
    }
    /// <summary>드레인 피트를 만들지 여부. 해제하면 DTO의 DrainPits가 빈 목록이 되어 Void를 배치하지 않는다.</summary>
    public bool HasDrainPit { get => _hasDrainPit; set => SetProperty(ref _hasDrainPit, value); }

    /// <summary>상부슬래브 하단 헌치 배치 여부.</summary>
    public bool HasUpperHaunch { get => _hasUpperHaunch; set => SetProperty(ref _hasUpperHaunch, value); }
    /// <summary>기초 상단 헌치 배치 여부.</summary>
    public bool HasLowerHaunch { get => _hasLowerHaunch; set => SetProperty(ref _hasLowerHaunch, value); }
    /// <summary>헌치 구조 프레임 유형("패밀리명 : 타입명"). <see cref="HaunchTypeAuto"/>면 이름 기반 자동 탐색을 쓴다.</summary>
    public string SelectedHaunchTypeName
    {
        get => _selectedHaunchTypeName;
        set
        {
            if (!SetProperty(ref _selectedHaunchTypeName, value ?? HaunchTypeAuto)) return;
            if (!_isApplyingTypeDefaults) _haunchTypePinned = true;
        }
    }

    /// <summary>실제로 피트를 만드는지. 공기밸브실은 피트 입력 자체가 숨겨지므로(<see cref="DrainPitVisibility"/>) 체크 상태와 무관하게 만들지 않는다.</summary>
    private bool CreatesDrainPit => HasDrainPit && !IsAir;
    public double PlainConcreteThickness { get => _plainConcreteThickness; set => SetAndRefresh(ref _plainConcreteThickness, value); }
    public double FoundationThickness { get => _foundationThickness; set => SetAndRefresh(ref _foundationThickness, value); }
    public double FoundationToe { get => _foundationToe; set => SetAndRefresh(ref _foundationToe, value); }
    /// <summary>공기밸브실 본관 직경. 바꾸면 기초두께·Hp를 공식으로 다시 맞춘다(docs/311). 두 칸은 이후 손 수정 가능.</summary>
    public double MainPipeDiameter
    {
        get => _mainPipeDiameter;
        set
        {
            if (!SetProperty(ref _mainPipeDiameter, value)) return;
            if (IsAir && value > 0)
            {
                FoundationThickness = ValveRoomPlacementRules.AirValveFoundationThicknessMm(value);
                FoundationTopToPipeCenterDepth = ValveRoomPlacementRules.AirValvePipeCenterDepthMm(value);
            }
            OnPropertyChanged(nameof(Summary));
        }
    }
    public double FoundationTopToPipeCenterDepth { get => _foundationTopToPipeCenterDepth; set => SetAndRefresh(ref _foundationTopToPipeCenterDepth, value); }
    public string SelectedVoidAxis { get => _selectedVoidAxis; set => SetAndRefresh(ref _selectedVoidAxis, value); }
    public double OuterWallThickness { get => _outerWallThickness; set => SetAndRefresh(ref _outerWallThickness, value); }
    public double IntermediateWallThickness { get => _intermediateWallThickness; set { if (SetProperty(ref _intermediateWallThickness, value)) ApplyAutoLayout(); } }
    public double UpperSlabThickness { get => _upperSlabThickness; set => SetProperty(ref _upperSlabThickness, value); }
    public double IntermediateSlabThickness { get => _intermediateSlabThickness; set { if (SetProperty(ref _intermediateSlabThickness, value)) ApplyAutoLayout(); } }
    public double InnerWidth { get => _innerWidth; set { SetAndRefresh(ref _innerWidth, value); ApplyAutoLayout(); ReanchorPlacementCorner(); } }
    public double InnerLength { get => _innerLength; set { SetAndRefresh(ref _innerLength, value); ApplyAutoLayout(); ReanchorPlacementCorner(); } }
    public double InnerHeight { get => _innerHeight; set { SetAndRefresh(ref _innerHeight, value); ApplyAutoLayout(); } }
    // 아래 칸은 손으로 고치면 pin — 자동 계산이 더는 안 덮는다. 짝 칸(Ox↔Sx, H1↔H2)은 남은 값으로 다시 맞춘다.
    public double IntermediateWallOffset { get => _intermediateWallOffset; set => SetPinned(ref _intermediateWallOffset, value, ref _intermediateWallOffsetPinned); }
    public double Floor1InnerHeight { get => _floor1InnerHeight; set => SetPinned(ref _floor1InnerHeight, value, ref _floor1InnerHeightPinned); }
    public double Floor2InnerHeight { get => _floor2InnerHeight; set => SetPinned(ref _floor2InnerHeight, value, ref _floor2InnerHeightPinned); }
    public int BeamCountX { get => _beamCountX; set { SetAndRefresh(ref _beamCountX, Math.Max(0, value)); ApplyAutoLayout(); } }
    public double BeamOffsetX { get => _beamOffsetX; set => SetPinned(ref _beamOffsetX, value, ref _beamOffsetXPinned); }
    public double BeamSpacingX { get => _beamSpacingX; set => SetPinned(ref _beamSpacingX, value, ref _beamSpacingXPinned); }
    public int BeamCountY { get => _beamCountY; set { SetAndRefresh(ref _beamCountY, Math.Max(0, value)); ApplyAutoLayout(); } }
    public double BeamOffsetY { get => _beamOffsetY; set => SetPinned(ref _beamOffsetY, value, ref _beamOffsetYPinned); }
    public double BeamSpacingY { get => _beamSpacingY; set => SetPinned(ref _beamSpacingY, value, ref _beamSpacingYPinned); }

    // 손으로 고친 좌표·도북각은 자동지정 중심과 무관 — 중심 기억 해제.
    public double SharedCoordinateX { get => _sharedCoordinateX; set { if (SetProperty(ref _sharedCoordinateX, value)) _placementCenter = null; } }
    public double SharedCoordinateY { get => _sharedCoordinateY; set { if (SetProperty(ref _sharedCoordinateY, value)) _placementCenter = null; } }
    /// <summary>기초 상부 EL(m). 레벨 표고가 되며 내부원점 표고는 항상 0이다.</summary>
    public double FoundationTopEl { get => _foundationTopEl; set => SetProperty(ref _foundationTopEl, value); }
    public double TrueNorthToProjectNorthClockwiseDegrees { get => _trueNorthToProjectNorthClockwiseDegrees; set { if (SetProperty(ref _trueNorthToProjectNorthClockwiseDegrees, value)) _placementCenter = null; } }
    /// <summary>관저고(m) = 엑셀 표고(관중심) − 호칭지름/2. 엑셀 행에서만 채운다 — 창에 입력칸 없음(docs/310).</summary>
    public double PipeInvertEl { get => _pipeInvertEl; set => SetProperty(ref _pipeInvertEl, value); }
    /// <summary>원지반 표고(m). 엑셀 행에서만 채운다. 상부슬래브 상면 EL = 원지반 − <see cref="ValveRoomPlacementRules.GroundToTopSlabTopMm"/>.</summary>
    public double GroundEl { get => _groundEl; set => SetProperty(ref _groundEl, value); }
    /// <summary>본관 호칭지름(mm). 엑셀 행에서만 채운다. 제원표 조회·관 중심(관저고 + 관경/2)에 쓴다. 공기밸브실은 성공 시 본관 관경 칸에도 넣는다.</summary>
    public double PlacementPipeDiameter { get => _placementPipeDiameter; set => SetProperty(ref _placementPipeDiameter, value); }
    /// <summary>관저고 → 기초 상면 거리 H1(mm). 기본 600. 공기밸브실은 안 쓴다(Hp 사용).</summary>
    public double InvertToFoundationTopMm
    {
        get => _invertToFoundationTopMm;
        set { if (SetProperty(ref _invertToFoundationTopMm, value)) OnPropertyChanged(nameof(PlacementRuleText)); }
    }
    /// <summary>제수밸브실 제원표 형식. A형은 DN350까지 — 그 밖은 B형으로 넘어간다.</summary>
    public IReadOnlyList<string> SluiceVariants { get; } = ValveRoomStandardDimensionCatalog.Variants(ValveRoomStandardDimensionCatalog.Sluice);
    public string SelectedSluiceVariant { get => _selectedSluiceVariant; set => SetProperty(ref _selectedSluiceVariant, value ?? ValveRoomStandardDimensionCatalog.TypeB); }
    /// <summary>규칙 안내. 관저고 = 엑셀 표고(관중심) − 호칭지름/2.</summary>
    public string PlacementRuleText =>
        $"관저고 = 엑셀 표고(관중심) − 호칭지름/2 · 기초 상면 = 관저고 − {InvertToFoundationTopMm:0.###} mm · 상부슬래브 상면 = 원지반 − {ValveRoomPlacementRules.GroundToTopSlabTopMm:0} mm";
    /// <summary>위치 자동지정에 쓸 엑셀 표시(파일명 · 시트). 지정 전이면 안내문.</summary>
    public string PlacementSourceText => _placementExcelPath is null || _placementExcelMapping is null
        ? "엑셀 미지정"
        : $"{System.IO.Path.GetFileName(_placementExcelPath)} · {_placementExcelMapping.SheetName}";
    public bool HasIntermediateWall
    {
        get => _hasIntermediateWall;
        set
        {
            if (!SetProperty(ref _hasIntermediateWall, value)) return;
            OnPropertyChanged(nameof(IntermediateWallVisibility));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public bool HasIntermediateSlab
    {
        get => _hasIntermediateSlab;
        set
        {
            if (!SetProperty(ref _hasIntermediateSlab, value)) return;
            ApplyAutoLayout();
            OnPropertyChanged(nameof(IntermediateSlabVisibility));
            OnPropertyChanged(nameof(SingleHeightVisibility));
            OnPropertyChanged(nameof(Summary));
        }
    }

    // 타이밸브실은 이토 형상을 쓰지만 내부 구성(중간벽·중간슬래브) 입력은 노출하지 않는다(docs/144 § 1).
    public Visibility MudVisibility => IsMud && !IsTie ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SluiceVisibility => IsSluice ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FrameVisibility => IsSluice ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IntermediateWallVisibility => IsMud && HasIntermediateWall ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IntermediateSlabVisibility => IsMud && HasIntermediateSlab ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SingleHeightVisibility => IsMud && HasIntermediateSlab ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AirValveVisibility => IsAir ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlainConcreteVisibility => IsAir ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DrainPitVisibility => IsAir ? Visibility.Collapsed : Visibility.Visible;
    // 관저고 → 기초 상면 H1 칸. 공기밸브실은 Hp 칸이 대신한다(docs/310).
    public Visibility InvertToFoundationVisibility => IsAir ? Visibility.Collapsed : Visibility.Visible;

    // 참고도는 Resources/ValveRoomImages/{종류}_{평면도|단면도}.png(Resource). 같은 폴더의 .svg가 원본이며 치수 기호는 View 라벨과 같다.
    private const string ImageBasePath = "pack://application:,,,/DHBIMWATER.UI;component/Resources/ValveRoomImages/";
    public string PlanImagePath => $"{ImageBasePath}{SelectedValveRoomType}_평면도.png";
    public string SectionImagePath => $"{ImageBasePath}{SelectedValveRoomType}_단면도.png";

    public string Summary
    {
        get
        {
            var outerWidth = InnerWidth + OuterWallThickness * 2;
            var outerLength = InnerLength + OuterWallThickness * 2;
            var foundationWidth = outerWidth + FoundationToe * 2;
            var foundationLength = outerLength + FoundationToe * 2;
            var height = IsMud && HasIntermediateSlab ? Floor1InnerHeight + Floor2InnerHeight : InnerHeight;
            var members = IsMud
                ? $"기초 · 외벽 4개 · {(HasIntermediateWall ? "중간벽 · " : string.Empty)}{(HasIntermediateSlab ? "중간슬래브 · " : string.Empty)}상부슬래브"
                : IsSluice
                    ? $"사각형 기초 · 외벽 4개 · 상부슬래브 · 보 X{BeamCountX}/Y{BeamCountY}개 · 보 교차부 기둥"
                    : "독립기초 · 본관 관통 Void · 외벽 4개 · 상부슬래브";

            return $"{SelectedValveRoomType} · 외벽 외곽 {outerWidth:N0}×{outerLength:N0} mm · " +
                   $"기초 {foundationWidth:N0}×{foundationLength:N0} mm (Toe {FoundationToe:N0} mm, 내부 H {height:N0} mm)\n{members}";
        }
    }

    // 타이밸브실은 이토밸브실 형상을 그대로 쓴다(docs/142 § 1). 값 기록만 "타이밸브실"로 남는다.
    private bool IsMud => SelectedValveRoomType is "이토밸브실" or "타이밸브실";
    // 단, 중간벽·중간슬래브는 타이밸브실에 없다(docs/144 § 1). 형상 계산은 이토와 같은 경로를 타되
    // MudSpec의 두 플래그가 항상 false라 그 두 부재만 빠진다.
    private bool IsTie => SelectedValveRoomType == "타이밸브실";
    private bool IsSluice => SelectedValveRoomType == "제수밸브실";
    private bool IsAir => SelectedValveRoomType == "공기밸브실";

    /// <summary>프로젝트 정보에 이미 기록된 밸브실 값을 입력창으로 되돌린다(docs/180).
    /// <para>생성자에서 한 번만 돌며, 세터가 아니라 backing field에 직접 넣는다 —
    /// 유형 세터의 부수효과(ApplyPreset·Visibility 알림)는 생성자 끝의 ApplyPreset이 어차피 처리한다.</para>
    /// 문서가 없거나 매개변수가 아직 없으면 빈 값이 와서 기본값이 그대로 유지된다.</summary>
    private void LoadProjectInformation()
    {
        try
        {
            var roomType = _projectInfoQueryRepo.GetTextParameter(ValveRoomProjectInfoParameters.RoomType);
            // 저장된 유형이 지금 UI가 아는 4종일 때만 복원한다. 오타·구버전 값이면 기본값(이토밸브실)을 쓴다.
            if (ValveRoomUsageTypes.Contains(roomType)) _selectedValveRoomType = roomType;
            _pipelineSection = _projectInfoQueryRepo.GetTextParameter(ValveRoomProjectInfoParameters.PipelineSection);
            _stationPoint = _projectInfoQueryRepo.GetTextParameter(ValveRoomProjectInfoParameters.StationPoint);
        }
        catch
        {
            // 값 복원 실패로 창이 안 뜨는 일은 없어야 한다. 기본값으로 연다.
        }
    }

    private void ApplyPreset()
    {
        // L·W·H = 제원표 기준관경 행(docs/311). 표 없으면(타이) 아래 하드코딩 값.
        var diameter = ValveRoomPlacementRules.DefaultPipeDiameter(SelectedValveRoomType);
        var standard = _dimensionTable.Find(SelectedValveRoomType, diameter, IsSluice ? ValveRoomStandardDimensionCatalog.TypeB : null);
        // 엑셀 행 고른 뒤엔 그 행 관경 유지. ApplyValveRoomListRow가 행 번호 먼저 넣고 유형 바꿈.
        if (_placementRowNumber is null) _placementPipeDiameter = diameter;

        if (IsMud)
        {
            _innerWidth = 2500; _innerLength = 4000; _innerHeight = 2500;
            _foundationToe = 300; _foundationThickness = 500;
            // 타이밸브실은 중간벽·중간슬래브를 쓰지 않는다(docs/144 § 1). 유형이 바뀔 때마다 이 프리셋이
            // 다시 도는 덕에 "이토에서 켠 중간슬래브"가 타이로 넘어가지 않는다.
            // 중간벽 오프셋은 아래 ApplyAutoLayout이 L·Tw로 정중앙 계산.
            ApplyStandardDimension(standard);
            _hasIntermediateWall = !IsTie; _hasIntermediateSlab = false;
        }
        else if (IsSluice)
        {
            _innerWidth = 2000; _innerLength = 3000; _innerHeight = 2500;
            ApplyStandardDimension(standard);
            // 공기에서 커진 기초두께가 넘어오지 않게 리셋(docs/311).
            _foundationToe = 300; _foundationThickness = 500;
            // 이토에서 켠 중간슬래브가 남으면 RequestCreate가 InnerHeight 대신 1F/2F를 검사한다(docs/238 § D).
            _hasIntermediateWall = false; _hasIntermediateSlab = false;
        }
        else
        {
            _innerWidth = 1500; _innerLength = 1800; _innerHeight = 2000;
            ApplyStandardDimension(standard);
            _foundationToe = 100; _selectedVoidAxis = "X";
            // 본관 기준관경 + 기초두께·Hp 공식(docs/311).
            _mainPipeDiameter = ValveRoomPlacementRules.DefaultPipeDiameterMm;
            _foundationThickness = ValveRoomPlacementRules.AirValveFoundationThicknessMm(_mainPipeDiameter);
            _foundationTopToPipeCenterDepth = ValveRoomPlacementRules.AirValvePipeCenterDepthMm(_mainPipeDiameter);
            _hasIntermediateWall = false; _hasIntermediateSlab = false;
        }

        // 유형 변경·[기본값] = 손 수정 해제 후 자동 치수 다시.
        _beamOffsetXPinned = _beamSpacingXPinned = _beamOffsetYPinned = _beamSpacingYPinned = false;
        _intermediateWallOffsetPinned = _floor1InnerHeightPinned = _floor2InnerHeightPinned = false;
        ApplyAutoLayout();

        OnPropertyChanged(nameof(InnerWidth));
        OnPropertyChanged(nameof(InnerLength));
        OnPropertyChanged(nameof(InnerHeight));
        OnPropertyChanged(nameof(PlacementPipeDiameter));
        OnPropertyChanged(nameof(FoundationThickness));
        OnPropertyChanged(nameof(FoundationToe));
        OnPropertyChanged(nameof(MainPipeDiameter));
        OnPropertyChanged(nameof(FoundationTopToPipeCenterDepth));
        OnPropertyChanged(nameof(SelectedVoidAxis));
        OnPropertyChanged(nameof(HasIntermediateWall));
        OnPropertyChanged(nameof(IntermediateWallOffset));
        OnPropertyChanged(nameof(HasIntermediateSlab));
        OnPropertyChanged(nameof(IntermediateWallVisibility));
        OnPropertyChanged(nameof(IntermediateSlabVisibility));
        OnPropertyChanged(nameof(SingleHeightVisibility));
        OnPropertyChanged(nameof(Summary));
        ReanchorPlacementCorner();
    }

    /// <summary>보 Ox·Sx·Oy·Sy 균등분배, 중간벽 Lw 정중앙, 중간슬래브 H1=H2. pin된 칸은 안 덮는다.</summary>
    private void ApplyAutoLayout()
    {
        // X방향 보는 내부 폭(W)에, Y방향 보는 내부 길이(L)에 놓인다(ValveRoomGeometryCalculator.CalculateBeams).
        var (ox, sx) = ValveRoomLayoutDefaults.EqualBeamLayout(_beamCountX, _innerWidth, _beamOffsetX, _beamOffsetXPinned, _beamSpacingX, _beamSpacingXPinned);
        var (oy, sy) = ValveRoomLayoutDefaults.EqualBeamLayout(_beamCountY, _innerLength, _beamOffsetY, _beamOffsetYPinned, _beamSpacingY, _beamSpacingYPinned);
        SetAuto(ref _beamOffsetX, ox, nameof(BeamOffsetX));
        SetAuto(ref _beamSpacingX, sx, nameof(BeamSpacingX));
        SetAuto(ref _beamOffsetY, oy, nameof(BeamOffsetY));
        SetAuto(ref _beamSpacingY, sy, nameof(BeamSpacingY));
        if (!_intermediateWallOffsetPinned)
            SetAuto(ref _intermediateWallOffset, ValveRoomLayoutDefaults.CenteredIntermediateWallOffset(_innerLength, _intermediateWallThickness), nameof(IntermediateWallOffset));
        var (h1, h2) = ValveRoomLayoutDefaults.SplitFloorHeights(_innerHeight, _intermediateSlabThickness,
            _floor1InnerHeight, _floor1InnerHeightPinned, _floor2InnerHeight, _floor2InnerHeightPinned);
        SetAuto(ref _floor1InnerHeight, h1, nameof(Floor1InnerHeight));
        SetAuto(ref _floor2InnerHeight, h2, nameof(Floor2InnerHeight));
        OnPropertyChanged(nameof(Summary));
    }

    // 자동 값은 화면 표기(소수 3자리)에 맞춰 반올림. 세터 안 거침 — pin 안 걸림.
    private void SetAuto(ref double field, double value, string propertyName) => SetProperty(ref field, Math.Round(value, 3), propertyName);

    private void SetPinned(ref double field, double value, ref bool pinned, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName)) return;
        pinned = true;
        // 짝 칸이 남은 구간·높이를 다시 맞춘다.
        ApplyAutoLayout();
    }

    /// <summary>
    /// 자동지정 뒤 L·W가 바뀌면 같은 중심으로 좌하단을 다시 계산한다.
    /// 좌하단만 저장하던 탓에 L 7000 → 10000 수정 시 실이 L 방향으로 1.5m 밀렸다(2026-10-23).
    /// </summary>
    private void ReanchorPlacementCorner()
    {
        if (_placementCenter is not { } center) return;
        var (x, y) = ValveRoomPlacementCalculator.LowerLeftFromCenter(center.X, center.Y, center.TrueNorth, InnerLength, InnerWidth);
        // 필드 직접 — setter는 중심 기억을 지운다.
        SetProperty(ref _sharedCoordinateX, Math.Round(x, 3), nameof(SharedCoordinateX));
        SetProperty(ref _sharedCoordinateY, Math.Round(y, 3), nameof(SharedCoordinateY));
    }

    /// <summary>제원표 행이 있으면 내부 L·W·H에 넣는다. 없으면 호출 전 값 유지.</summary>
    private void ApplyStandardDimension(ValveRoomStandardDimension? standard)
    {
        if (standard is null) return;
        _innerLength = standard.LengthMm;
        _innerWidth = standard.WidthMm;
        _innerHeight = standard.HeightMm;
    }

    private void RequestCreate()
    {
        // 숫자 칸 변환 실패는 VM 값이 옛 값 그대로라 여기 검사를 통과한다 — 버튼 CanExecute와 별개로 한 번 더 막는다(docs/238 § B).
        if (_inputValidationErrorCount > 0)
        {
            _dialogService.Warn("입력 확인", $"[{ErrorCodes.VR_InvalidDimensions}] 빨간 칸의 숫자를 고치세요.");
            return;
        }

        // 중간슬래브가 없을 때 InnerHeight를 안 봐서 벽 높이 0이 Revit까지 갔다(docs/207 § A-2).
        if (InnerWidth <= 0 || InnerLength <= 0 || FoundationThickness <= 0 || OuterWallThickness <= 0 || UpperSlabThickness <= 0 ||
            (HasIntermediateSlab ? (Floor1InnerHeight <= 0 || Floor2InnerHeight <= 0) : InnerHeight <= 0))
        {
            _dialogService.Warn("입력 확인", $"[{ErrorCodes.VR_InvalidDimensions}] 밸브실 치수와 두께는 0보다 커야 합니다.");
            return;
        }

        // 버림콘크리트를 만드는 유형은 두께 0이면 Revit 바닥 유형 생성이 던진다(docs/238 § D). 공기밸브실은 버림이 없다.
        if (!IsAir && PlainConcreteThickness <= 0)
        {
            _dialogService.Warn("입력 확인", $"[{ErrorCodes.VR_InvalidDimensions}] 버림콘크리트 두께는 0보다 커야 합니다.");
            return;
        }

        if (IsSluice && (SelectedBeamTypeName == NotUsed || SelectedColumnTypeName == NotUsed))
        {
            _dialogService.Warn("입력 확인", $"[{ErrorCodes.VR_SluiceTypesRequired}] 제수밸브실은 보 유형과 기둥 유형을 선택해야 합니다.");
            return;
        }

        if (IsSluice && ValveRoomGeometryCalculator.BeamLayoutError(BeamCountX, BeamOffsetX, BeamSpacingX,
                BeamCountY, BeamOffsetY, BeamSpacingY, InnerWidth, InnerLength) is { } beamError)
        {
            _dialogService.Warn("입력 확인", $"[{ErrorCodes.VR_InvalidBeamLayout}] {beamError}");
            return;
        }

        // 헌치를 켰는데 자동 탐색 후보가 없으면 슬래브·벽까지 만든 뒤 VR-201로 전부 롤백됐다(docs/238 § A).
        // 규칙은 RevitElementTypeCommandRepo.FindHaunchBeamSymbol과 같다(패밀리명·유형명에 "헌치" 또는 "haunch").
        if ((HasUpperHaunch || HasLowerHaunch) && SelectedHaunchTypeName == HaunchTypeAuto
            && !HaunchTypeNames.Any(name => name != HaunchTypeAuto && (name.Contains("헌치", StringComparison.Ordinal) || name.Contains("haunch", StringComparison.Ordinal))))
        {
            _dialogService.Warn("입력 확인", $"[{ErrorCodes.VR_BeamTypeNotFound}] 헌치 유형을 찾을 수 없습니다. 이름에 '헌치' 또는 'haunch'가 든 구조 프레임 패밀리를 로드하거나 헌치 체크를 끄세요.");
            return;
        }

        // 반경까지 더해 기초 아래로 빠지는 Void를 막는다(docs/207 § A-2).
        if (IsAir && (MainPipeDiameter <= 0 || FoundationTopToPipeCenterDepth <= 0
            || FoundationTopToPipeCenterDepth + MainPipeDiameter / 2 >= FoundationThickness))
        {
            _dialogService.Warn("입력 확인", $"[{ErrorCodes.VR_InvalidAirValveSpec}] 공기밸브실 본관 직경은 0보다 크고, 본관 중심 깊이는 기초 두께 범위 안이어야 합니다.");
            return;
        }

        if (CreatesDrainPit && (SelectedDrainPitVoidFamily == NotSelected ||
            SelectedDrainPitLengthParam == NotSelected || SelectedDrainPitWidthParam == NotSelected ||
            SelectedDrainPitDepthParam == NotSelected))
        {
            _dialogService.Warn("입력 확인", $"[{ErrorCodes.VR_DrainPitMappingRequired}] 드레인 피트가 있으면 패밀리와 Length/Width/Depth 매개변수를 모두 선택해야 합니다.");
            return;
        }

        if (CreatesDrainPit && (DrainPit.LengthX <= 0 || DrainPit.WidthY <= 0 || DrainPit.Depth <= 0))
        {
            _dialogService.Warn("입력 확인", $"[{ErrorCodes.VR_InvalidDrainPit}] 드레인 피트의 길이, 폭, 굴착 깊이는 0보다 커야 합니다.");
            return;
        }

        RequestedCreate = new ValveRoomRequestDto
        {
            RoomType = SelectedValveRoomType,
            PipelineSection = PipelineSection.Trim(),
            StationPoint = StationPoint.Trim(),
            PlainConcreteThickness = PlainConcreteThickness,
            FoundationThickness = FoundationThickness,
            SharedCoordinateX = SharedCoordinateX,
            SharedCoordinateY = SharedCoordinateY,
            FoundationTopEl = FoundationTopEl,
            TrueNorthToProjectNorthClockwiseDegrees = TrueNorthToProjectNorthClockwiseDegrees,
            FoundationToe = FoundationToe,
            OuterWallThickness = OuterWallThickness,
            UpperSlabThickness = UpperSlabThickness,
            InnerWidth = InnerWidth,
            InnerLength = InnerLength,
            InnerHeight = InnerHeight,
            HasUpperHaunch = HasUpperHaunch,
            HasLowerHaunch = HasLowerHaunch,
            HaunchTypeName = SelectedHaunchTypeName == HaunchTypeAuto ? string.Empty : SelectedHaunchTypeName,
            // 밸브실당 피트는 1개지만, 아래 계층(DTO·UseCase·Repo)은 N개를 처리할 수 있게 그대로 둔다.
            DrainPits = CreatesDrainPit
                ? new[]
                {
                    new DrainPitDefinition
                    {
                        TopCenter = ResolveDrainPitCenter(DrainPit),
                        LengthX = DrainPit.LengthX, WidthY = DrainPit.WidthY, Depth = DrainPit.Depth, Name = DrainPit.Name
                    }
                }
                : Array.Empty<DrainPitDefinition>(),
            DrainPitVoidMapping = CreatesDrainPit
                ? new DrainPitVoidFamilyMappingDto
                {
                    FamilyTypeName = SelectedDrainPitVoidFamily,
                    LengthParameterName = SelectedDrainPitLengthParam,
                    WidthParameterName = SelectedDrainPitWidthParam,
                    DepthParameterName = SelectedDrainPitDepthParam
                }
                : null,
            AirSpec = IsAir ? new AirValveRoomSpecDto(MainPipeDiameter, FoundationTopToPipeCenterDepth, SelectedVoidAxis) : null,
            // 타입 전용 입력은 선택된 밸브실 타입에 해당하는 쪽만 채운다.
            // 타이밸브실도 IsMud라 MudSpec을 반드시 넘긴다 — GeometryType이 "이토밸브실"로 정규화되므로
            // null이면 ValveRoomGeometryCalculator.Validate가 예외를 던진다(docs/144 § 1-3).
            MudSpec = IsMud
                ? new MudValveRoomSpecDto(HasIntermediateWall, IntermediateWallOffset, IntermediateWallThickness,
                    HasIntermediateSlab, IntermediateSlabThickness, Floor1InnerHeight, Floor2InnerHeight)
                : null,
            SluiceSpec = IsSluice
                ? new SluiceValveRoomSpecDto(BeamCountX, BeamOffsetX, BeamSpacingX, BeamCountY, BeamOffsetY, BeamSpacingY,
                    SelectedBeamTypeName == NotUsed ? string.Empty : SelectedBeamTypeName,
                    SelectedColumnTypeName == NotUsed ? string.Empty : SelectedColumnTypeName)
                : null
        };
        if (CreateModelAction != null) CreateModelAction(RequestedCreate);
        else CloseAction?.Invoke();
    }
    /// <param name="applyDefaults">기둥·보·헌치·Void 패밀리의 기본값을 자동 선택할지. 재로드(<see cref="ReloadTypeNames"/>)는
    /// 사용자가 이미 고른 값이 우선이라 false로 부르고, 복원 규칙을 직접 돌린다(docs/154 § B-1, docs/156 § C-3).</param>
    private void LoadTypeNames(bool applyDefaults = true)
    {
        foreach (var name in _typeQueryRepo.GetColumnFamilyTypeNames().Where(name => !string.IsNullOrWhiteSpace(name)).Distinct())
            ColumnTypeNames.Add(name);
        // 보와 헌치는 같은 구조 프레임 목록을 공용한다. 재료로 거르지 않는다(docs/159 § 2) —
        // 재료 판정은 아래 기본값 자동 선택에서만 쓴다.
        var framingTypeNames = _typeQueryRepo.GetStructuralFramingFamilyTypeNames()
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList();
        foreach (var name in framingTypeNames) BeamTypeNames.Add(name);
        foreach (var name in framingTypeNames) HaunchTypeNames.Add(name);
        foreach (var name in _typeQueryRepo.GetGenericModelTypeNames().Where(name => !string.IsNullOrWhiteSpace(name)).Distinct())
            DrainPitVoidFamilyNames.Add(name);

        if (!applyDefaults) return;

        // 기본값을 자동 선택한다. Void 패밀리는 setter가 매개변수 후보 갱신까지 이어서 돌린다.
        // 가드 두 개를 켜둬야 이 초기 선택이 "사용자가 직접 고른 값"으로 오인되지 않는다.
        _isApplyingTypeDefaults = true;
        _isRefreshingDrainPitParameters = true;
        try
        {
            SelectedColumnTypeName = ResolveDefaultTypeName(ColumnTypeNames, _typeQueryRepo.GetConcreteColumnTypeNames());
            SelectedBeamTypeName = ResolveDefaultTypeName(BeamTypeNames, _typeQueryRepo.GetConcreteBeamTypeNames());
            SelectedHaunchTypeName = GuessHaunchTypeName(HaunchTypeNames) ?? HaunchTypeAuto;
            SelectedDrainPitVoidFamily = GuessDrainPitVoidFamily(DrainPitVoidFamilyNames) ?? NotSelected;
        }
        finally { _isRefreshingDrainPitParameters = false; _isApplyingTypeDefaults = false; }
    }

    /// <summary>패밀리가 로드·편집된 뒤 유형 목록 전체를 다시 읽는다. 사용자가 고른 값은 새 목록에 남아 있으면 유지한다.
    /// Revit API를 부르므로 반드시 유효한 API 컨텍스트(ExternalEvent)에서 호출해야 한다(docs/154 § B-2).</summary>
    public void ReloadTypeNames()
    {
        // 캐시 키가 "문서Hash|패밀리명"이라, 같은 이름으로 재로드된 패밀리는 비워주지 않으면 낡은 매개변수명이 그대로 나온다.
        _typeQueryRepo.InvalidateFamilyCaches();

        // 컬렉션을 Clear하면 ComboBox가 SelectedItem에 null을 역기록하므로, 비우기 전에 값을 붙잡아 둔다.
        var column = _selectedColumnTypeName;
        var beam = _selectedBeamTypeName;
        var haunch = _selectedHaunchTypeName;
        var drainPitFamily = _selectedDrainPitVoidFamily;

        // 재적재·복원 구간 전체에서 가드를 켜 둬야 자동 복원이 "사용자가 직접 고른 값"으로 오인되지 않는다.
        _isApplyingTypeDefaults = true;
        _isRefreshingDrainPitParameters = true;
        try
        {
            ColumnTypeNames.Clear(); ColumnTypeNames.Add(NotUsed);
            BeamTypeNames.Clear(); BeamTypeNames.Add(NotUsed);
            HaunchTypeNames.Clear(); HaunchTypeNames.Add(HaunchTypeAuto);
            DrainPitVoidFamilyNames.Clear(); DrainPitVoidFamilyNames.Add(NotSelected);
            LoadTypeNames(applyDefaults: false);

            // 사용자가 직접 고른 값(pin)만 보존하고, 고른 적이 없거나 그 값이 사라졌으면 기본값을 다시 추정한다(docs/156 § F-3).
            SelectedColumnTypeName = ResolvePinnedTypeName(column, ref _columnTypePinned, ColumnTypeNames,
                () => ResolveDefaultTypeName(ColumnTypeNames, _typeQueryRepo.GetConcreteColumnTypeNames()));
            SelectedBeamTypeName = ResolvePinnedTypeName(beam, ref _beamTypePinned, BeamTypeNames,
                () => ResolveDefaultTypeName(BeamTypeNames, _typeQueryRepo.GetConcreteBeamTypeNames()));
            SelectedHaunchTypeName = ResolvePinnedTypeName(haunch, ref _haunchTypePinned, HaunchTypeNames,
                () => GuessHaunchTypeName(HaunchTypeNames) ?? HaunchTypeAuto);
            SelectedDrainPitVoidFamily = drainPitFamily != NotSelected && DrainPitVoidFamilyNames.Contains(drainPitFamily)
                ? drainPitFamily
                : GuessDrainPitVoidFamily(DrainPitVoidFamilyNames) ?? NotSelected;
        }
        finally { _isRefreshingDrainPitParameters = false; _isApplyingTypeDefaults = false; }

        // 패밀리 이름이 그대로면 위 setter가 SetProperty false로 조기 반환해 매개변수 목록이 갱신되지 않는다. 여기서 무조건 한 번 돌린다.
        // RefreshDrainPitVoidParameterNames의 finally가 가드를 무조건 false로 되돌리므로 위 try 안에서 부르지 않는다.
        RefreshDrainPitVoidParameterNames();
    }

    /// <summary>선택된 Void 패밀리의 인스턴스 매개변수를 다시 읽고, 사용자가 직접 고르지 않은 이름만 후보 키워드로 자동 추정한다.</summary>
    private void RefreshDrainPitVoidParameterNames()
    {
        _isRefreshingDrainPitParameters = true;
        try
        {
            DrainPitVoidParameterNames.Clear();
            DrainPitVoidParameterNames.Add(NotSelected);
            var names = SelectedDrainPitVoidFamily == NotSelected
                ? new List<string>()
                // 실수형(길이) 매개변수만 — 문자·정수·예/아니오를 고르면 Set(double)이 실패한다(docs/238 § E, **동작 변경**).
                : _typeQueryRepo.GetPipeAccessoryNumericInstanceParameterNames(SelectedDrainPitVoidFamily)
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList();
            foreach (var name in names) DrainPitVoidParameterNames.Add(name);

            // 같은 매개변수가 Length/Width/Depth에 중복 배정되면 형상이 틀어지므로 used로 제외한다.
            var used = new List<string>();
            SelectedDrainPitLengthParam = ResolveDrainPitParam(_selectedDrainPitLengthParam, ref _drainPitLengthParamPinned,
                names, DrainPitLengthParameterDefaultKeywords, used);
            SelectedDrainPitWidthParam = ResolveDrainPitParam(_selectedDrainPitWidthParam, ref _drainPitWidthParamPinned,
                names, DrainPitWidthParameterDefaultKeywords, used);
            SelectedDrainPitDepthParam = ResolveDrainPitParam(_selectedDrainPitDepthParam, ref _drainPitDepthParamPinned,
                names, DrainPitDepthParameterDefaultKeywords, used);
        }
        finally { _isRefreshingDrainPitParameters = false; }
    }

    /// <summary>사용자가 고른 이름이 새 패밀리에도 있으면 유지하고, 아니면 키워드로 다시 추정한다.
    /// 선례: PipeLayoutViewModel.RefreshFittingDiameterParameterNames.</summary>
    private static string ResolveDrainPitParam(string current, ref bool pinned,
        IReadOnlyList<string> names, IReadOnlyList<string> keywords, ICollection<string> used)
    {
        if (pinned && current != NotSelected && names.Contains(current)) { used.Add(current); return current; }
        pinned = false;
        var guess = GuessDrainPitParameter(names, keywords, used);
        if (guess != null) used.Add(guess);
        return guess ?? NotSelected;
    }

    /// <summary>키워드를 앞에서부터 시도해 매개변수명을 고른다. 키워드마다 완전일치를 먼저 보고, 두 글자 이상일 때만 부분일치로 넘어간다.
    /// 한 글자 키워드("L","W","H")는 대소문자 무시 Contains로 오탐이 심해 완전일치만 인정한다.</summary>
    private static string? GuessDrainPitParameter(IReadOnlyList<string> names, IReadOnlyList<string> keywords, ICollection<string> used)
    {
        foreach (var keyword in keywords)
        {
            var exact = names.FirstOrDefault(n => !used.Contains(n) && string.Equals(n, keyword, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            if (keyword.Length == 1) continue;
            var partial = names.FirstOrDefault(n => !used.Contains(n) && n.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (partial != null) return partial;
        }
        return null;
    }

    /// <summary>기둥·보 콤보의 기본 선택. 목록은 "패밀리명 : 타입명"이지만 콘크리트 후보 목록은 유형명 단독이라
    /// 타입명 부분으로 대조한다(docs/159 § B-2). 목록 순서(오름차순) 기준 첫 콘크리트 항목이며, 없으면 (사용 안 함)이다.
    /// 목록에 실제로 들어 있는 값만 고른다 — 없는 값을 넣으면 ComboBox가 빈칸이 된다(docs/156 § C-2).
    /// <para>패밀리가 달라도 타입명이 같으면 둘 다 콘크리트로 본다. 기본 선택 추정에만 쓰는 값이라 감수한다.</para></summary>
    private static string ResolveDefaultTypeName(IEnumerable<string> listed, IEnumerable<string> concrete)
    {
        var candidates = new HashSet<string>(concrete, StringComparer.Ordinal);
        return listed.FirstOrDefault(name => name != NotUsed && candidates.Contains(TypeNameOf(name))) ?? NotUsed;
    }

    /// <summary>사용자가 고른 유형이 새 목록에도 있으면 유지하고, 없거나 고른 적이 없으면 기본값을 다시 추정한다.
    /// 선례: <see cref="ResolveDrainPitParam"/>(docs/154 § B-1).</summary>
    private static string ResolvePinnedTypeName(string current, ref bool pinned,
        ICollection<string> names, Func<string> fallback)
    {
        if (pinned && names.Contains(current)) return current;
        pinned = false; // 고른 값이 사라졌으면 pin을 풀고 자동 추정으로 돌아간다
        return fallback();
    }

    /// <summary>헌치 기본 유형. 1순위 DH_헌치 패밀리, 2순위 패밀리명에 "헌치"가 든 것. 둘 다 없으면 null → (자동 탐색) 유지.
    /// 목록이 "패밀리명 : 타입명" 오름차순이라, 같은 패밀리에 유형이 여럿이면 타입명 사전순 첫 항목이 잡힌다.
    /// <para>타입명은 보지 않는다 — 그 완화된 탐색은 (자동 탐색) 경로의 FindHaunchBeamSymbol이 이미 맡는다(docs/156 § D-2).</para></summary>
    private static string? GuessHaunchTypeName(IEnumerable<string> familyTypeNames)
    {
        // (자동 탐색) sentinel을 먼저 걷어내야 키워드 검사가 그 항목을 집지 않는다. 호출부가 그 항목을 포함한 컬렉션을 넘긴다.
        var names = familyTypeNames.Where(name => name != HaunchTypeAuto).ToList();
        return names.FirstOrDefault(name => string.Equals(FamilyNameOf(name), HaunchPrimaryFamilyName, StringComparison.Ordinal))
            ?? names.FirstOrDefault(name => FamilyNameOf(name).Contains(HaunchFamilyKeyword, StringComparison.Ordinal));
    }

    /// <summary>"패밀리명 : 타입명"에서 패밀리명만. 구분자 규칙은 RevitElementTypeQueryRepo.GetFamilyTypeCategoryId와 같다
    /// — 타입명 자체에 " : "가 들어갈 수 있어 LastIndexOf를 쓴다.</summary>
    private static string FamilyNameOf(string familyTypeName)
    {
        var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        return separator <= 0 ? familyTypeName : familyTypeName[..separator];
    }

    /// <summary>"패밀리명 : 타입명"에서 타입명만. 구분자가 없으면 입력을 그대로 돌려준다(유형명 단독 목록 대비).
    /// 구분자 규칙은 <see cref="FamilyNameOf"/>와 같은 LastIndexOf(" : ")다.</summary>
    private static string TypeNameOf(string familyTypeName)
    {
        var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        return separator <= 0 || separator >= familyTypeName.Length - 3 ? familyTypeName : familyTypeName[(separator + 3)..];
    }

    /// <summary>"패밀리명 : 타입명" 목록에서 기본 Void 패밀리를 고른다. 목록이 비면 null.
    /// 1순위 기본 키워드("피트" → "보이드_사각형"…, 원형류 제외) → 2순위 "보이드"/"Void"(원형류 제외)
    /// → 3순위 목록 첫 항목 순으로 폴백한다.
    /// 3순위는 임의 추정이라 L/W/D 매개변수를 못 찾으면 (선택 안 함)으로 남아 RequestCreate 검사에 걸린다.</summary>
    private static string? GuessDrainPitVoidFamily(IEnumerable<string> familyTypeNames)
    {
        // NotSelected를 먼저 걷어내야 3순위 폴백이 "(선택 안 함)"을 집지 않는다. 호출부가 그 항목을 포함한 컬렉션을 넘긴다.
        var names = familyTypeNames.Where(n => n != NotSelected).ToList();
        if (names.Count == 0) return null;

        var primary = DrainPitVoidFamilyDefaultKeywords
            .Select(keyword => names.FirstOrDefault(n =>
                n.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                !DrainPitVoidFamilyExcludeKeywords.Any(x => n.Contains(x, StringComparison.OrdinalIgnoreCase))))
            .FirstOrDefault(n => n != null);
        if (primary != null) return primary;

        var fallback = DrainPitVoidFamilyFallbackKeywords
            .Select(keyword => names.FirstOrDefault(n =>
                n.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                !DrainPitVoidFamilyExcludeKeywords.Any(x => n.Contains(x, StringComparison.OrdinalIgnoreCase))))
            .FirstOrDefault(n => n != null);

        return fallback ?? names[0];
    }

    private Point3D ResolveDrainPitCenter(DrainPitItemViewModel pit)
    {
        var x = pit.Corner is "우상단" or "우하단" ? InnerLength - pit.LengthX / 2 : pit.LengthX / 2;
        var y = pit.Corner is "좌상단" or "우상단" ? InnerWidth - pit.WidthY / 2 : pit.WidthY / 2;
        return new Point3D(x, y, FoundationTopEl * 1000);
    }

    /// <summary>매핑창에서 고른 밸브실 행 → 입력칸. 유형을 먼저 바꾼다 — 유형 세터의 ApplyPreset이 L·W·본관 관경을 되돌리기 때문.</summary>
    private void ApplyValveRoomListRow(ValveRoomListRow row)
    {
        _placementRowNumber = row.RowNumber;
        _placementStationText = row.StationText;
        if (ValveRoomUsageTypes.Contains(row.RoomType)) SelectedValveRoomType = row.RoomType;
        else _dialogService.Warn("밸브실 목록", $"'{row.RoomType}'은 이 창에서 만들 수 없는 유형입니다. 유형은 그대로 두고 나머지 값만 채웁니다.");
        StationPoint = row.StationText;
        PlacementPipeDiameter = row.DiameterMm;
        PipeInvertEl = row.PipeInvertEl;
        GroundEl = row.GroundEl;
    }

    private void AutoPlace()
    {
        if (_excelReader is null || _fileDialog is null || _resolvePlacement is null)
        {
            _dialogService.Warn("위치 자동지정", "위치 자동지정 서비스가 연결되지 않았습니다.");
            return;
        }
        // 처음이면 매핑창에서 밸브실 행까지 골라야 확정된다 — 측점 칸이 프로젝트 정보의 옛 값으로 남지 않는다(VR-502 오발생 방지).
        if (_placementExcelPath is null || _placementExcelMapping is null)
        {
            if (!SelectPlacementExcel(reuseMapping: false)) return;
        }
        if (string.IsNullOrWhiteSpace(StationPoint))
        {
            _dialogService.Warn("위치 자동지정", $"[{ErrorCodes.VR_PlacementStationInvalid}] 밸브실측점을 입력하세요. 예) 1+750.00");
            return;
        }
        // 표고·원지반 칸이 없어 측점만 바뀌면 옛 행 값으로 계산된다(docs/310 § C-3).
        if (_placementStationText is not null && !string.Equals(StationPoint.Trim(), _placementStationText.Trim(), StringComparison.Ordinal))
        {
            _dialogService.Warn("위치 자동지정", $"측점이 엑셀에서 고른 행({_placementStationText})과 다릅니다. [엑셀 다시 지정]에서 밸브실 행을 다시 고르세요.");
            return;
        }
        if (!IsAir && InvertToFoundationTopMm <= 0)
        {
            _dialogService.Warn("위치 자동지정", "관저고 ~ 기초 상면 거리는 0보다 커야 합니다.");
            return;
        }

        // 제원표 L·W. 없으면(타이·표 밖 관경) 지금 값으로 계산하고 경고만 남긴다(docs/304 가정 3).
        var standard = _dimensionTable.Find(SelectedValveRoomType, PlacementPipeDiameter, IsSluice ? SelectedSluiceVariant : null);
        var innerLength = standard?.LengthMm ?? InnerLength;
        var innerWidth = standard?.WidthMm ?? InnerWidth;
        var usesIntermediateSlab = IsMud && HasIntermediateSlab;
        // 공기밸브실 Hp는 칸 값 말고 행 관경 공식. 성공 뒤 MainPipeDiameter 연동이 같은 값으로 칸을 맞춘다(docs/311 § C).
        var airDepth = ValveRoomPlacementRules.AirValvePipeCenterDepthMm(PlacementPipeDiameter);
        ValveRoomPlacementResolution resolution;
        try
        {
            resolution = _resolvePlacement.Execute(_placementExcelPath!, _placementExcelMapping!, StationPoint, point => new ValveRoomPlacementInput
            {
                Point = point,
                PipeInvertEl = PipeInvertEl,
                GroundEl = GroundEl,
                PipeDiameterMm = PlacementPipeDiameter,
                FoundationTopReference = IsAir ? FoundationTopReference.AbovePipeCenter : FoundationTopReference.BelowPipeInvert,
                FoundationTopOffsetMm = IsAir ? airDepth : InvertToFoundationTopMm,
                InnerLengthMm = innerLength,
                InnerWidthMm = innerWidth,
                UpperSlabThicknessMm = UpperSlabThickness,
                HasIntermediateSlab = usesIntermediateSlab,
                IntermediateSlabThicknessMm = IntermediateSlabThickness,
                Floor1InnerHeightMm = Floor1InnerHeight,
            });
        }
        catch (Exception ex)
        {
            // 계산 실패면 칸 하나도 안 바꾼다(부분 적용 금지, docs/300 § 3-5).
            _dialogService.Warn("위치 자동지정", UiErrors.Report("ValveRoom", ex));
            return;
        }

        var placement = resolution.Placement;
        InnerLength = innerLength;
        InnerWidth = innerWidth;
        if (IsAir)
        {
            MainPipeDiameter = PlacementPipeDiameter;
            // 관경 같으면 setter 연동 안 돔. EL 계산에 쓴 Hp를 칸에도 맞춘다.
            FoundationTopToPipeCenterDepth = airDepth;
        }
        // 화면 표기(소수 3자리)와 저장값을 맞춘다(docs/310 § E).
        SharedCoordinateX = Math.Round(placement.SharedX, 3);
        SharedCoordinateY = Math.Round(placement.SharedY, 3);
        TrueNorthToProjectNorthClockwiseDegrees = Math.Round(placement.TrueNorthToProjectNorthClockwiseDegrees, 3);
        // setter가 지운 뒤 기억해야 한다 — 순서 바꾸지 말 것.
        _placementCenter = (resolution.Point.X, resolution.Point.Y, placement.TrueNorthToProjectNorthClockwiseDegrees);
        FoundationTopEl = placement.FoundationTopEl;
        if (placement.Floor2InnerHeight is { } floor2)
        {
            // 전체 높이는 표고가 정함. 내부 높이 = H1 + Ts + H2로 넣고 H1=H2 분배는 ApplyAutoLayout에 맡김.
            // H1·H2 둘 다 손 수정이면 합이 안 맞으므로 H2를 풀어 차이를 흡수(종전 동작과 같음).
            if (_floor1InnerHeightPinned && _floor2InnerHeightPinned) _floor2InnerHeightPinned = false;
            InnerHeight = Floor1InnerHeight + IntermediateSlabThickness + floor2;
        }
        if (placement.InnerHeight is { } inner) InnerHeight = inner;

        var point = resolution.Point;
        var height = placement.Floor2InnerHeight is not null ? $"1F 내부 높이 {Floor1InnerHeight:N0} · 2F 내부 높이 {Floor2InnerHeight:N0} mm" : $"내부 높이 {placement.InnerHeight:N0} mm";
        var dimension = standard is null
            ? $"제원표에 {SelectedValveRoomType} DN{PlacementPipeDiameter:0} 없음 — 내부 L·W 유지 ({innerLength:N0}×{innerWidth:N0} mm)"
            : $"제원표 {standard.RoomKind}{(standard.Variant is null ? string.Empty : " " + standard.Variant)} DN{standard.DiameterMm:0} → L {standard.LengthMm:N0} · W {standard.WidthMm:N0} mm (표 높이 {standard.HeightMm:N0}은 참고)";
        var message = $"측점 {point.StationM:0.###} m · 관중심 EL(엑셀 표고) {placement.PipeCenterEl:0.###} m · 관저고 {PipeInvertEl:0.###} m (= 관중심 − DN/2) · 원지반 {GroundEl:0.###} m\n" +
                      $"{dimension}\n" +
                      $"기초 상부 EL {placement.FoundationTopEl:0.###} m · 상부슬래브 상면 EL {placement.TopSlabTopEl:0.###} m · {height}\n" +
                      $"도북각 {placement.TrueNorthToProjectNorthClockwiseDegrees:0.###}° 적용";
        if (IsSluice && standard is not null && standard.Variant != SelectedSluiceVariant)
            message += $"\n※ {SelectedSluiceVariant}에 DN{PlacementPipeDiameter:0}이 없어 {standard.Variant}을 적용했습니다.";
        if (resolution.Warnings.Count > 0) message += "\n\n" + string.Join("\n", resolution.Warnings);
        if (standard is null) _dialogService.Warn("위치 자동지정", message);
        else _dialogService.Info("위치 자동지정", message);
    }

    /// <summary>엑셀 선택 + 매핑. reuseMapping이면 기억한 파일·매핑으로 매핑창만 다시 연다. 확정하면 true.</summary>
    private bool SelectPlacementExcel(bool reuseMapping)
    {
        if (_excelReader is null || _fileDialog is null) return false;
        var path = reuseMapping ? _placementExcelPath : _fileDialog.OpenFile("밸브실 위치 — 선형 좌표표 선택", "엑셀·CSV (*.xlsx;*.csv)|*.xlsx;*.csv");
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (ShowExcelMappingDialog is null)
        {
            _dialogService.Warn("위치 자동지정", "엑셀 매핑 창을 열 수 없습니다.");
            return false;
        }

        ExcelAlignmentMappingViewModel mappingVm;
        try
        {
            mappingVm = reuseMapping && _placementExcelMapping is not null
                ? new ExcelAlignmentMappingViewModel(_excelReader, _dialogService, path, _placementExcelMapping)
                : new ExcelAlignmentMappingViewModel(_excelReader, _dialogService, path);
            mappingVm.IsValveRoomMode = true;
            if (reuseMapping && _placementRowNumber is { } rowNumber)
                mappingVm.SelectedValveRoomRow = mappingVm.ValveRoomRows.FirstOrDefault(x => x.RowNumber == rowNumber);
        }
        catch (Exception ex)
        {
            _dialogService.Warn("엑셀 읽기 실패", UiErrors.Report("ValveRoom", ex));
            return false;
        }

        if (!ShowExcelMappingDialog(mappingVm) || mappingVm.Result is not { } mappings) return false;
        if (mappings.Count != 1)
        {
            _dialogService.Warn("입력 확인", "밸브실 위치는 시트 1개만 지정하세요.");
            return false;
        }

        _placementExcelPath = path;
        _placementExcelMapping = mappings[0];
        if (mappingVm.SelectedValveRoomRow is { } selected) ApplyValveRoomListRow(selected);
        OnPropertyChanged(nameof(PlacementSourceText));
        CommandManager.InvalidateRequerySuggested();
        return true;
    }

    private void SetAndRefresh<T>(ref T field, T value)
    {
        if (!SetProperty(ref field, value)) return;
        OnPropertyChanged(nameof(Summary));
    }
}
