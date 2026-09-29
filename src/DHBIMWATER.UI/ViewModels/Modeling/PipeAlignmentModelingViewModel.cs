using DHBIMWATER.UI.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Input;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.UseCases.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;
using DHBIMWATER.UI.Views.Modeling;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>데이터 그리드를 분리해 보여주기 위한 소스 파일 구분. DXF는 DWG와 동일하게(레이어·XDATA 기반) 취급한다.</summary>
public enum PipeAlignmentSourceKind { Shp, Dwg, Excel }

public sealed record BendConnectionOption(string Label, BendConnection Value)
{
    public override string ToString() => Label;
}

public sealed record BendFormOption(string Label, BendForm Value)
{
    public override string ToString() => Label;
}

/// <summary>강관 곡관·이형관 종류(F12/F15/F20) 선택지(docs/211 § G-3).</summary>
public sealed record SteelFittingClassOption(string Label, SteelFittingClass Value)
{
    public override string ToString() => Label;
}

/// <summary>DWG·DXF 파일 하나에 포함된 레이어의 다중선택 항목.</summary>
public sealed class CadLayerOption : ViewModelBase
{
    private bool _isSelected;
    public required string Name { get; init; }
    public bool IsSelected { get => _isSelected; set { if (SetProperty(ref _isSelected, value)) SelectionChanged?.Invoke(); } }
    public Action? SelectionChanged { get; init; }
}

public sealed class PipeAlignmentSourceFileItem : ViewModelBase
{
    /// <summary>직경·관종 필드 콤보박스에서 "필드 대신 수동 입력값을 쓰겠다"를 뜻하는 항목. 실제 필드명과 겹치지 않도록 꺾쇠로 감싼다.</summary>
    public const string ManualFieldOption = "<수동>";
    private string _pipeKind = string.Empty;
    private string? _diameterField, _kindField;
    private double? _manualDiameterMm;
    private ShapefileReadResult _readResult = null!;
    private ExcelAlignmentMapping? _excelMapping;
    private IReadOnlyList<string?> _excelHeaders = Array.Empty<string?>();
    public required string Path { get; init; }
    public required ShapefileReadResult ReadResult
    {
        get => _readResult;
        set
        {
            if (!SetProperty(ref _readResult, value)) return;
            OnPropertyChanged(nameof(HasWarnings));
            OnPropertyChanged(nameof(WarningsDisplay));
            OnPropertyChanged(nameof(Fields));
            OnPropertyChanged(nameof(FieldOptions));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Details));
            OnPropertyChanged(nameof(ExcelMappingSummary));
        }
    }
    /// <summary>엑셀 소스일 때만 값이 있다. 같은 파일이라도 시트별로 별도 항목이 되므로 파일 경로만으로는 구분되지 않는다.</summary>
    public string? SheetName { get; init; }
    public ExcelAlignmentMapping? ExcelMapping
    {
        get => _excelMapping;
        set { if (SetProperty(ref _excelMapping, value)) OnPropertyChanged(nameof(ExcelMappingSummary)); }
    }
    /// <summary>엑셀 헤더 행의 셀 값(0-based 열 번호 순). 매핑 요약 표시용.
    /// <see cref="Fields"/>는 리더가 X·Y·Z 열과 빈 헤더를 빼고 만든 목록이라 열 번호로 찾으면 어긋난다.</summary>
    public IReadOnlyList<string?> ExcelHeaders
    {
        get => _excelHeaders;
        set { if (SetProperty(ref _excelHeaders, value)) OnPropertyChanged(nameof(ExcelMappingSummary)); }
    }
    public PipeAlignmentSourceKind SourceKind => SheetName is not null ? PipeAlignmentSourceKind.Excel
        : string.Equals(System.IO.Path.GetExtension(Path), ".shp", StringComparison.OrdinalIgnoreCase) ? PipeAlignmentSourceKind.Shp
        : PipeAlignmentSourceKind.Dwg;
    public string FileName => SheetName is null ? System.IO.Path.GetFileName(Path) : $"{System.IO.Path.GetFileName(Path)} [시트: {SheetName}]";
    public bool HasWarnings => ReadResult.Warnings.Count > 0;
    public string WarningsDisplay => string.Join("\n", ReadResult.Warnings);
    /// <summary>수동 등급도 규격표 조회 키의 일부라, 바뀌면 규격 미매칭 카운터를 다시 계산해야 한다.</summary>
    public string PipeKind { get => _pipeKind; set { if (SetProperty(ref _pipeKind, value)) MappingChanged?.Invoke(); } }
    public IReadOnlyList<string> PipeKinds => PipeKindCatalog.All;
    public string? DiameterField { get => _diameterField; set { if (SetProperty(ref _diameterField, value)) { OnPropertyChanged(nameof(IsManualDiameterEnabled)); MappingChanged?.Invoke(); } } }
    public bool IsManualDiameterEnabled => DiameterField == ManualFieldOption;
    public string? KindField { get => _kindField; set { if (SetProperty(ref _kindField, value)) { OnPropertyChanged(nameof(Details)); MappingChanged?.Invoke(); } } }
    /// <summary>현재 UI 입력 없음(91번 지시서). Loader 폴백 경로만 남아 있다.</summary>
    public double? ManualDiameterMm { get => _manualDiameterMm; set { if (SetProperty(ref _manualDiameterMm, value)) MappingChanged?.Invoke(); } }
    public IReadOnlyList<string> Fields => ReadResult.Fields.Select(x => x.Name).ToList();
    /// <summary>직경·관종 필드 콤보박스에 실제 표시하는 목록. 필드가 없거나(엑셀 미매핑 등) 있어도 항상 수동 옵션을 함께 보여준다.</summary>
    public IReadOnlyList<string> FieldOptions => new[] { ManualFieldOption }.Concat(Fields).ToList();
    public IReadOnlyList<string> Layers => ReadResult.Features.Select(x => x.Attributes.TryGetValue("LAYER", out var layer) ? layer : string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    /// <summary>DWG·DXF의 폴리선 레이어를 파일별로 독립 선택한다. 최초에는 모든 레이어를 선택한다.</summary>
    public ObservableCollection<CadLayerOption> LayerOptions { get; } = new();
    public IReadOnlyList<string>? SelectedLayers => LayerOptions.Count == 0 ? null : LayerOptions.Where(x => x.IsSelected).Select(x => x.Name).ToList();
    public void InitializeLayerOptions()
    {
        foreach (var layer in Layers)
            LayerOptions.Add(new CadLayerOption { Name = layer, IsSelected = true, SelectionChanged = () => MappingChanged?.Invoke() });
    }
    public string Summary => $"레코드 {ReadResult.RecordCount:N0} · 정점 {ReadResult.VertexCount:N0} · 구간 {ReadResult.SegmentCount:N0}";
    public string ExcelMappingSummary
    {
        get
        {
            if (ExcelMapping is not { } mapping) return "(매핑 없음)";
            var fields = new (string Label, int? Index)[]
            {
                ("X", mapping.XColumnIndex), ("Y", mapping.YColumnIndex), ("Z", mapping.ZColumnIndex),
                ("측점", mapping.StationColumnIndex), ("호칭지름", mapping.VertexDiameterColumnIndex), ("곡관유형", mapping.BendKindColumnIndex)
            };
            // 열 번호는 엑셀 열 기준(0=A)이므로 헤더 행에서 바로 찾는다. 헤더가 비었거나 못 읽었으면 열 문자만 보인다.
            return string.Join(", ", fields.Where(x => x.Index is >= 0).Select(x => $"{x.Label}={ColumnDisplay(x.Index!.Value)}"));
        }
    }
    private string ColumnDisplay(int index)
        => index < ExcelHeaders.Count && !string.IsNullOrWhiteSpace(ExcelHeaders[index])
            ? $"{ExcelHeaders[index]!.Trim()}({ColumnLetter(index)})"
            : $"{ColumnLetter(index)}열";
    private static string ColumnLetter(int index)
    {
        var letter = string.Empty; var n = index;
        do { letter = (char)('A' + n % 26) + letter; n = n / 26 - 1; } while (n >= 0);
        return letter;
    }
    public string Details => $"{Summary}\n관종 필드: {(KindField is null or ManualFieldOption ? "없음 (등급은 [규격 매핑]에서 지정)" : KindField)}\n필드: {string.Join(", ", Fields)}\n레이어: {string.Join(", ", Layers)}\n샘플: {string.Join(", ", (ReadResult.SampleAttributes ?? new Dictionary<string, string>()).Select(x => $"{x.Key}={x.Value}"))}\n인코딩: {ReadResult.EncodingName}";
    // WKT가 비는 원인은 두 가지다 — DXF라서 애초에 좌표계가 없는 경우와, SHP인데 형제 .prj가 없는 경우.
    // 둘을 같은 문구로 뭉치면 .prj 누락을 놓치므로 확장자로 분기한다.
    public string ProjectionDetails => !string.IsNullOrWhiteSpace(ReadResult.ProjectionSummary)
        ? $"{ReadResult.ProjectionSummary}{(ReadResult.RecommendedEpsg is null ? string.Empty : $" → {ReadResult.RecommendedEpsg}")}"
        : !string.IsNullOrWhiteSpace(ReadResult.ProjectionWkt)
            ? $"추천: {ReadResult.RecommendedEpsg ?? "없음"}\n{ReadResult.ProjectionWkt}"
        : System.IO.Path.GetExtension(Path).Equals(".shp", StringComparison.OrdinalIgnoreCase)
            ? "⚠ .prj 없음 → 좌표계 직접 확인"
            : $"좌표계 정보 없음 ({System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant()})";
    public string ProjectionToolTip => ReadResult.ProjectionWkt ?? string.Empty;
    public Action? MappingChanged { get; init; }
}

/// <summary>관로 선형 입력, 출력 모드, 곡관 설정과 사전 진단을 하나의 창에서 관리한다.</summary>
public sealed class PipeAlignmentModelingViewModel : ViewModelBase
{
    /// <summary>규격 매핑 등급을 고정하지 않고 관종 필드 해석에 맡긴다.</summary>
    public const string UseKindFieldOption = "(관종 필드 사용)";
    private readonly IFileDialogService _fileDialog; private readonly IDialogService _dialog;
    private readonly IReadOnlyList<IAlignmentSourceReader> _readers; private readonly AnalyzePipeNetworkUseCase _analyze;
    private readonly BendSettingsProvider _settingsProvider; private readonly SaveBendSettingsUseCase _save; private readonly IBendSettingsFileStore _settingsFileStore; private readonly IExcelAlignmentSourceReader _excelReader;
    private readonly IProjectLocationQueryRepo _projectLocationQuery; private readonly IElementTypeQueryRepo _typeRepo;
    private readonly IRevitDispatcher _revit;
    private readonly ILevelQueryRepo _levelRepo;
    // 진단 실행 중(docs/236 § C). 진단은 Revit 스레드 밖에서 돌 수 있어 그 사이 [진단]·[모델 생성]을 막는다.
    private bool _isDiagnosing;
    private PipeAlignmentSourceFileItem? _selectedFile; private PipeAlignmentOutputMode _outputMode;
    private double _referenceX, _referenceY, _intervalM = 6, _snapToleranceMm = 10;
    private int _selectedTabIndex;
    private bool _applySharedCoordinates, _propagatingReversal; private ZDatum _zDatum = ZDatum.Centerline;
    private string? _straightFamilyTypeName, _bendFamilyTypeName, _pipingSystemTypeName, _pipeTypeName, _levelName, _summary, _settingsNotice;
    private string? _straightDiameterParameterName, _straightOuterDiameterParameterName, _straightThicknessParameterName, _straightPointCountNotice, _bendPointCountNotice;
    private string? _bendDiameterParameterName, _bendWallThicknessParameterName, _bendOuterDiameterParameterName;
    private string _bulkMappingPipeKind = PipeKindCatalog.Water1;
    private BendConnectionOption? _bendConnectionOption;
    private BendFormOption? _bendFormOption;
    private SteelFittingClassOption? _steelFittingClassOption;
    /// <summary>창 전체의 관종(docs/211 § N). 등급 목록·소켓 전용 항목·접합 라벨·규격표 창 탭이 이 값을 따른다.</summary>
    private PipeMaterial _selectedMaterial = PipeMaterial.DuctileIron;
    private const double ExistingSharedCoordinateWarningDistanceM = 10_000d;
    private IReadOnlyList<string> _straightParameterNames = Array.Empty<string>(), _bendParameterNames = Array.Empty<string>();
    private BendSettings _settings = BendSettings.Default;
    private bool _isBusy, _hasResult, _isAwaitingPreviewConfirm;
    // 기본값 True — 매핑 결과 전체를 먼저 보여주고, 필요할 때 사용자가 미해결만 걸러 본다.
    private bool _showResolvedDiameterMappings = true;
    private bool _isIntervalHighlighted, _isStraightFamilyHighlighted, _isBendFamilyHighlighted;
    private ICollectionView? _diameterMappingsView;
    private PipeAlignmentModelingResult? _pendingPreview;
    private double _progressPercent;
    private string _phaseText = string.Empty, _progressDetail = string.Empty, _resultSummary = string.Empty, _resultWarningsText = string.Empty, _previewConfirmMessage = string.Empty;
    private readonly Stopwatch _phaseStopwatch = new();
    private PipeAlignmentPhase? _phaseTrackedPhase;
    private double _reducerBodyLengthMm = 250d;
    private int _phaseStartCompleted;
    private AlignmentOverviewModel? _overview;
    private bool _suspendDirectionSelection;
    private const int MinEtaSampleCount = 5;

