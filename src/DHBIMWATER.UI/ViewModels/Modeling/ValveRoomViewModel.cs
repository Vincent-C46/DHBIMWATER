using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Services;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Parameters;
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

    private double _sharedCoordinateX;
    private double _sharedCoordinateY;
    private double _foundationTopEl;
    private double _trueNorthToProjectNorthClockwiseDegrees;
    public ValveRoomViewModel(IDialogService dialogService, IElementTypeQueryRepo typeQueryRepo,
        IProjectInformationQueryRepo projectInfoQueryRepo,
        IConcreteSettingsRepo? concreteSettingsRepo = null, DHBIMWATER.Application.UseCases.AutoGenerator.SaveConcreteSettingsUseCase? saveConcreteSettings = null)
    {
        _dialogService = dialogService;
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
    public ICommand CreateCommand { get; }
    /// <summary>밸브실당 드레인 피트는 1개다. 생성 여부는 <see cref="HasDrainPit"/>가 결정한다.</summary>
    public DrainPitItemViewModel DrainPit { get; } = new();
    public Action<ValveRoomRequestDto>? CreateModelAction { get; set; }
    /// <summary>Revit API 컨텍스트에서 동작을 실행하는 훅(docs/238 § E). 창의 ExternalEvent로 연결된다.
    /// 드레인 피트 Void 매개변수 조회(EditFamily)를 WPF 디스패처에서 부르지 않기 위함이다. null이면(테스트) 바로 실행한다.</summary>
    public Action<Action>? RunInRevitContext { get; set; }
    /// <summary>[콘크리트 강도] 버튼(docs/249 § E). 밸브실 생성 UseCase가 문서 설정을 직접 읽는다.</summary>
    public ConcreteSettingsLauncher ConcreteLauncher { get; }

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
    public double MainPipeDiameter { get => _mainPipeDiameter; set => SetAndRefresh(ref _mainPipeDiameter, value); }
    public double FoundationTopToPipeCenterDepth { get => _foundationTopToPipeCenterDepth; set => SetAndRefresh(ref _foundationTopToPipeCenterDepth, value); }
    public string SelectedVoidAxis { get => _selectedVoidAxis; set => SetAndRefresh(ref _selectedVoidAxis, value); }
    public double OuterWallThickness { get => _outerWallThickness; set => SetAndRefresh(ref _outerWallThickness, value); }
    public double IntermediateWallThickness { get => _intermediateWallThickness; set => SetProperty(ref _intermediateWallThickness, value); }
    public double UpperSlabThickness { get => _upperSlabThickness; set => SetProperty(ref _upperSlabThickness, value); }
    public double IntermediateSlabThickness { get => _intermediateSlabThickness; set => SetProperty(ref _intermediateSlabThickness, value); }
    public double InnerWidth { get => _innerWidth; set => SetAndRefresh(ref _innerWidth, value); }
    public double InnerLength { get => _innerLength; set => SetAndRefresh(ref _innerLength, value); }
    public double InnerHeight { get => _innerHeight; set => SetAndRefresh(ref _innerHeight, value); }
    public double IntermediateWallOffset { get => _intermediateWallOffset; set => SetAndRefresh(ref _intermediateWallOffset, value); }
    public double Floor1InnerHeight { get => _floor1InnerHeight; set => SetAndRefresh(ref _floor1InnerHeight, value); }
    public double Floor2InnerHeight { get => _floor2InnerHeight; set => SetAndRefresh(ref _floor2InnerHeight, value); }
    public int BeamCountX { get => _beamCountX; set => SetAndRefresh(ref _beamCountX, Math.Max(0, value)); }
    public double BeamOffsetX { get => _beamOffsetX; set => SetProperty(ref _beamOffsetX, value); }
    public double BeamSpacingX { get => _beamSpacingX; set => SetProperty(ref _beamSpacingX, value); }
    public int BeamCountY { get => _beamCountY; set => SetAndRefresh(ref _beamCountY, Math.Max(0, value)); }
    public double BeamOffsetY { get => _beamOffsetY; set => SetProperty(ref _beamOffsetY, value); }
    public double BeamSpacingY { get => _beamSpacingY; set => SetProperty(ref _beamSpacingY, value); }

    public double SharedCoordinateX { get => _sharedCoordinateX; set => SetProperty(ref _sharedCoordinateX, value); }
    public double SharedCoordinateY { get => _sharedCoordinateY; set => SetProperty(ref _sharedCoordinateY, value); }
    /// <summary>기초 상부 EL(m). 레벨 표고가 되며 내부원점 표고는 항상 0이다.</summary>
    public double FoundationTopEl { get => _foundationTopEl; set => SetProperty(ref _foundationTopEl, value); }
    public double TrueNorthToProjectNorthClockwiseDegrees { get => _trueNorthToProjectNorthClockwiseDegrees; set => SetProperty(ref _trueNorthToProjectNorthClockwiseDegrees, value); }
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
        if (IsMud)
        {
            _innerWidth = 2500; _innerLength = 4000; _innerHeight = 2500;
            _foundationToe = 300;
            // 타이밸브실은 중간벽·중간슬래브를 쓰지 않는다(docs/144 § 1). 유형이 바뀔 때마다 이 프리셋이
            // 다시 도는 덕에 "이토에서 켠 중간슬래브"가 타이로 넘어가지 않는다.
            _hasIntermediateWall = !IsTie; _intermediateWallOffset = _innerLength / 2; _hasIntermediateSlab = false;
        }
        else if (IsSluice)
        {
            _innerWidth = 2000; _innerLength = 3000; _innerHeight = 2500;
            _foundationToe = 300;
            // 이토에서 켠 중간슬래브가 남으면 RequestCreate가 InnerHeight 대신 1F/2F를 검사한다(docs/238 § D).
            _hasIntermediateWall = false; _hasIntermediateSlab = false;
        }
        else
        {
            _innerWidth = 1500; _innerLength = 1800; _innerHeight = 2000;
            _foundationToe = 100; _mainPipeDiameter = 100; _foundationTopToPipeCenterDepth = 250; _selectedVoidAxis = "X";
            _hasIntermediateWall = false; _hasIntermediateSlab = false;
        }

        OnPropertyChanged(nameof(InnerWidth));
        OnPropertyChanged(nameof(InnerLength));
        OnPropertyChanged(nameof(InnerHeight));
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

    private void SetAndRefresh<T>(ref T field, T value)
    {
        if (!SetProperty(ref field, value)) return;
        OnPropertyChanged(nameof(Summary));
    }
}