    public PipeAlignmentModelingViewModel(IFileDialogService fileDialog, IDialogService dialog, IEnumerable<IAlignmentSourceReader> readers,
        IElementTypeQueryRepo typeRepo, ILevelQueryRepo levelRepo, AnalyzePipeNetworkUseCase analyze, BendSettingsProvider settingsProvider, SaveBendSettingsUseCase save, IBendSettingsFileStore settingsFileStore, IExcelAlignmentSourceReader excelReader, IProjectLocationQueryRepo projectLocationQuery, IRevitDispatcher revit)
    {
        _fileDialog = fileDialog; _dialog = dialog; _readers = readers.ToList(); _analyze = analyze; _settingsProvider = settingsProvider; _save = save; _settingsFileStore = settingsFileStore; _excelReader = excelReader; _projectLocationQuery = projectLocationQuery; _typeRepo = typeRepo; _revit = revit; _levelRepo = levelRepo;
        AdaptiveComponentTypeNames = typeRepo.GetAdaptiveComponentTypeNames().ToList(); PipingSystemTypeNames = typeRepo.GetPipingSystemTypeNames().ToList(); PipeTypeNames = typeRepo.GetPipeTypeNames().ToList(); LevelNames = levelRepo.GetExistingLevelNames().ToList();
        _straightFamilyTypeName = MaterialFamilyTypeName(_selectedMaterial, "직관") ?? AdaptiveComponentTypeNames.FirstOrDefault(); _bendFamilyTypeName = MaterialFamilyTypeName(_selectedMaterial, "곡관") ?? DefaultBendFamilyTypeName(); _pipingSystemTypeName = PipingSystemTypeNames.FirstOrDefault(); _pipeTypeName = PipeTypeNames.FirstOrDefault(); _levelName = LevelNames.FirstOrDefault();
        UpdateStraightParameterOptions();
        AddCommand = new RelayCommand(AddFile); RemoveCommand = new RelayCommand(_ => RemoveFile()); EditExcelMappingCommand = new RelayCommand(item => EditExcelMapping((PipeAlignmentSourceFileItem)item!)); RunCommand = new RelayCommand(async _ => await RunDiagnosisAsync(), _ => CanRunDiagnosis); CreateCommand = new RelayCommand(async _ => await RequestModelingAsync(), _ => CanCreateModel);
        // 창을 연 뒤 로드한 패밀리를 목록에 반영한다(docs/236 § D).
        RefreshFamilyListsCommand = new RelayCommand(_ => RefreshFamilyLists(), _ => CanRunDiagnosis); CancelCommand = new RelayCommand(_ => CloseAction?.Invoke());
        CancelModelingCommand = new RelayCommand(_ => { CancelModelingAction?.Invoke(); PhaseText = "취소 중 — 되돌리는 중"; }, _ => IsBusy);
        ConfirmPreviewCommand = new RelayCommand(_ => ConfirmPreview(), _ => IsAwaitingPreviewConfirm);
        CancelPreviewCommand = new RelayCommand(_ => CancelPreview(), _ => IsAwaitingPreviewConfirm);
        NavigatePreflightIssueCommand = new RelayCommand(issue => FocusPreflightIssue((PreflightIssue)issue!));
        ApplyBulkMappingCommand = new RelayCommand(_ => ApplyBulkMapping());
        OpenPipeSpecsCommand = new RelayCommand(_ => OpenPipeSpecs()); LoadSettings();
        Files.CollectionChanged += (_, _) => { NotifyFilesByKindChanged(); RefreshPreflightIssues(); };
        // docs/219 — 배치 제외 구간 탭. 반전 여부는 [선형 방향] 탭 행을 그대로 따른다.
        Exclusions.IntervalM = _intervalM;
        Exclusions.IsReversed = (fileKey, recordNumber) => DirectionRows.Any(x => x.IsReversed && x.FileKey == fileKey && x.RecordNumber == recordNumber);
        Exclusions.Changed = () => { RefreshPreflightIssues(); RefreshOverview(); };
        // docs/220 — [선형·제외 구간] 병합 탭. [＋] 대상은 첫 선택 선형, 표에서 고른 구간은 개요도에서 강조한다.
        Exclusions.PreferredTarget = PreferredExclusionTarget;
        Exclusions.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AlignmentExclusionViewModel.SelectedRow)) RefreshOverview(); };
        ReverseSelectedCommand = new RelayCommand(_ => ReverseSelected());
        SelectOverviewLineCommand = new RelayCommand(p => { if (p is AlignmentOverviewSelection s) SelectDirectionRow(s.Row, s.Additive); });
        SelectOverviewZoneCommand = new RelayCommand(p => SelectOverviewZone(p as AlignmentExclusionRow));
        RefreshPreflightIssues();
    }

    /// <summary>
    /// 곡관 패밀리 기본 선택값. 곡관 기본 형식이 B형(한쪽 소켓)이므로(<see cref="BendFittingCatalog"/>의
    /// ActiveBendForm 기본값) 유형 이름에 `B형`이 든 것을 먼저 고른다. 목록은 이름 오름차순이라
    /// (RevitElementTypeQueryRepo.GetAdaptiveComponentTypeNames) 그냥 첫 항목을 잡으면 A형이 걸린다.
    /// <para>곡관 타입명은 프로젝트마다 달라 코드에 상수로 박지 않는다 — 부분 일치로 찾고 실패하면
    /// 종전대로 5점 가변 첫 항목 → 목록 첫 항목 순으로 떨어진다.</para>
    /// </summary>
    private string? DefaultBendFamilyTypeName()
    {
        // 창을 열 때 모든 가변 패밀리에 EditFamily를 돌리던 경로다(docs/209 § D). 이름에 'B형'이 있으면 그것을 먼저 쓰고,
        // 없을 때만 점 개수(EditFamily)로 5점 패밀리를 찾는다. 고른 값은 UpdateBendParameterOptions가 점 개수를 검증한다.
        var byName = AdaptiveComponentTypeNames.FirstOrDefault(x => x.Contains("B형", StringComparison.Ordinal));
        if (byName is not null) return byName;
        var candidates = AdaptiveComponentTypeNames.Where(x => _typeRepo.GetAdaptiveBendPointCount(x) == 5).ToList();
        return candidates.FirstOrDefault() ?? AdaptiveComponentTypeNames.FirstOrDefault();
    }

    /// <summary>
    /// 관종 토글에 맞는 가변 패밀리("패밀리명 : 유형명")를 이름으로 찾는다(2026-09-23 사용자 지시).
    /// 관종 단어('주철관'/'강관')와 역할 단어('곡관'/'직관')가 모두 든 항목을 고르고, 곡관은 기본 형식인 B형을 먼저 쓴다.
    /// 이름만 보므로 EditFamily를 부르지 않는다(docs/209 § D). 못 찾으면 null — 호출부가 현재 선택을 유지한다.
    /// <para>주철관은 '주철관'이 0건이면 2순위로 '소켓'을 찾는다 — 현장 패밀리명(`DH_소켓_직관 : 80A-1000A`)에
    /// '주철관'이 없기 때문이다(docs/222 § A, 2026-09-23 사용자 지시). 강관은 '강관' 하나뿐이다.</para>
    /// </summary>
    private string? MaterialFamilyTypeName(PipeMaterial material, string role)
    {
        string[] materialWords = material switch { PipeMaterial.DuctileIron => ["주철관", "소켓"], PipeMaterial.Steel => ["강관"], _ => [] };
        foreach (var materialWord in materialWords)
        {
            var matches = AdaptiveComponentTypeNames
                .Where(x => x.Contains(materialWord, StringComparison.Ordinal) && x.Contains(role, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0) continue;
            return (role == "곡관" ? matches.FirstOrDefault(x => x.Contains("B형", StringComparison.Ordinal)) : null) ?? matches.FirstOrDefault();
        }
        return null;
    }

    /// <summary>관종을 바꾸면 곡관·직관 패밀리를 그 관종 이름의 것으로 바꾼다. 맞는 이름이 없으면 사용자가 고른 값을 그대로 둔다.</summary>
    private void SelectFamiliesForMaterial()
    {
        if (MaterialFamilyTypeName(_selectedMaterial, "곡관") is { } bend) BendFamilyTypeName = bend;
        if (MaterialFamilyTypeName(_selectedMaterial, "직관") is { } straight) StraightFamilyTypeName = straight;
    }

    /// <summary>배치 중 창 닫기를 막았을 때의 안내(docs/209 § A-5).</summary>
    public void NotifyCloseBlocked()
        => ProgressDetail = "배치가 끝나거나 [배치 취소]로 되돌린 뒤 닫을 수 있습니다.";
    public ObservableCollection<PipeAlignmentSourceFileItem> Files { get; } = new();
    public ObservableCollection<DiameterMappingRow> DiameterMappings { get; } = new();
    public ObservableCollection<PreflightIssue> PreflightIssues { get; } = new();
    // 이 VM은 Revit 메인 스레드에서 생성되지만 View는 전용 STA 스레드에서 산다.
    // CollectionView는 처음 만들어진 스레드의 Dispatcher에 묶이므로, 생성자에서 미리 만들면
    // View 스레드의 컬렉션 변경이 "발송자 스레드와 다른 스레드" 예외로 죽는다.
    // 그래서 View 스레드가 바인딩할 때 처음 접근하는 시점에 만든다.
    public ICollectionView DiameterMappingsView
    {
        get
        {
            if (_diameterMappingsView is null)
            {
                _diameterMappingsView = CollectionViewSource.GetDefaultView(DiameterMappings);
                _diameterMappingsView.Filter = item => ShowResolvedDiameterMappings || item is DiameterMappingRow row && !row.Status.StartsWith("✔", StringComparison.Ordinal);
            }
            return _diameterMappingsView;
        }
    }
    public bool ShowResolvedDiameterMappings { get => _showResolvedDiameterMappings; set { if (SetProperty(ref _showResolvedDiameterMappings, value)) RefreshDiameterMappingsView(); } }
    public bool HasNoVisibleDiameterMappings => DiameterMappingsView.IsEmpty;
    public bool HasPreflightIssues => PreflightIssues.Count > 0;
    public bool HasBlockingPreflightIssues => PreflightIssues.Any(x => x.Severity == PreflightSeverity.Blocking);
    public bool CanCreateModel => !IsBusy && !IsAwaitingPreviewConfirm && !IsDiagnosing && !HasBlockingPreflightIssues;
    /// <summary>[진단 실행]·[새로고침] 가능 여부. 배치·미리보기 대기·진단 중에는 막는다(docs/236 § C) —
    /// 버튼이 TabControl 밖이라 IsInputEnabled로는 잠기지 않았다.</summary>
    public bool CanRunDiagnosis => !IsBusy && !IsAwaitingPreviewConfirm && !IsDiagnosing;
    public bool IsDiagnosing
    {
        get => _isDiagnosing;
        private set
        {
            if (!SetProperty(ref _isDiagnosing, value)) return;
            OnPropertyChanged(nameof(CanCreateModel));
            OnPropertyChanged(nameof(CanRunDiagnosis));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    /// <summary>선형(레코드)별 진행 방향 지정 행. 입력 파일·레이어 선택이 바뀔 때마다 재구성한다.</summary>
    public ObservableCollection<AlignmentDirectionRow> DirectionRows { get; } = new();
    /// <summary>[배치 제외 구간] 탭(docs/219). 대상 선형 목록은 <see cref="RebuildDirectionRows"/>에서 같은 레이어 필터로 만든다.</summary>
    public AlignmentExclusionViewModel Exclusions { get; } = new();
    public int ReversedCount => DirectionRows.Count(x => x.IsReversed);
    /// <summary>선형 전체 개요도(docs/220 § C). 방향 행·반전·선택·제외 구간이 바뀔 때 다시 만든다.</summary>
    public AlignmentOverviewModel? Overview { get => _overview; private set => SetProperty(ref _overview, value); }
    public ICommand ReverseSelectedCommand { get; }
    public ICommand SelectOverviewLineCommand { get; }
    public ICommand SelectOverviewZoneCommand { get; }
    public IReadOnlyList<DiameterMappingOption> DiameterOptions { get; } = DiameterOptionsOf(StraightPipeSpecTable.NominalDiameters);
    // 관종별 DN 콤보(docs/212 § D-7). 주철관 행에 DN1350~3000이 뜨던 것을 막는다.
    private static readonly IReadOnlyList<DiameterMappingOption> DuctileIronDiameterOptions = DiameterOptionsOf(StraightPipeSpecTable.DuctileIronNominalDiameters);
    private static readonly IReadOnlyList<DiameterMappingOption> SteelDiameterOptions = DiameterOptionsOf(StraightPipeSpecTable.SteelNominalDiameters);
    private static IReadOnlyList<DiameterMappingOption> DiameterOptionsOf(IEnumerable<double> diameters)
        => new[] { new DiameterMappingOption("(지정 안 함)", null) }.Concat(diameters.Select(x => new DiameterMappingOption($"DN{x:0.##}", (double?)x))).ToList();
    /// <summary>창 전체의 관종 — 주철관/강관 토글(docs/211 § N, 2026-09-22 사용자 확정). 등급은 이 관종의 것만 고를 수 있다.
    /// 바꾸면 다른 관종 등급이 든 매핑 행은 새 관종의 기본 등급으로 다시 잡힌다(사용자 편집 포함 — 관종이 바뀌면 그 등급은 무의미하다).</summary>
    public PipeMaterial SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (!SetProperty(ref _selectedMaterial, value)) return;
            OnPropertyChanged(nameof(IsDuctileIronSelected)); OnPropertyChanged(nameof(IsSteelSelected));
            OnPropertyChanged(nameof(MappingPipeKinds)); OnPropertyChanged(nameof(SelectedMaterialLabel));
            BulkMappingPipeKind = DefaultGradeFor(value, 0d);
            SelectFamiliesForMaterial();
            RebuildDiameterMappings();
            Summary = string.Empty;
        }
    }
    public bool IsDuctileIronSelected { get => _selectedMaterial == PipeMaterial.DuctileIron; set { if (value) SelectedMaterial = PipeMaterial.DuctileIron; } }
    public bool IsSteelSelected { get => _selectedMaterial == PipeMaterial.Steel; set { if (value) SelectedMaterial = PipeMaterial.Steel; } }
    public string SelectedMaterialLabel => PipeAlignmentParameters.Label(_selectedMaterial);
    /// <summary>관종별 기본 등급 — 주철관은 상수 1종관, 강관은 DN으로 갈린다(docs/211 § 0 확정 7). DN을 모르면 STWW 290.</summary>
    private static string DefaultGradeFor(PipeMaterial material, double diameterMm)
        => material == PipeMaterial.Steel ? PipeKindCatalog.DefaultSteelGrade(diameterMm) : PipeKindCatalog.Water1;
    private static bool IsGradeOf(string? pipeKind, PipeMaterial material) => PipeKindCatalog.MaterialOf(pipeKind) == material;
    /// <summary>접합 종류 선택지. 강관이면 소켓 대신 "용접곡관"으로 보인다 — 강관의 Socket은 용접이다(docs/211 § G-4).
    /// 라벨만 다르고 값은 같은 <see cref="BendConnection"/>이다.</summary>
    public IReadOnlyList<BendConnectionOption> BendConnectionOptions => IsSteelSelected
        ? new[] { new BendConnectionOption("용접곡관", BendConnection.Socket), new BendConnectionOption("플랜지곡관", BendConnection.Flanged) }
        : new[] { new BendConnectionOption("소켓곡관", BendConnection.Socket), new BendConnectionOption("플랜지곡관", BendConnection.Flanged) };
    public IReadOnlyList<SteelFittingClassOption> SteelFittingClassOptions { get; } = new[]
    {
        new SteelFittingClassOption("F12", SteelFittingClass.F12),
        new SteelFittingClassOption("F15", SteelFittingClass.F15),
        new SteelFittingClassOption("F20", SteelFittingClass.F20)
    };
    /// <summary>강관 곡관·이형관 종류. 직관 등급(STWW)과 무관하며 곡관 관두께·무게가 이 값으로 정해진다(docs/211 § 0 확정 2).</summary>
    public SteelFittingClassOption? SteelFittingClassOption
    {
        get => _steelFittingClassOption;
        set
        {
            if (!SetProperty(ref _steelFittingClassOption, value) || value is null) return;
            _settings = _settings with { ActiveSteelFittingClass = value.Value };
            Summary = string.Empty;
        }
    }
    /// <summary>관종이 바뀌면 접합 라벨을 다시 계산한다. 라벨이 바뀐 목록에서 같은 값의 항목을 다시 잡는다(record 값 동등이라 라벨이 같으면 변경 없음).</summary>
    /// <summary>토글 관종에 맞는 접합 종류 안내만 보인다(docs/212 § D-1).</summary>
    public string BendConnectionToolTip => IsSteelSelected
        ? "강관: 용접 · 각도 허용차 90°·45° ±2°, 그 이하 ±1°"
        : "주철관 플랜지곡관: 45°·90°만 · 허용굴곡 0° → 미해결 증가";
    private void NotifyMaterialState()
    {
        OnPropertyChanged(nameof(BendConnectionOptions));
        OnPropertyChanged(nameof(BendConnectionToolTip));
        BendConnectionOption = BendConnectionOptions.First(x => x.Value == _settings.ActiveBendConnection);
    }
    public BendConnectionOption? BendConnectionOption
    {
        get => _bendConnectionOption;
        set
        {
            if (!SetProperty(ref _bendConnectionOption, value) || value is null) return;
            _settings = _settings with { ActiveBendConnection = value.Value };
            Summary = string.Empty;
        }
    }
    /// <summary>주철관 소켓 접합에 이탈방지압륜을 쓰는지. 산출서에서 `이탈방지압륜 접합 및 부설`이
    /// `KP접합 및 부설`과 별개 공종이라 개소를 다른 매개변수에 적어야 한다(docs/166 § 11-4).
    /// <para>형상·허용굴곡에는 영향이 없다. 강관·플랜지 접합에도 영향이 없다.</para></summary>
    public bool UseRestraintGland
    {
        get => _settings.UseRestraintGland;
        set
        {
            if (_settings.UseRestraintGland == value) return;
            _settings = _settings with { UseRestraintGland = value };
            OnPropertyChanged();
            Summary = string.Empty;
        }
    }
    public IReadOnlyList<BendFormOption> BendFormOptions { get; } = new[]
    {
        new BendFormOption("B형 (한쪽 소켓)", BendForm.BType),
        new BendFormOption("A형 (양쪽 소켓)", BendForm.AType)
    };
    public BendFormOption? BendFormOption
    {
        get => _bendFormOption;
        set
        {
            if (!SetProperty(ref _bendFormOption, value) || value is null) return;
            _settings = _settings with { ActiveBendForm = value.Value };
            Summary = string.Empty;
        }
    }
    /// <summary>등급 콤보 항목 — 선택한 관종의 등급만(docs/211 § N). "(관종 필드 사용)"은 주철관에서만 뜻이 있다(강관은 별칭이 등급이 아니라 DN으로 기본 등급을 잡는다).</summary>
    public IReadOnlyList<string> MappingPipeKinds => IsSteelSelected
        ? PipeKindCatalog.AllFor(PipeMaterial.Steel)
        : new[] { UseKindFieldOption }.Concat(PipeKindCatalog.AllFor(PipeMaterial.DuctileIron)).ToList();
    public string BulkMappingPipeKind { get => _bulkMappingPipeKind; set => SetProperty(ref _bulkMappingPipeKind, value); }
    // DataGrid 셀 편집(EditItem)은 IList를 지원하는 컬렉션 뷰가 필요하다. 지연 평가 IEnumerable(Where만 적용)을 그대로
    // 바인딩하면 EnumerableCollectionView로 감싸져 편집을 지원하지 않아 "EditItem을 사용할 수 없습니다" 예외가 난다.
    public IEnumerable<PipeAlignmentSourceFileItem> ShpFiles => Files.Where(x => x.SourceKind == PipeAlignmentSourceKind.Shp).ToList();
    public IEnumerable<PipeAlignmentSourceFileItem> DwgFiles => Files.Where(x => x.SourceKind == PipeAlignmentSourceKind.Dwg).ToList();
    public IEnumerable<PipeAlignmentSourceFileItem> ExcelFiles => Files.Where(x => x.SourceKind == PipeAlignmentSourceKind.Excel).ToList();
    public bool HasShpFiles => ShpFiles.Any(); public bool HasDwgFiles => DwgFiles.Any(); public bool HasExcelFiles => ExcelFiles.Any();
    private void NotifyFilesByKindChanged() { OnPropertyChanged(nameof(ShpFiles)); OnPropertyChanged(nameof(DwgFiles)); OnPropertyChanged(nameof(ExcelFiles)); OnPropertyChanged(nameof(HasShpFiles)); OnPropertyChanged(nameof(HasDwgFiles)); OnPropertyChanged(nameof(HasExcelFiles)); }
    public ObservableCollection<PipeNetworkNodeReport> Attention { get; } = new();
    public ObservableCollection<string> DiagnosisWarnings { get; } = new();
    public bool HasDiagnosisWarnings => DiagnosisWarnings.Count > 0;
    // [새로고침](docs/236 § D)이 통째로 바꾸므로 private set + 변경 알림(RefreshFamilyLists).
    public IReadOnlyList<string> AdaptiveComponentTypeNames { get; private set; }
    public IReadOnlyList<string> PipingSystemTypeNames { get; private set; }
    public IReadOnlyList<string> PipeTypeNames { get; private set; }
    public IReadOnlyList<string> LevelNames { get; private set; }
    public ICommand RefreshFamilyListsCommand { get; }
    public PipeAlignmentSourceFileItem? SelectedFile { get => _selectedFile; set { if (SetProperty(ref _selectedFile, value)) { OnPropertyChanged(nameof(SelectedFileDetails)); OnPropertyChanged(nameof(ProjectionDetails)); OnPropertyChanged(nameof(IsSelectedFileDwg)); } } }
    /// <summary>레이어 선택 입력란은 DWG·DXF 소스에서만 의미가 있다.</summary>
    public bool IsSelectedFileDwg => SelectedFile?.SourceKind == PipeAlignmentSourceKind.Dwg;
    public PipeAlignmentOutputMode OutputMode { get => _outputMode; set { if (SetProperty(ref _outputMode, value)) { OnPropertyChanged(nameof(IsDirectShape)); OnPropertyChanged(nameof(IsAdaptive)); OnPropertyChanged(nameof(IsPiping)); OnPropertyChanged(nameof(UsesStraightAndBendSettings)); OnPropertyChanged(nameof(IsAlignmentDirectionVisible)); OnPropertyChanged(nameof(HasSpecUnmatched)); OnPropertyChanged(nameof(SpecUnmatchedCount)); RefreshPreflightIssues(); } } }
    public bool IsDirectShape { get => OutputMode == PipeAlignmentOutputMode.DirectShape; set { if (value) OutputMode = PipeAlignmentOutputMode.DirectShape; } }
    public bool UsesStraightAndBendSettings => OutputMode is PipeAlignmentOutputMode.DirectShape or PipeAlignmentOutputMode.Adaptive;
    public bool IsAlignmentDirectionVisible => true;
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }
    /// <summary>직관 2점 가변 + 곡관 5점 가변 모드(구 빔 모드를 교체).</summary>
    public bool IsAdaptive { get => OutputMode == PipeAlignmentOutputMode.Adaptive; set { if (value) OutputMode = PipeAlignmentOutputMode.Adaptive; } }
    public bool IsPiping { get => OutputMode == PipeAlignmentOutputMode.PipingSystem; set { if (value) OutputMode = PipeAlignmentOutputMode.PipingSystem; } }
    public double ReferenceX { get => _referenceX; set => SetProperty(ref _referenceX, value); }
    public double ReferenceY { get => _referenceY; set => SetProperty(ref _referenceY, value); }
    public bool ApplySharedCoordinates { get => _applySharedCoordinates; set => SetProperty(ref _applySharedCoordinates, value); }
    public ZDatum ZDatum { get => _zDatum; set { if (SetProperty(ref _zDatum, value)) OnPropertyChanged(nameof(HasSpecUnmatched)); } }
    public Array ZDatums => Enum.GetValues(typeof(ZDatum));
    public double IntervalM { get => _intervalM; set { if (SetProperty(ref _intervalM, value)) { if (Exclusions is not null) Exclusions.IntervalM = value; RefreshPreflightIssues(); } } }
    public double ReducerBodyLengthMm { get => _reducerBodyLengthMm; set => SetProperty(ref _reducerBodyLengthMm, value); }
    public string? StraightFamilyTypeName { get => _straightFamilyTypeName; set { if (SetProperty(ref _straightFamilyTypeName, value)) { UpdateStraightParameterOptions(); RefreshPreflightIssues(); } } }
    public string? BendFamilyTypeName { get => _bendFamilyTypeName; set { if (SetProperty(ref _bendFamilyTypeName, value)) { UpdateBendParameterOptions(); RefreshPreflightIssues(); } } }
    public string? PipingSystemTypeName { get => _pipingSystemTypeName; set => SetProperty(ref _pipingSystemTypeName, value); }
    public string? PipeTypeName { get => _pipeTypeName; set => SetProperty(ref _pipeTypeName, value); }
    public string? LevelName { get => _levelName; set => SetProperty(ref _levelName, value); }
    /// <summary>직경·관종 필드 매핑 콤보박스에서 "매핑하지 않음"을 뜻하는 항목.</summary>
    public const string ManualParameterOption = "<사용 안 함>";
    /// <summary>선택된 직관 패밀리에서 조회한 인스턴스 파라미터명 + 수동 옵션. 패밀리가 바뀔 때마다 다시 조회한다.</summary>
    public IReadOnlyList<string> StraightParameterOptions => new[] { ManualParameterOption }.Concat(_straightParameterNames).ToList();
    /// <summary>호칭지름(mm)을 기록할 파라미터명. 패밀리 선택 시 후보 목록에서 자동 매칭을 시도하고, 실패하면 수동 옵션이 선택된다.</summary>
    public string? StraightDiameterParameterName { get => _straightDiameterParameterName; set => SetProperty(ref _straightDiameterParameterName, value); }
    public string? StraightOuterDiameterParameterName { get => _straightOuterDiameterParameterName; set => SetProperty(ref _straightOuterDiameterParameterName, value); }
    public string? StraightThicknessParameterName { get => _straightThicknessParameterName; set => SetProperty(ref _straightThicknessParameterName, value); }
    public IReadOnlyList<string> BendParameterOptions => new[] { ManualParameterOption }.Concat(_bendParameterNames).ToList();
    public string? BendDiameterParameterName { get => _bendDiameterParameterName; set => SetProperty(ref _bendDiameterParameterName, value); }
    public string? BendWallThicknessParameterName { get => _bendWallThicknessParameterName; set => SetProperty(ref _bendWallThicknessParameterName, value); }
    public string? BendOuterDiameterParameterName { get => _bendOuterDiameterParameterName; set => SetProperty(ref _bendOuterDiameterParameterName, value); }
    /// <summary>선택한 직관 패밀리의 Adaptive Point가 2개가 아닐 때 보여줄 경고. 2개면 빈 문자열이다.</summary>
    public string StraightPointCountNotice { get => _straightPointCountNotice ?? string.Empty; private set { if (SetProperty(ref _straightPointCountNotice, value)) OnPropertyChanged(nameof(HasStraightPointCountNotice)); } }
    public bool HasStraightPointCountNotice => !string.IsNullOrEmpty(StraightPointCountNotice);
    /// <summary>곡관 패밀리가 5점 가변이 아닐 때의 경고. 직관 쪽 <see cref="StraightPointCountNotice"/>와 같은 규칙이다.</summary>
    public string BendPointCountNotice { get => _bendPointCountNotice ?? string.Empty; private set { if (SetProperty(ref _bendPointCountNotice, value)) OnPropertyChanged(nameof(HasBendPointCountNotice)); } }
    public bool HasBendPointCountNotice => !string.IsNullOrEmpty(BendPointCountNotice);
    public double SnapToleranceMm { get => _snapToleranceMm; set => SetProperty(ref _snapToleranceMm, value); }
    public string Summary { get => _summary ?? string.Empty; private set { if (SetProperty(ref _summary, value)) OnPropertyChanged(nameof(DiagnosisText)); } }
    /// <summary>하단 진단 박스(docs/220 § E). 요약 + 경고 + 주의 절점 고정폭 표. 입력이 바뀌어 <see cref="Summary"/>가 비면 안내 문구로 돌아간다.</summary>
    public string DiagnosisText => BuildDiagnosisText();
    public string SettingsNotice { get => _settingsNotice ?? string.Empty; private set => SetProperty(ref _settingsNotice, value); }
    public string SelectedFileDetails => SelectedFile?.Details ?? "파일을 추가하면 DBF/XDATA 필드와 샘플 속성이 표시됩니다.";
    public string ProjectionDetails => SelectedFile?.ProjectionDetails ?? string.Empty;
    public int DiameterUnresolvedCount => CountUnresolved(); public bool HasDiameterUnresolved => DiameterUnresolvedCount > 0;
    /// <summary>
    /// 직관 제원표에서 (등급, DN) 행을 찾지 못하는 레코드 수. 직경 문자열 파싱은 성공해도 이 조회가 실패하면
    /// OD·두께가 기록되지 않고 관저고·크라운 기준 높이가 DN을 외경으로 간주한 폴백으로 계산되므로,
    /// <see cref="DiameterUnresolvedCount"/>보다 배치 품질을 직접 반영하는 지표다(모델 생성 전에 보여야 한다).
    /// </summary>
    public int SpecUnmatchedCount => CountSpecUnmatched();
    public bool HasSpecUnmatched => SpecUnmatchedCount > 0
        && (PipeElevationDatum.NeedsSpec(ZDatum) || UsesStraightAndBendSettings);
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(IsInputEnabled)); OnPropertyChanged(nameof(CanCreateModel)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } } }
    public bool IsInputEnabled => !IsBusy && !IsAwaitingPreviewConfirm;
    public double ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }
    public string PhaseText { get => _phaseText; private set => SetProperty(ref _phaseText, value); }
    public string ProgressDetail { get => _progressDetail; private set => SetProperty(ref _progressDetail, value); }
    public string ResultSummary { get => _resultSummary; private set => SetProperty(ref _resultSummary, value); }
    public ObservableCollection<string> ResultWarnings { get; } = new();
    public string ResultWarningsText { get => _resultWarningsText; private set => SetProperty(ref _resultWarningsText, value); }
    public bool HasResult { get => _hasResult; private set => SetProperty(ref _hasResult, value); }
    public bool IsAwaitingPreviewConfirm
    {
        get => _isAwaitingPreviewConfirm;
        private set
        {
            if (SetProperty(ref _isAwaitingPreviewConfirm, value))
            {
                OnPropertyChanged(nameof(IsInputEnabled));
                OnPropertyChanged(nameof(CanCreateModel));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    public string PreviewConfirmMessage { get => _previewConfirmMessage; private set => SetProperty(ref _previewConfirmMessage, value); }
    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand EditExcelMappingCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenPipeSpecsCommand { get; }
    public ICommand ApplyBulkMappingCommand { get; }
    public ICommand ConfirmPreviewCommand { get; }
    public ICommand CancelPreviewCommand { get; }
    public ICommand NavigatePreflightIssueCommand { get; }
    public Action? CloseAction { get; set; }
    /// <summary>진행 중인 배치 취소(docs/209 § A-5). 핸들러의 CancellationTokenSource를 세운다.</summary>
    public Action? CancelModelingAction { get; set; }
    public ICommand CancelModelingCommand { get; }
    public Action<PipeAlignmentModelingRequest>? CreateModelAction { get; set; }
    public Action? FinalizeModelAction { get; set; }
    public System.Windows.Window? OwnerWindow { get; set; }
    public PipeAlignmentModelingRequest? RequestedModeling { get; private set; }
    public Action<PreflightIssue>? NavigatePreflightIssueAction { get; set; }
    public bool IsIntervalHighlighted { get => _isIntervalHighlighted; private set => SetProperty(ref _isIntervalHighlighted, value); }
    public bool IsStraightFamilyHighlighted { get => _isStraightFamilyHighlighted; private set => SetProperty(ref _isStraightFamilyHighlighted, value); }
    public bool IsBendFamilyHighlighted { get => _isBendFamilyHighlighted; private set => SetProperty(ref _isBendFamilyHighlighted, value); }

    /// <summary>
    /// 파일 추가. <paramref name="kind"/>는 [파일 추가 ▾] 드롭다운에서 넘어오는 소스 종류로,
    /// 파일 대화상자 필터만 좁힌다(null이면 종전처럼 전체 형식). 선택 후 처리는 확장자로 판별해 종류와 무관하게 동일하다.
    /// </summary>
    private void AddFile(object? kind)
    {
        var (title, filter) = FileDialogFilter(kind as string);
        var path = _fileDialog.OpenFile(title, filter); if (string.IsNullOrWhiteSpace(path)) return;
        if (IsExcelSourcePath(path)) { AddExcelFile(path); return; }
        if (Files.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            var reader = _readers.FirstOrDefault(x => x.CanRead(path)) ?? throw new InvalidOperationException($"'{path}' 파일을 읽을 수 있는 리더가 없습니다."); var read = reader.Read(path); var item = new PipeAlignmentSourceFileItem { Path = path, ReadResult = read, MappingChanged = NotifyFileMappingChanged }; item.InitializeLayerOptions(); item.DiameterField = AlignmentAttributeParser.GuessDiameterField(read.Fields.Select(x => x.Name)) ?? PipeAlignmentSourceFileItem.ManualFieldOption; item.KindField = AlignmentAttributeParser.GuessKindField(read.Fields.Select(x => x.Name)) ?? PipeAlignmentSourceFileItem.ManualFieldOption; Files.Add(item); SelectedFile = item; RefreshReferencePoint(); NotifyFileMappingChanged();
        }
        catch (Exception ex) { _dialog.Warn("파일 읽기 실패", UiErrors.Report("PipeAlignment", ex)); }
    }
    /// <summary>엑셀 매핑 대화상자를 태워야 하는 소스인지. CSV도 시트 1개짜리 엑셀과 동일하게 취급한다.</summary>
    private static bool IsExcelSourcePath(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Title, string Filter) FileDialogFilter(string? kind) => kind switch
    {
        "Shp" => ("SHP 관로망 파일 선택", "Shapefile (*.shp)|*.shp"),
        "Dwg" => ("DWG·DXF 도면 파일 선택", "AutoCAD 도면 (*.dwg;*.dxf)|*.dwg;*.dxf"),
        "Excel" => ("엑셀·CSV 좌표표 선택", "엑셀·CSV (*.xlsx;*.csv)|*.xlsx;*.csv"),
        _ => ("관로 선형 파일 선택", "관로 선형 파일 (*.shp;*.dxf;*.dwg;*.xlsx;*.csv)|*.shp;*.dxf;*.dwg;*.xlsx;*.csv|모든 파일 (*.*)|*.*"),
    };

    private void AddExcelFile(string path)
    {
        ExcelAlignmentMappingViewModel mappingVm;
        try { mappingVm = new ExcelAlignmentMappingViewModel(_excelReader, _dialog, path); }
        catch (Exception ex) { _dialog.Warn("엑셀 읽기 실패", UiErrors.Report("PipeAlignment", ex)); return; }
        var dlg = new ExcelAlignmentMappingView(mappingVm);
        var owner = OwnerWindow;
        if (owner is not null) dlg.Owner = owner;
        dlg.ShowDialog();
        if (owner is not null) owner.Activate();
        if (mappingVm.Result is not { Count: > 0 } mappings) return;
        foreach (var mapping in mappings)
        {
            if (Files.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase) && string.Equals(x.SheetName, mapping.SheetName, StringComparison.Ordinal))) continue;
            try
            {
                var read = _excelReader.Read(path, mapping);
                var item = new PipeAlignmentSourceFileItem { Path = path, SheetName = mapping.SheetName, ExcelMapping = mapping, ExcelHeaders = ReadExcelHeaders(path, mapping), ReadResult = read, MappingChanged = NotifyFileMappingChanged };
                item.DiameterField = PipeAlignmentSourceFileItem.ManualFieldOption;
                item.KindField = PipeAlignmentSourceFileItem.ManualFieldOption;
                Files.Add(item); SelectedFile = item;
            }
            catch (Exception ex) { _dialog.Warn("엑셀 읽기 실패", $"{mapping.SheetName}: {UiErrors.Report("PipeAlignment", ex)}"); }
        }
        RefreshReferencePoint(); NotifyFileMappingChanged();
    }
    private void EditExcelMapping(PipeAlignmentSourceFileItem item)
    {
        if (item.ExcelMapping is not { } existing) return;
        ExcelAlignmentMappingViewModel mappingVm;
        try { mappingVm = new ExcelAlignmentMappingViewModel(_excelReader, _dialog, item.Path, existing); }
        catch (Exception ex) { _dialog.Warn("엑셀 읽기 실패", UiErrors.Report("PipeAlignment", ex)); return; }
        var dlg = new ExcelAlignmentMappingView(mappingVm);
        if (OwnerWindow is not null) dlg.Owner = OwnerWindow;
        dlg.ShowDialog();
        OwnerWindow?.Activate();
        if (mappingVm.Result is not { Count: 1 } mappings) return;
        try
        {
            var mapping = mappings[0];
            item.ReadResult = _excelReader.Read(item.Path, mapping);
            item.ExcelHeaders = ReadExcelHeaders(item.Path, mapping);
            item.ExcelMapping = mapping;
            OnPropertyChanged(nameof(ExcelFiles));
            RefreshReferencePoint();
            NotifyFileMappingChanged();
        }
        catch (Exception ex) { _dialog.Warn("엑셀 읽기 실패", $"{existing.SheetName}: {UiErrors.Report("PipeAlignment", ex)}"); }
    }
    /// <summary>매핑 요약에 쓸 시트 헤더 행. 시트마다 헤더가 다를 수 있어 시트별로 읽는다. 실패하면 요약이 열 문자로 떨어진다.</summary>
    private IReadOnlyList<string?> ReadExcelHeaders(string path, ExcelAlignmentMapping mapping)
    {
        try
        {
            var rows = _excelReader.PreviewRows(path, mapping.SheetName, mapping.HeaderRow);
            return mapping.HeaderRow <= rows.Count ? rows[mapping.HeaderRow - 1] : Array.Empty<string?>();
        }
        catch (Exception) { return Array.Empty<string?>(); }
    }
    private void RemoveFile() { if (SelectedFile is null) return; Files.Remove(SelectedFile); SelectedFile = Files.FirstOrDefault(); RefreshReferencePoint(); NotifyFileMappingChanged(); }
    private void RefreshReferencePoint() { var point = AlignmentReferencePoint.FromFirstVertex(Files.SelectMany(x => x.ReadResult.Features)); ReferenceX = point?.X ?? 0; ReferenceY = point?.Y ?? 0; }
    private static string? ResolvedField(string? field) => field is null or PipeAlignmentSourceFileItem.ManualFieldOption ? null : field;
    private static string? ResolvedParameter(string? name) => name is null or ManualParameterOption ? null : name;
    /// <summary>직관 패밀리에서 조회한 파라미터명으로 호칭지름·관종 후보 매칭을 다시 시도하고, Adaptive Point가 2개인지 검증한다.</summary>
    /// <summary>적응형·파이프 시스템·PipeType·레벨 목록을 다시 읽는다(docs/236 § D). 창이 모델리스라 연 뒤 로드한 패밀리가 보이지 않았다.
    /// 패밀리 파라미터·점 개수 캐시도 비운다. 선택값은 건드리지 않는다 — 새 목록에 없으면 사전 진단이 막는다.</summary>
    private void RefreshFamilyLists()
    {
        try
        {
            var (adaptive, systems, pipes, levels) = _revit.Run(() =>
            {
                _typeRepo.InvalidateFamilyCaches();
                return (_typeRepo.GetAdaptiveComponentTypeNames().ToList(), _typeRepo.GetPipingSystemTypeNames().ToList(),
                    _typeRepo.GetPipeTypeNames().ToList(), _levelRepo.GetExistingLevelNames().ToList());
            });
            AdaptiveComponentTypeNames = adaptive; PipingSystemTypeNames = systems; PipeTypeNames = pipes; LevelNames = levels;
            OnPropertyChanged(nameof(AdaptiveComponentTypeNames)); OnPropertyChanged(nameof(PipingSystemTypeNames));
            OnPropertyChanged(nameof(PipeTypeNames)); OnPropertyChanged(nameof(LevelNames));
            // 캐시를 비웠으므로 지금 고른 패밀리의 파라미터 후보·점 개수도 다시 읽는다.
            UpdateStraightParameterOptions();
            UpdateBendParameterOptions();
            RefreshPreflightIssues();
        }
        catch (Exception ex) { _dialog.Warn("패밀리 목록 새로고침", UiErrors.Report("PipeAlignment", ex)); }
    }

    private void UpdateStraightParameterOptions()
    {
        var familyType = StraightFamilyTypeName;
        var (parameterNames, pointCount) = _revit.Run(() => (
            familyType is null ? Array.Empty<string>() : _typeRepo.GetAdaptiveInstanceParameterNames(familyType).ToArray(),
            familyType is null ? -1 : _typeRepo.GetAdaptiveBendPointCount(familyType)));
        _straightParameterNames = parameterNames;
        OnPropertyChanged(nameof(StraightParameterOptions));
        // 곡관(UpdateBendParameterOptions)과 같은 규칙 — 파라미터명 "DN"을 최우선으로 잡고,
        // DN이 없는 패밀리만 기존 후보(직경/관경/Diameter …) 매칭으로 폴백한다.
        StraightDiameterParameterName = _straightParameterNames.FirstOrDefault(name => string.Equals(name, "DN", StringComparison.OrdinalIgnoreCase))
            ?? FamilyParameterMapper.GuessDiameterParameter(_straightParameterNames)
            ?? ManualParameterOption;
        StraightOuterDiameterParameterName = FamilyParameterMapper.GuessOuterDiameterParameter(_straightParameterNames) ?? ManualParameterOption;
        StraightThicknessParameterName = FamilyParameterMapper.GuessThicknessParameter(_straightParameterNames) ?? ManualParameterOption;
        // 배치 시점에 Repo가 다시 검증해 예외를 던지지만, 선택 즉시 알려주는 편이 낫다. -1은 확인 불가(패밀리 편집 실패 등)라 경고하지 않는다.
        StraightPointCountNotice = pointCount == 2 || pointCount < 0 ? string.Empty : $"Adaptive Point {pointCount}개 → 직관은 2점 필요";
    }
    private void UpdateBendParameterOptions()
    {
        var familyType = BendFamilyTypeName;
        var (parameterNames, pointCount) = _revit.Run(() => (
            familyType is null ? Array.Empty<string>() : _typeRepo.GetAdaptiveInstanceParameterNames(familyType).ToArray(),
            familyType is null ? -1 : _typeRepo.GetAdaptiveBendPointCount(familyType)));
        _bendParameterNames = parameterNames;
        OnPropertyChanged(nameof(BendParameterOptions));
        BendDiameterParameterName = _bendParameterNames.FirstOrDefault(name => string.Equals(name, "DN", StringComparison.OrdinalIgnoreCase)) ?? ManualParameterOption;
        BendWallThicknessParameterName = FamilyParameterMapper.GuessThicknessParameter(_bendParameterNames) ?? ManualParameterOption;
        BendOuterDiameterParameterName = FamilyParameterMapper.GuessOuterDiameterParameter(_bendParameterNames) ?? ManualParameterOption;
        // 직관과 같은 규칙 — -1(확인 불가: 패밀리 편집 실패 등)은 경고하지 않는다.
        BendPointCountNotice = pointCount == 5 || pointCount < 0 ? string.Empty : $"Adaptive Point {pointCount}개 → 곡관은 5점 필요";
    }
    // 강관이면 파일 수동 등급이 비어 있을 때 STWW 290을 넘겨 Loader 폴백(상수 1종관)이 관종을 넘지 않게 한다(docs/211 § N).
    // 매핑 행이 있는 표기는 행의 등급이 이기므로 DN별 기본 등급은 행에서 이미 잡혀 있다.
    private IReadOnlyList<AlignmentSourceFile> SourceFiles() => Files.Select(x => new AlignmentSourceFile(
        x.Path, IsSteelSelected && string.IsNullOrWhiteSpace(x.PipeKind) ? PipeKindCatalog.Steel290 : x.PipeKind, x.ExcelMapping is null ? ResolvedField(x.DiameterField) : null, x.ExcelMapping is null ? ResolvedField(x.KindField) : null, x.SelectedLayers, x.ExcelMapping, x.ManualDiameterMm,
        DiameterMappings.Where(row => row.FileKey == FileKey(x)).ToDictionary(
            row => row.MappingKey,
            row => new ResolvedPipeSpecKey(row.DiameterMm, string.IsNullOrWhiteSpace(row.PipeKind) || row.PipeKind == UseKindFieldOption ? null : row.PipeKind),
            StringComparer.Ordinal),
        DirectionRows.Where(row => row.IsReversed && row.FileKey == FileKey(x)).Select(row => row.RecordNumber).ToList(),
        // Loader가 창과 같은 관종으로 등급을 잡게 한다(docs/212 § A-2).
        SelectedMaterial,
        // docs/219 — 오류 없는 제외 구간만 싣는다(오류 행이 있으면 preflight가 생성을 막는다).
        Exclusions?.RangesFor(FileKey(x)))).ToList();
    // 확정 DN이 없는 표기 수일 뿐 배치 실패 수가 아니다. Loader는 매핑 다음에 자동 파싱·수동값 폴백을 적용한다.
    private int CountUnresolved() => DiameterMappings.Where(x => x.DiameterMm is null or <= 0).Sum(x => x.Count);
    private int CountSpecUnmatched() => DiameterMappings.Where(IsSpecUnmatched).Sum(x => x.Count);
    /// <summary>(등급, DN) 조합이 규격표에 없는 행. "(관종 필드 사용)" 행은 필드에서 해석한 등급으로 본다(docs/212 § C).</summary>
    private bool IsSpecUnmatched(DiameterMappingRow row)
        => row.DiameterMm is > 0 && row.LookupGrade(UseKindFieldOption) is { } grade && !string.IsNullOrWhiteSpace(grade)
           && _settings.StraightPipes.Find(grade, row.DiameterMm.Value) is null;
    private static string? FieldValue(PipeAlignment feature, string? field)
        => field is not null && feature.Attributes.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    private static string FileKey(PipeAlignmentSourceFileItem item) => $"{item.Path}\n{item.SheetName}";
    private void RebuildDiameterMappings()
    {
        // 사용자 편집을 보존하되, 관종 토글로 다른 관종 등급이 된 행은 버리고 새 관종 기본 등급으로 다시 잡는다(docs/211 § N).
        var preserved = DiameterMappings.Where(x => x.IsUserModified && (x.PipeKind == UseKindFieldOption ? IsDuctileIronSelected : IsGradeOf(x.PipeKind, SelectedMaterial)))
            .ToDictionary(x => (x.FileKey, x.MappingKey), x => (x.DiameterMm, x.PipeKind, x.IsDiameterUserModified));
        var rebuilt = new List<DiameterMappingRow>();
        foreach (var item in Files)
        {
            var diameterField = ResolvedField(item.DiameterField);
            var kindField = ResolvedField(item.KindField);
            // Loader와 같은 규칙: 레이어 목록이 null일 때만 전체 포함하고, 빈 목록은 전건 제외하며 이름은 Ordinal로 비교한다.
            var features = item.ReadResult.Features.Where(feature => item.SelectedLayers is not { } layers
                || feature.Attributes.TryGetValue("LAYER", out var layer) && layers.Contains(layer, StringComparer.Ordinal));
            var mappingValues = features.SelectMany(feature =>
            {
                var kindRaw = FieldValue(feature, kindField);
                var values = new List<(PipeAlignment Feature, string Raw, string KindRaw)>();
                if (diameterField is not null || kindField is not null)
                    values.Add((feature, DiameterMappingKey.Of(FieldValue(feature, diameterField)), DiameterMappingKey.Of(kindRaw)));
                if (feature.VertexDiameterRawValues is not null)
                    values.AddRange(feature.VertexDiameterRawValues.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => (feature, DiameterMappingKey.Of(value), DiameterMappingKey.Of(kindRaw))));
                return values;
            });
            foreach (var group in mappingValues.GroupBy(value => DiameterMappingKey.Of(value.Raw, value.KindRaw), StringComparer.Ordinal))
            {
                var first = group.First().Feature;
                var rawValue = group.First().Raw;
                var kindRawValue = group.First().KindRaw;
                var parsed = AlignmentAttributeParser.ParseDiameter(rawValue);
                var key = (FileKey(item), group.Key);
                double? diameterMm;
                string pipeKind;
                var isDefaultPipeKind = false;
                var isSteelDefault = false;
                // Loader와 같은 한 함수로 등급을 푼다(docs/212 § A-3) — 직경 셀에 붙은 관종("강관_D300")도 여기서 강관으로 잡힌다.
                // 사용자 수정 행에서도 "(관종 필드 사용)"이 실제로 잡을 등급을 알아야 하므로(§ C) 먼저 푼다.
                var grade = PipeKindCatalog.ResolveGrade(FieldValue(first, kindField), item.PipeKind, parsed.Kind, parsed.Success ? parsed.DiameterMm : 0d);
                // 관종은 창의 토글이 정한다(docs/211 § N). 필드에서 푼 등급이 그 관종 것일 때만 쓰고, 아니면 관종 기본 등급으로 잡는다.
                var resolvedIsUsable = grade.IsResolved && IsGradeOf(grade.Kind, SelectedMaterial);
                var isUserModified = preserved.TryGetValue(key, out var existing);
                if (isUserModified) { diameterMm = existing.DiameterMm; pipeKind = existing.PipeKind; }
                else
                {
                    diameterMm = parsed.Success && StraightPipeSpecTable.NominalDiameters.Any(x => Math.Abs(x - parsed.DiameterMm) <= 1e-6) ? parsed.DiameterMm : null;
                    if (IsSteelSelected)
                    {
                        // 강관: 등급 없이 관종만 온 값("강관"·"SP")도 DN으로 기본 등급을 잡는다(docs/211 § A-2). "(관종 필드 사용)"은 두지 않는다.
                        pipeKind = resolvedIsUsable ? grade.Kind! : PipeKindCatalog.DefaultSteelGrade(parsed.Success ? parsed.DiameterMm : 0d);
                        isSteelDefault = !resolvedIsUsable || grade.IsSteelDefault;
                    }
                    else
                    {
                        pipeKind = resolvedIsUsable ? grade.Kind! : kindField is not null ? UseKindFieldOption : PipeKindCatalog.Water1;
                        isDefaultPipeKind = !resolvedIsUsable && kindField is null;
                    }
                }
                var row = new DiameterMappingRow
                {
                    RawValue = rawValue, KindRawValue = kindRawValue, FileName = item.FileName, FileKey = key.Item1, Count = group.Count(),
                    AutoDiameterMm = parsed.Success ? parsed.DiameterMm : null,
                    Changed = DiameterMappingChanged,
                    // "(관종 필드 사용)" 행이 규격 조회·상태 표시에 쓸 필드 등급. 토글 관종의 등급일 때만 유효하다(docs/212 § A-4·§ C).
                    FieldGrade = resolvedIsUsable ? grade.Kind : null,
                    FieldMaterial = resolvedIsUsable ? SelectedMaterial : null
                };
                row.InitializeValues(DiameterOptions, diameterMm, pipeKind, isUserModified, isDefaultPipeKind, isSteelDefault, isUserModified && existing.IsDiameterUserModified);
                rebuilt.Add(row);
            }
        }
        DiameterMappings.Clear();
        foreach (var row in rebuilt) DiameterMappings.Add(row);
        RefreshDiameterMappingPreviews();
        NotifyMaterialState();
    }
    private void DiameterMappingChanged() => RefreshDiameterMappingPreviews();
    private void RefreshDiameterMappingPreviews()
    {
        var materialDiameterOptions = IsSteelSelected ? SteelDiameterOptions : DuctileIronDiameterOptions;
        foreach (var row in DiameterMappings)
        {
            var delegated = row.PipeKind == UseKindFieldOption;
            var grade = row.LookupGrade(UseKindFieldOption);
            var spec = row.DiameterMm is > 0 && !string.IsNullOrWhiteSpace(grade) ? _settings.StraightPipes.Find(grade, row.DiameterMm.Value) : null;
            // DN 콤보는 토글 관종의 목록만(docs/212 § D-7). 주철관에 DN1350~3000이 뜨던 것을 막는다.
            row.DiameterOptions = materialDiameterOptions;
            var status = row.DiameterMm is null or <= 0
                ? row.AutoDiameterMm is > 0 ? $"⚠ 규격표에 DN{row.AutoDiameterMm:0.##} 없음" : "⚠ DN 미지정"
                : delegated
                    ? grade is null
                        // 필드 값이 토글 관종의 등급이 아니다 — Loader가 기본 등급으로 잡는다(docs/212 § A-2).
                        ? $"⚠ '{row.KindRawValue}' {SelectedMaterialLabel} 등급이 아님 → {PipeKindCatalog.Water1}"
                        : spec is null ? $"⚠ {grade}에 DN{row.DiameterMm:0.##} 없음(관종 필드)" : $"✔ (관종 필드: {grade})"
                : string.IsNullOrWhiteSpace(row.PipeKind) ? "⚠ 등급 미지정"
                : spec is null ? $"⚠ {row.PipeKind}에 DN{row.DiameterMm:0.##} 없음"
                : row.IsSteelDefault ? "✔ (강관 기본 등급)" : row.IsDefaultPipeKind ? $"✔ (등급 기본값: {PipeKindCatalog.Water1})" : "✔";
            row.SetPreview(spec?.OuterDiameterMm, spec?.ThicknessMm, status);
        }
        OnPropertyChanged(nameof(DiameterUnresolvedCount)); OnPropertyChanged(nameof(HasDiameterUnresolved));
        OnPropertyChanged(nameof(SpecUnmatchedCount)); OnPropertyChanged(nameof(HasSpecUnmatched));
        RefreshDiameterMappingsView();
        RefreshPreflightIssues();
    }
    private void RefreshDiameterMappingsView()
    {
        // 아직 바인딩 전이면 CollectionView가 없다. 여기서 만들면 잘못된 스레드에 묶이므로 건너뛴다.
        if (_diameterMappingsView is null) return;
        _diameterMappingsView.Refresh();
        OnPropertyChanged(nameof(HasNoVisibleDiameterMappings));
    }
    /// <summary>표의 전 행에 자동추정 DN과 콤보에서 고른 등급을 한 번에 적용한다(선택 행 개념 없음).
    /// 사용자가 확정 DN 콤보로 직접 고른 행은 DN을 건드리지 않고 등급만 바꾼다(2026-09-23 사용자 지시).</summary>
    private void ApplyBulkMapping()
    {
        foreach (var row in DiameterMappings)
        {
            if (!row.IsDiameterUserModified && row.AutoDiameterMm is > 0 && StraightPipeSpecTable.NominalDiameters.Any(d => Math.Abs(d - row.AutoDiameterMm.Value) <= 1e-6))
                row.DiameterMm = row.AutoDiameterMm;
            if (!string.IsNullOrWhiteSpace(BulkMappingPipeKind))
                row.PipeKind = BulkMappingPipeKind;
        }
    }
    private void NotifyFileMappingChanged() { RebuildDiameterMappings(); RebuildDirectionRows(); }

    private void RefreshPreflightIssues(PipeNetworkDiagnosisResult? diagnosis = null)
    {
        if (PreflightIssues is null || DiameterMappings is null) return;
        var issues = new List<PreflightIssue>();
        if (Files.Count == 0) issues.Add(new(PreflightSeverity.Blocking, "입력 파일이 없습니다.", PipeAlignmentTab.Input, null));
        // 하한은 UseCase 상수와 같다(docs/209 § C). 0.006m 오타가 수백만 split로 이어지던 경로다.
        if (IntervalM * 1000 < ModelPipeAlignmentUseCase.MinIntervalMm) issues.Add(new(PreflightSeverity.Blocking, $"배치 간격은 {ModelPipeAlignmentUseCase.MinIntervalMm / 1000d:0.##}m 이상이어야 합니다.", PipeAlignmentTab.OutputMode, null));
        else if (IntervalM < 1) issues.Add(new(PreflightSeverity.Warning, $"배치 간격 {IntervalM:0.##}m는 정척(4~6m)보다 매우 짧습니다. 의도한 값인지 확인하세요.", PipeAlignmentTab.OutputMode, null));
        if (IsAdaptive && string.IsNullOrWhiteSpace(StraightFamilyTypeName)) issues.Add(new(PreflightSeverity.Blocking, "직관 패밀리를 선택하세요.", PipeAlignmentTab.OutputMode, null));
        if (IsAdaptive && string.IsNullOrWhiteSpace(BendFamilyTypeName)) issues.Add(new(PreflightSeverity.Blocking, "곡관 패밀리를 선택하세요.", PipeAlignmentTab.OutputMode, null));
        if (!string.IsNullOrWhiteSpace(StraightPointCountNotice)) issues.Add(new(PreflightSeverity.Warning, StraightPointCountNotice, PipeAlignmentTab.OutputMode, null));
        if (IsAdaptive && !string.IsNullOrWhiteSpace(BendPointCountNotice)) issues.Add(new(PreflightSeverity.Warning, BendPointCountNotice, PipeAlignmentTab.OutputMode, null));
        AddMappingIssue(issues, DiameterMappings.Where(x => x.DiameterMm is null or <= 0), "확정 DN 미지정");
        AddMappingIssue(issues, DiameterMappings.Where(x => string.IsNullOrWhiteSpace(x.PipeKind)), "등급 미지정");
        // 표에 없는 (등급, DN)은 직관·곡관 외경이 갈려 생성 자체를 막는다(docs/212 § C, 2026-09-22 확정 3).
        AddMappingIssue(issues, DiameterMappings.Where(IsSpecUnmatched), "규격 미매칭(등급·DN 조합이 규격표에 없음)", PreflightSeverity.Blocking);
        if (diagnosis is not null)
        {
            if (diagnosis.BendUnresolvedCount > 0) issues.Add(new(PreflightSeverity.Warning, $"허용 범위 초과 곡관 {diagnosis.BendUnresolvedCount}건", PipeAlignmentTab.Diagnosis, null));
            if (diagnosis.SizeConflictCount > 0) issues.Add(new(PreflightSeverity.Warning, $"치수 모순 {diagnosis.SizeConflictCount}건", PipeAlignmentTab.Diagnosis, null));
            // AnalyzePipeNetworkUseCase의 실제 문구("직관 자리 없는 구간 {n}건")와 맞춘다 — 종전 문구는 절대 매칭되지 않았다(docs/209 § G).
            if (diagnosis.Warnings.Any(x => x.StartsWith("직관 자리 없는 구간", StringComparison.Ordinal))) issues.Add(new(PreflightSeverity.Warning, "직관이 들어갈 자리가 없는 구간이 있습니다.", PipeAlignmentTab.Diagnosis, null));
        }
        // docs/219 D5 — 제외 구간 오류(형식·순서·범위·겹침)는 생성을 막는다. 경고(곡관 생략·짧은 절단관)는 행 상태에만 보인다.
        if (Exclusions is not null)
            foreach (var row in Exclusions.Rows.Where(x => x.HasError))
                issues.Add(new(PreflightSeverity.Blocking, $"배치 제외 구간 {row.Index}({row.DisplayName}): {row.Errors[0]}", PipeAlignmentTab.Exclusion, row.Id.ToString()));
        PreflightIssues.Clear();
        foreach (var issue in issues) PreflightIssues.Add(issue);
        foreach (var row in DiameterMappings)
            if (row.IsHighlighted && row.Status.StartsWith("✔", StringComparison.Ordinal)) row.IsHighlighted = false;
        if (IntervalM * 1000 >= ModelPipeAlignmentUseCase.MinIntervalMm) IsIntervalHighlighted = false;
        if (!string.IsNullOrWhiteSpace(StraightFamilyTypeName)) IsStraightFamilyHighlighted = false;
        if (!string.IsNullOrWhiteSpace(BendFamilyTypeName)) IsBendFamilyHighlighted = false;
        OnPropertyChanged(nameof(HasPreflightIssues)); OnPropertyChanged(nameof(HasBlockingPreflightIssues)); OnPropertyChanged(nameof(CanCreateModel));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private static void AddMappingIssue(List<PreflightIssue> issues, IEnumerable<DiameterMappingRow> rows, string message,
        PreflightSeverity severity = PreflightSeverity.Warning)
    {
        var matching = rows.ToList();
        if (matching.Count > 0) issues.Add(new(severity, $"{message} {matching.Sum(x => x.Count)}건", PipeAlignmentTab.SpecMapping, matching[0].MappingKey));
    }

    private void FocusPreflightIssue(PreflightIssue issue)
    {
        if (issue.TargetTab == PipeAlignmentTab.SpecMapping)
        {
            ShowResolvedDiameterMappings = true;
            foreach (var row in DiameterMappings)
                row.IsHighlighted = issue.Message.StartsWith("확정 DN", StringComparison.Ordinal) ? row.DiameterMm is null or <= 0
                    : issue.Message.StartsWith("등급 미지정", StringComparison.Ordinal) ? string.IsNullOrWhiteSpace(row.PipeKind)
                    : issue.Message.StartsWith("규격 미매칭", StringComparison.Ordinal) && IsSpecUnmatched(row);
        }
        else if (issue.TargetTab == PipeAlignmentTab.OutputMode)
        {
            IsIntervalHighlighted = issue.Message.StartsWith("배치 간격", StringComparison.Ordinal);
            IsStraightFamilyHighlighted = issue.Message.StartsWith("직관 패밀리", StringComparison.Ordinal) || issue.Message.Contains("직관은 2점");
            IsBendFamilyHighlighted = issue.Message.StartsWith("곡관 패밀리", StringComparison.Ordinal) || issue.Message.Contains("곡관은 5점");
        }
        else if (issue.TargetTab == PipeAlignmentTab.Exclusion) Exclusions.SelectById(issue.HighlightKey);
        NavigatePreflightIssueAction?.Invoke(issue);
    }

    /// <summary>
    /// 입력 파일에서 읽은 레코드를 방향 지정 행으로 펼친다. 이미 사용자가 지정한 반전 상태는
    /// (파일키, 레코드번호)로 보존하므로, 레이어 선택을 바꿔도 기존 지정이 날아가지 않는다.
    /// </summary>
    private void RebuildDirectionRows()
    {
        var preserved = DirectionRows.Where(x => x.IsReversed).Select(x => (x.FileKey, x.RecordNumber)).ToHashSet();
        var preservedSelection = DirectionRows.Where(x => x.IsSelected).Select(x => (x.FileKey, x.RecordNumber)).ToHashSet();
        var rebuilt = new List<AlignmentDirectionRow>();
        foreach (var item in Files)
        {
            // Loader·규격 매핑과 같은 레이어 필터 규칙(null=전체, 빈 목록=전건 제외, Ordinal 비교).
            var features = item.ReadResult.Features.Where(feature => item.SelectedLayers is not { } layers
                || feature.Attributes.TryGetValue("LAYER", out var layer) && layers.Contains(layer, StringComparer.Ordinal));
            foreach (var feature in features)
            {
                if (feature.Vertices.Count < 2) continue;
                var key = (FileKey(item), feature.RecordNumber);
                rebuilt.Add(new AlignmentDirectionRow
                {
                    FileKey = key.Item1, FileName = item.FileName, RecordNumber = feature.RecordNumber,
                    SourceStart = feature.Vertices[0], SourceEnd = feature.Vertices[^1],
                    VertexCount = feature.Vertices.Count, LengthM = PolylineLengthM(feature.Vertices),
                    IsReversed = preserved.Contains(key),
                    IsSelected = preservedSelection.Contains(key),
                    Feature = feature,
                    Changed = DirectionRowChanged,
                    SelectionChanged = DirectionRowSelectionChanged
                });
            }
        }
        // docs/220 § D — 선택은 (파일키, 레코드)로 보존하고, 남은 선택이 없으면 첫 선형을 고른다.
        if (rebuilt.Count > 0 && !rebuilt.Any(x => x.IsSelected))
        {
            _suspendDirectionSelection = true;
            try { rebuilt[0].IsSelected = true; }
            finally { _suspendDirectionSelection = false; }
        }
        DirectionRows.Clear();
        foreach (var row in rebuilt) DirectionRows.Add(row);
        OnPropertyChanged(nameof(ReversedCount));
        // docs/219 — 제외 구간 대상 선형도 같은 레코드 목록이다.
        Exclusions?.RebuildTargets(Files.SelectMany(item => item.ReadResult.Features
            .Where(feature => feature.Vertices.Count >= 2 && (item.SelectedLayers is not { } layers
                || feature.Attributes.TryGetValue("LAYER", out var layer) && layers.Contains(layer, StringComparer.Ordinal)))
            .Select(feature => AlignmentExclusionTargetOption.Create(FileKey(item), item.FileName, feature))));
        SyncExclusionFocus();
        RefreshOverview();
    }

    private void DirectionRowSelectionChanged(AlignmentDirectionRow _)
    {
        if (_suspendDirectionSelection) return;
        SyncExclusionFocus();
        RefreshOverview();
    }

    /// <summary>개요도 클릭·띠 클릭으로 선형을 고른다. Ctrl(추가)이면 켜고 끄되 마지막 하나는 남긴다.</summary>
    private void SelectDirectionRow(AlignmentDirectionRow row, bool additive)
    {
        _suspendDirectionSelection = true;
        try
        {
            if (!additive) foreach (var x in DirectionRows) x.IsSelected = ReferenceEquals(x, row);
            else if (!row.IsSelected) row.IsSelected = true;
            else if (DirectionRows.Count(x => x.IsSelected) > 1) row.IsSelected = false;
        }
        finally { _suspendDirectionSelection = false; }
        SyncExclusionFocus();
        RefreshOverview();
    }

    private void SelectOverviewZone(AlignmentExclusionRow? zone)
    {
        if (zone?.Target is not { } target) return;
        var line = DirectionRows.FirstOrDefault(x => x.FileKey == target.FileKey && x.RecordNumber == target.RecordNumber);
        if (line is not null) SelectDirectionRow(line, additive: false);
        Exclusions.SelectedRow = zone;
    }

    /// <summary>[선택 반전] — 고른 선형을 각각 뒤집는다(값 맞추기 전파 없이).</summary>
    private void ReverseSelected()
    {
        var rows = DirectionRows.Where(x => x.IsSelected).ToList();
        _propagatingReversal = true;
        try { foreach (var row in rows) row.IsReversed = !row.IsReversed; }
        finally { _propagatingReversal = false; }
    }

    private void SyncExclusionFocus()
        => Exclusions?.SetFocusTargets(DirectionRows.Where(x => x.IsSelected).Select(x => (x.FileKey, x.RecordNumber)));

    private AlignmentExclusionTargetOption? PreferredExclusionTarget()
        => DirectionRows.FirstOrDefault(x => x.IsSelected) is { } row
            ? Exclusions.Targets.FirstOrDefault(t => t.FileKey == row.FileKey && t.RecordNumber == row.RecordNumber)
            : null;

    /// <summary>
    /// 개요도 모델(docs/220 § C). 반전 선형은 Loader와 같은 규칙으로 뒤집은 정점을 쓴다 — 화살표가 6m 분절 시작 방향이다.
    /// 제외 구간은 측점 → 선형 거리로 바꿔 그 위치에 띠를 둔다(엑셀 Station은 위치에 고정, 없으면 진행 방향 시점부터 — 현행 규칙).
    /// </summary>
    private void RefreshOverview()
    {
        if (DirectionRows is null) return;
        var lines = new List<AlignmentOverviewLine>();
        var zones = new List<AlignmentOverviewZone>();
        foreach (var row in DirectionRows)
        {
            if (row.Feature is not { Vertices.Count: >= 2 } source) continue;
            var feature = row.IsReversed ? AlignmentExclusionViewModel.Reverse(source) : source;
            var profile = new AlignmentStationProfile(feature);
            lines.Add(new AlignmentOverviewLine(row, feature.Vertices.Select(v => new System.Windows.Point(v.X, v.Y)).ToList(), row.IsSelected,
                StationTextFormatter.Format(profile.StartStation)));
            if (Exclusions is null) continue;
            foreach (var zone in Exclusions.Rows)
            {
                if (zone.HasError || zone.Target is not { } target || target.FileKey != row.FileKey || target.RecordNumber != row.RecordNumber) continue;
                if (zone.StartStationM is not { } start || zone.EndStationM is not { } end) continue;
                if (!profile.TryGetDistanceAtStation(start, out var d1) || !profile.TryGetDistanceAtStation(end, out var d2)) continue;
                zones.Add(new AlignmentOverviewZone(zone, row, SubPolyline(feature.Vertices, Math.Min(d1, d2), Math.Max(d1, d2)), zone.DisplayName,
                    ReferenceEquals(zone, Exclusions.SelectedRow)));
            }
        }
        Overview = new AlignmentOverviewModel(lines, zones);
    }

    /// <summary>선형을 따라간 3D 거리 [from, to] 구간의 평면 좌표(AlignmentStationProfile과 같은 거리 기준).</summary>
    private static IReadOnlyList<System.Windows.Point> SubPolyline(IReadOnlyList<DHBIMWATER.Core.Geometry.Point3D> vertices, double from, double to)
    {
        var points = new List<System.Windows.Point>();
        var walked = 0d;
        for (var i = 1; i < vertices.Count; i++)
        {
            var (a, b) = (vertices[i - 1], vertices[i]);
            var length = a.DistanceTo(b);
            if (length <= 1e-9) continue;
            var (s, e) = (walked, walked + length);
            walked = e;
            if (e < from || s > to) continue;
            System.Windows.Point At(double d) { var t = Math.Clamp((d - s) / length, 0d, 1d); return new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }
            if (points.Count == 0) points.Add(At(Math.Max(from, s)));
            points.Add(At(Math.Min(to, e)));
            if (e >= to) break;
        }
        return points;
    }

    private string BuildDiagnosisText()
    {
        if (string.IsNullOrWhiteSpace(Summary)) return "[진단 실행]을 누르면 절점·곡관 판정 결과와 주의 절점이 여기에 표시됩니다. (모델 생성 시에도 자동 실행)";
        var text = new System.Text.StringBuilder(Summary);
        if (DiagnosisWarnings.Count > 0)
        {
            text.AppendLine().AppendLine().AppendLine($"[경고 {DiagnosisWarnings.Count}]");
            foreach (var warning in DiagnosisWarnings) text.AppendLine($"  {warning}");
        }
        if (Attention.Count > 0)
        {
            // 주의 절점 표 → 고정폭 텍스트(docs/220 § E). 한글은 2칸으로 세어 열을 맞춘다.
            var columns = new (string Header, int Width, bool Right)[]
                { ("절점", 6, false), ("종류", 14, false), ("곡관판정", 14, false), ("X(m)", 13, true), ("Y(m)", 13, true), ("Z(m)", 9, true), ("편각(°)", 8, true), ("직경", 6, true), ("관종", 12, false) };
            text.AppendLine().AppendLine().AppendLine($"[주의 절점 {Attention.Count}]");
            text.AppendLine("  " + string.Join("  ", columns.Select(c => PadDisplay(c.Header, c.Width, c.Right))).TrimEnd());
            foreach (var node in Attention)
            {
                var cells = new[] { node.NodeId.ToString(), node.Kind, node.BendResolution, node.X.ToString("0.###"), node.Y.ToString("0.###"), node.Z.ToString("0.###"),
                    node.DeflectionDeg.ToString("0.##"), node.MaxDiameterMm.ToString("0.#"), node.PipeKind };
                text.AppendLine("  " + string.Join("  ", cells.Select((c, i) => PadDisplay(c, columns[i].Width, columns[i].Right))).TrimEnd());
            }
        }
        return text.ToString().TrimEnd();
    }

    private static string PadDisplay(string? value, int width, bool right)
    {
        value ??= string.Empty;
        var length = value.Sum(c => c >= 0x1100 && (c <= 0x115F || c >= 0x2E80) ? 2 : 1);
        var fill = new string(' ', Math.Max(0, width - length));
        return right ? fill + value : value + fill;
    }
    private static double PolylineLengthM(IReadOnlyList<DHBIMWATER.Core.Geometry.Point3D> vertices)
    {
        var length = 0d;
        for (var i = 1; i < vertices.Count; i++) length += vertices[i - 1].DistanceTo(vertices[i]);
        return length;
    }
    /// <summary>
    /// 체크박스로 바뀐 행이 다중 선택에 포함돼 있으면 선택한 나머지 행도 같은 값으로 맞춘다.
    /// 선택에 없는 행을 클릭했을 때는 그 행만 바뀐다(선택과 무관한 단독 편집).
    /// </summary>
    private void DirectionRowChanged(AlignmentDirectionRow source)
    {
        if (!_propagatingReversal && source.IsSelected)
        {
            _propagatingReversal = true;
            try
            {
                foreach (var row in DirectionRows.Where(x => x.IsSelected && !ReferenceEquals(x, source)).ToList())
                    row.IsReversed = source.IsReversed;
            }
            finally { _propagatingReversal = false; }
        }
        OnPropertyChanged(nameof(ReversedCount));
        // 반전하면 분절이 반대쪽 끝에서 시작하므로 경계 절단관 길이가 달라진다(docs/219 미리보기).
        Exclusions?.Revalidate();
        RefreshOverview();
    }
    /// <summary>사전 진단. 파일 읽기·위상 분석은 Revit API가 필요 없으므로 Revit 스레드 밖(Task.Run)에서 돈다(docs/236 § C) —
    /// 종전에는 RevitDispatcher로 Revit 스레드에서 돌아 30초(PA-903)를 넘는 대용량 입력은 모델 생성까지 막혔다.
    /// 규격 설정이 없을 때만(<see cref="AnalyzePipeNetworkUseCase"/>가 문서에서 읽는다) 종전처럼 Revit 스레드로 보낸다.
    /// await 뒤는 창 디스패처로 돌아오므로 컬렉션 갱신은 UI 스레드에서 한다.</summary>
    private async Task<PipeNetworkDiagnosisResult?> RunDiagnosisAsync()
    {
        if (Files.Count == 0) { _dialog.Warn("입력 확인", "하나 이상의 SHP, DXF, DWG 또는 엑셀·CSV 파일을 추가하세요."); return null; }
        if (SnapToleranceMm <= 0) { _dialog.Warn("입력 확인", "스냅 허용오차는 0보다 커야 합니다."); return null; }
        if (IsDiagnosing) return null;
        IsDiagnosing = true;
        try
        {
            var request = new PipeNetworkDiagnosisRequest { Files = SourceFiles(), SnapToleranceMm = SnapToleranceMm, CurrentBendSettings = _settings };
            var result = request.CurrentBendSettings is null
                ? _revit.Run(() => _analyze.Execute(request))
                : await Task.Run(() => _analyze.Execute(request));
            Attention.Clear();
            foreach (var report in result.Attention) Attention.Add(report);
            DiagnosisWarnings.Clear();
            foreach (var warning in result.Warnings) DiagnosisWarnings.Add(warning);
            OnPropertyChanged(nameof(HasDiagnosisWarnings));
            OnPropertyChanged(nameof(DiagnosisText));
            var angles = result.BendAngleCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(3)
                .Select(x => $"{x.Key:0.##}° {x.Value}");
            var bendDetails = result.BendAngleCounts.Count == 0
                ? string.Empty
                : $" ({string.Join(" · ", angles)}{(result.BendAngleCounts.Count > 3 ? $" · 외 {result.BendAngleCounts.Count - 3}종" : string.Empty)})";
            var issues = new[]
            {
                result.Warnings.Count > 0 ? $"경고 {result.Warnings.Count}" : null,
                result.BendUnresolvedCount > 0 ? $"미해결 {result.BendUnresolvedCount}" : null,
                SpecUnmatchedCount > 0 ? $"규격 미매칭 {SpecUnmatchedCount}" : null
            }.Where(x => x is not null);
            Summary =
                $"절점 {result.NodeCount} · 간선 {result.EdgeCount} · 곡관 {result.BendStandardCount}본{bendDetails}\n" +
                (issues.Any() ? string.Join(" · ", issues) : "이상 없음");
            OnPropertyChanged(nameof(DiagnosisText));
            return result;
        }
        catch (Exception ex) { _dialog.Warn("관로 네트워크 진단", $"진단에 실패했습니다.\n{UiErrors.Report("PipeAlignment", ex)}"); return null; }
        finally { IsDiagnosing = false; }
    }
    private async Task RequestModelingAsync()
    {
        RefreshPreflightIssues();
        if (HasBlockingPreflightIssues) return;
        var diagnosis = await RunDiagnosisAsync();
        if (diagnosis is null) return;
        RefreshPreflightIssues(diagnosis);
        var warnings = PreflightIssues.Where(x => x.Severity == PreflightSeverity.Warning).Select(x => x.Message).ToList();
        if (warnings.Count > 0 && !_dialog.Confirm("사전 진단 경고", $"{string.Join(", ", warnings)}을 확인했습니다. 계속 모델링할까요?")) return;
        var reference = ResolveReference();
        // DH_야간은 항상 0(주간)으로 기록한다. PipeAlignmentModelingRequest.IsNightWork 기본값이 false이므로
        // 값을 넘기지 않아도 파라미터가 비지 않는다 — 관공사 수량 집계의 "값 없음" 방지(docs/141 § C-5, docs/146).
        RequestedModeling = new PipeAlignmentModelingRequest { Files = SourceFiles(), ReferenceX = reference.X, ReferenceY = reference.Y, ApplySharedCoordinates = ApplySharedCoordinates, ZDatum = ZDatum, OutputMode = OutputMode, IntervalMm = IntervalM * 1000, ReducerBodyLengthMm = ReducerBodyLengthMm, StraightFamilyTypeName = StraightFamilyTypeName, StraightDiameterParameterName = ResolvedParameter(StraightDiameterParameterName), StraightOuterDiameterParameterName = ResolvedParameter(StraightOuterDiameterParameterName), StraightThicknessParameterName = ResolvedParameter(StraightThicknessParameterName), BendFamilyTypeName = BendFamilyTypeName, BendDiameterParameterName = ResolvedParameter(BendDiameterParameterName), BendWallThicknessParameterName = ResolvedParameter(BendWallThicknessParameterName), BendOuterDiameterParameterName = ResolvedParameter(BendOuterDiameterParameterName), PipingSystemTypeName = PipingSystemTypeName, PipeTypeName = PipeTypeName, LevelName = LevelName, SnapToleranceMm = SnapToleranceMm, CurrentBendSettings = _settings };
        SetResultWarnings([]); HasResult = false; ResultSummary = string.Empty;
        ProgressPercent = 0; PhaseText = "모델링 준비 중"; ProgressDetail = string.Empty; IsBusy = true;
        try
        {
            if (CreateModelAction is null) throw new InvalidOperationException("모델 생성 요청이 연결되지 않았습니다.");
            CreateModelAction(RequestedModeling);
        }
        catch (Exception ex) { ApplyFailure(UiErrors.Report("PipeAlignment", ex)); }
    }
    /// <summary>
    /// 실제로 배치에 쓸 평면 기준점을 확정한다. 형상은 항상 (정점 - 기준점)만큼 프로젝트 기준점(PBP)에서 떨어져 놓이므로,
    /// 파일마다 기준점이 달라지면 같은 문서 안에서도 관로가 서로 어긋난다. 그래서 체크 해제 상태에서는
    /// 화면 기준점(= 이번 파일의 첫 정점)을 쓰지 않고 문서에 이미 잡힌 공유좌표를 따라간다.
    /// </summary>
    private (double? X, double? Y) ResolveReference()
    {
        if (ApplySharedCoordinates) return (ReferenceX, ReferenceY);
        try
        {
            var (eastWest, northSouth) = _revit.Run(() => _projectLocationQuery.GetProjectBasePointSharedPosition());
            if (Math.Abs(eastWest) >= 1e-4 || Math.Abs(northSouth) >= 1e-4)
                return AlignToExistingSharedCoordinates(eastWest, northSouth);

            if (_dialog.Confirm("공유좌표 확인", "현재 프로젝트 기준점의 공유좌표가 (0, 0)입니다.\n\n[예] 기준점(X, Y)을 프로젝트 공유좌표로 기록하고, 그 점이 프로젝트 기준점에 놓이도록 배치합니다.\n[아니오] 기준점을 적용하지 않고 원본 좌표 그대로 배치합니다. 모델이 내부 원점에서 매우 멀어져 Revit 정밀도 경고가 발생할 수 있습니다."))
            {
                ApplySharedCoordinates = true;
                return (ReferenceX, ReferenceY);
            }
            // '아니오' = 좌표 이동 없음. 원본 좌표를 그대로 배치해야 이후 다른 파일을 가져와도 서로 정합한다.
            return (0d, 0d);
        }
        catch { return (ReferenceX, ReferenceY); /* PBP 조회에 실패해도 모델링 자체는 막지 않는다 */ }
    }

    /// <summary>문서에 이미 공유좌표가 잡혀 있으면 그 좌표를 기준점으로 삼아, 먼저 임포트한 관로와 같은 위치에 놓이게 한다.</summary>
    private (double? X, double? Y) AlignToExistingSharedCoordinates(double eastWest, double northSouth)
    {
        var x = Math.Round(eastWest, 5, MidpointRounding.AwayFromZero);
        var y = Math.Round(northSouth, 5, MidpointRounding.AwayFromZero);
        if (Math.Abs(x - ReferenceX) >= 1e-4 || Math.Abs(y - ReferenceY) >= 1e-4)
        {
            var distanceM = Math.Sqrt(Math.Pow(x - ReferenceX, 2) + Math.Pow(y - ReferenceY, 2));
            if (distanceM > ExistingSharedCoordinateWarningDistanceM)
                _dialog.Warn("공유좌표 거리 경고",
                    $"입력 기준점과 현재 프로젝트 기준점의 거리가 {distanceM / 1000:0.00} km입니다(경고 거리 10.00 km 초과).\n\n"
                    + $"프로젝트 기준점({x:0.00000}, {y:0.00000})은 변경하지 않고 이 좌표를 기준으로 배치합니다.\n"
                    + "Revit 정밀도 또는 DirectShape 생성 오류가 발생할 수 있습니다.");
            else
                _dialog.Info("공유좌표 확인",
                    $"이 문서에는 이미 공유좌표가 설정되어 있습니다(프로젝트 기준점 = {x:0.00000}, {y:0.00000}).\n"
                    + $"화면의 기준점({ReferenceX:0.00000}, {ReferenceY:0.00000}) 대신 이 좌표를 기준으로 배치해 기존 모델과 위치를 맞춥니다.\n\n"
                    + "화면 기준점으로 배치하려면 [프로젝트 공유좌표에도 기준점 적용]을 체크하세요(문서의 공유좌표가 새 기준점으로 덮어써집니다).");
        }
        return (x, y);
    }
    /// <summary>프로젝트 저장본을 읽는다. 저장본이 없으면 내장 기본값을 이 프로젝트에 바로 저장해 두어(docs/213 § A)
    /// 창을 열 때마다 "저장된 값이 없어…" 안내가 반복되지 않게 한다. 저장이 실패하면 안내만 남기고 창은 연다.</summary>
    private void LoadSettings()
    {
        var (settings, source) = _revit.Run(() => _settingsProvider.Load());
        _settings = settings;
        SyncBendConnectionOption();
        SettingsNotice = string.Empty;
        if (source == BendSettingsSource.BuiltInDefault)
        {
            // settings는 이미 WithSteelDefaults()가 적용된 값이라 그대로 저장한다. 생성자에서 호출되므로 예외를 밖으로 내보내면 창이 안 뜬다.
            try { _revit.Run(() => _save.Execute(settings)); }
            catch (Exception ex) { SettingsNotice = $"저장값 없음 → 내장 기본값 (자동 저장 실패: {UiErrors.Report("PipeAlignment", ex)})"; }
        }
        // 기본값은 핸드북 규격으로 교체됐지만, 그 전에 저장한 프로젝트에는 임시값이 그대로 남아 있어 안내가 필요하다.
        if (_settings.Fittings.NeedsRestore)
            SettingsNotice = (SettingsNotice + " 저장된 곡관 치수 구조 불일치(중복 행·플랜지곡관 누락) → [관·곡관 설정]에서 [기본값 복원] 후 저장").Trim();
        UpdateBendParameterOptions();
    }
    private void OpenPipeSpecs() { var vm = new PipeSpecTableViewModel(_settings, _save, _dialog, _fileDialog, _settingsFileStore, _revit, SelectedMaterial); ShowDialog(new PipeSpecTableView(vm)); if (vm.Result is not null) { _settings = vm.Result; SyncBendConnectionOption(); SettingsNotice = string.Empty; RefreshDiameterMappingPreviews(); } }
    private void SyncBendConnectionOption()
    {
        BendConnectionOption = BendConnectionOptions.First(x => x.Value == _settings.ActiveBendConnection);
        BendFormOption = BendFormOptions.First(x => x.Value == _settings.ActiveBendForm);
        // 저장본의 None(구 값)은 기본 F12로 본다.
        SteelFittingClassOption = SteelFittingClassOptions.FirstOrDefault(x => x.Value == _settings.ActiveSteelFittingClass) ?? SteelFittingClassOptions[0];
        // 이 속성은 _settings를 직접 읽으므로 설정을 통째로 갈아끼운 뒤 알려주지 않으면 체크박스가 옛 값을 보여준다.
        OnPropertyChanged(nameof(UseRestraintGland));
    }
    private void ShowDialog(System.Windows.Window dialog) { if (OwnerWindow is { } owner) dialog.Owner = owner; dialog.ShowDialog(); OwnerWindow?.Activate(); }

    public void ApplyProgress(PipeAlignmentProgress progress)
    {
        ProgressPercent = progress.Percent;
        PhaseText = progress.Phase switch
        {
            PipeAlignmentPhase.Loading => "파일 로드 중",
            PipeAlignmentPhase.PlanningBends => "곡관 계획 중",
            PipeAlignmentPhase.PlacingStraights => "직관 배치 중",
            PipeAlignmentPhase.PlacingBends => "곡관 배치 중",
            PipeAlignmentPhase.PlacingReducers => "이경관 배치 중",
            PipeAlignmentPhase.Committing => "모델 저장 중",
            _ => "모델링 중"
        };
        if (_phaseTrackedPhase != progress.Phase)
        {
            _phaseTrackedPhase = progress.Phase;
            _phaseStartCompleted = progress.Completed;
            _phaseStopwatch.Restart();
        }
        ProgressDetail = progress.Total > 0 ? $"{progress.Completed:N0} / {progress.Total:N0}개{EstimateRemainingText(progress)}" : string.Empty;
    }

    /// <summary>
    /// 같은 페이즈 안에서 지금까지의 처리율(경과시간 / 처리개수)로 남은 개수의 소요시간을 추정한다.
    /// 페이즈가 바뀌면(직관→곡관 등) ApplyProgress에서 스톱워치를 리셋하므로, 페이즈별 처리속도 차이(직관 ~150ms/개, 곡관 ~2,200ms/개)가
    /// 서로 섞여 왜곡되지 않는다. 표본이 너무 적을 때(페이즈 진입 직후)는 노이즈가 커서 "계산 중"으로 표시한다.
    /// </summary>
    private string EstimateRemainingText(PipeAlignmentProgress progress)
    {
        var done = progress.Completed - _phaseStartCompleted;
        if (done < MinEtaSampleCount) return " · 계산 중…";
        var remainingCount = progress.Total - progress.Completed;
        if (remainingCount <= 0) return string.Empty;
        var secondsPerItem = _phaseStopwatch.Elapsed.TotalSeconds / done;
        var remaining = TimeSpan.FromSeconds(secondsPerItem * remainingCount);
        return $" · 약 {FormatRemaining(remaining)} 남음";
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalMinutes < 1) return $"{Math.Max(1, (int)remaining.TotalSeconds)}초";
        if (remaining.TotalHours < 1) return $"{(int)remaining.TotalMinutes}분 {remaining.Seconds}초";
        return $"{(int)remaining.TotalHours}시간 {remaining.Minutes}분";
    }

    public void ApplyResult(PipeAlignmentModelingResult result)
    {
        IsAwaitingPreviewConfirm = false;
        ProgressPercent = 100;
        PhaseText = "완료";
        ProgressDetail = string.Empty;
        ResultSummary = result.OutputMode switch
        {
            PipeAlignmentOutputMode.DirectShape => $"직관 {result.CreatedCount:N0}개(DirectShape) / 곡관 {result.BendCount:N0}개 / 경고 {result.Warnings.Count:N0}건",
            PipeAlignmentOutputMode.Adaptive => $"직관 {result.CreatedCount:N0}개 / 곡관 {result.BendCount:N0}개 / 경고 {result.Warnings.Count:N0}건",
            _ => $"배치 {result.CreatedCount:N0}개 / 경고 {result.Warnings.Count:N0}건"
        };
        SetResultWarnings(result.Warnings);
        HasResult = true;
        IsBusy = false;
        OnPropertyChanged(nameof(CanCreateModel));
    }

    public void ApplyPreviewRetained(PipeAlignmentModelingResult result)
    {
        IsAwaitingPreviewConfirm = false;
        ProgressPercent = 100;
        PhaseText = "미리보기 완료 · 최종 생성 취소";
        ProgressDetail = string.Empty;
        ResultSummary = $"선형 미리보기 {result.CreatedCount:N0}개 유지 / 스킵된 0길이 구간 {result.SkippedSegments:N0}개 / 경고 {result.Warnings.Count:N0}건";
        SetResultWarnings(result.Warnings);
        HasResult = true;
        IsBusy = false;
        OnPropertyChanged(nameof(CanCreateModel));
    }

    /// <summary>미리보기 커밋 완료. TaskDialog 없이 창 안에서 확인한다.</summary>
    public void ApplyPreviewReady(PipeAlignmentModelingResult result)
    {
        ProgressPercent = 100;
        PhaseText = "선형 미리보기 확인 대기";
        ProgressDetail = string.Empty;
        PreviewConfirmMessage = $"선형 미리보기 {result.CreatedCount:N0}개 생성 · Revit에서 위치·방향 확인 후 [계속 생성], 수정은 [미리보기 유지·중단]";
        SetResultWarnings(result.Warnings);
        _pendingPreview = result;
        IsAwaitingPreviewConfirm = true;
        IsBusy = false;
    }

    private void ConfirmPreview()
    {
        IsAwaitingPreviewConfirm = false;
        _pendingPreview = null;
        PhaseText = "최종 모델 생성 중";
        ProgressPercent = 0;
        ProgressDetail = string.Empty;
        IsBusy = true;
        try
        {
            if (FinalizeModelAction is null) throw new InvalidOperationException("최종 생성 요청이 연결되지 않았습니다.");
            FinalizeModelAction();
        }
        catch (Exception ex) { ApplyFailure(UiErrors.Report("PipeAlignment", ex)); }
    }

    private void CancelPreview()
    {
        IsAwaitingPreviewConfirm = false;
        if (_pendingPreview is { } preview) ApplyPreviewRetained(preview);
        _pendingPreview = null;
    }

    public void ApplyFailure(string error)
    {
        IsAwaitingPreviewConfirm = false;
        // 취소(PA-304)는 실패가 아니다 — 롤백돼 변경이 없다는 것만 알린다(docs/236 § B).
        var cancelled = error.StartsWith($"[{DHBIMWATER.Shared.Errors.ErrorCodes.PA_Cancelled}]", StringComparison.Ordinal);
        PhaseText = cancelled ? "취소됨" : "실패";
        ProgressDetail = string.Empty;
        ResultSummary = cancelled
            ? "취소됨 — 변경 없음"
            : error.Contains("내부원점으로부터 너무 멀리 떨어진 곳에 모델링을 시도했습니다.", StringComparison.Ordinal)
            ? "내부원점에서 너무 멀어 생성할 수 없습니다."
            : "모델 생성에 실패했습니다.";
        SetResultWarnings([error]);
        HasResult = true;
        IsBusy = false;
        OnPropertyChanged(nameof(CanCreateModel));
    }

    private void SetResultWarnings(IEnumerable<string> warnings)
    {
        ResultWarnings.Clear();
        foreach (var warning in warnings) ResultWarnings.Add(warning);
        ResultWarningsText = string.Join(Environment.NewLine, ResultWarnings);
    }
}
