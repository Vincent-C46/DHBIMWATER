using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.Documentation;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;

namespace DHBIMWATER.UI.ViewModels.Documentation;

public sealed class JunctionDetailViewModel : ViewModelBase
{
    private readonly BuildJunctionDetailPreviewUseCase _previewUseCase;
    private readonly RunJunctionDetailUseCase _runUseCase;
    private readonly BackfillPipeMaterialParametersUseCase _backfillUseCase;
    private JunctionDetailPreview _preview;
    private bool _createSchedule = true, _createDrafting = true, _createPlan = true;
    private bool _expandVertical = true, _placeTags = true, _useTypeImage = true;
    // 재실행·재조회해도 세트 설정 유지 (SetId = 세트 첫 부재 ElementId)
    private readonly Dictionary<long, JunctionSetOffset> _setState = [];
    private bool _bulkAxisX;
    private string _bulkStepText = JunctionOffsetSetViewModel.Format(JunctionDetailParameters.StepMm);
    private int _viewScale = JunctionDetailParameters.ViewScale;
    private JunctionDetailTagType? _selectedTagType;
    private JunctionTagSide _tagSide = JunctionTagSide.Auto;
    private string _activeTab = "Plan";
    private bool _isRunning;
    private string _resultText = string.Empty;
    private string _statusText = string.Empty;

    public JunctionDetailViewModel(BuildJunctionDetailPreviewUseCase previewUseCase,
        RunJunctionDetailUseCase runUseCase, BackfillPipeMaterialParametersUseCase backfillUseCase)
    {
        _previewUseCase = previewUseCase;
        _runUseCase = runUseCase;
        _backfillUseCase = backfillUseCase;
        _preview = previewUseCase.Execute();
        TagTypes = new ObservableCollection<JunctionDetailTagType>(_preview.TagTypes);
        _selectedTagType = TagTypes.Count == 1 ? TagTypes[0] : null;
        // 값 빈 부재 수 = 실행 시 자동 기록 대상. 문서 전체 재산정 체크는 제거(docs/291)
        UntaggedCount = backfillUseCase.ScanCandidates().CandidateCount;
        RunCommand = new RelayCommand(_ => Run(), _ => CanRun && !IsRunning);
        CancelCommand = new RelayCommand(_ => CloseAction?.Invoke());
        ApplyAllSetsCommand = new RelayCommand(_ => ApplyAllSets(), _ => OffsetSets.Count > 0);
        SyncOffsetSets();
        RefreshPreview();
        RefreshSummary();
    }

    public Action? CloseAction { get; set; }
    /// <summary>모델리스 창(docs/295). Revit API 컨텍스트에서 action 실행 요청. false = Revit이 거부.
    /// null이면 인라인 실행(테스트·모달 경로).</summary>
    public Func<Action, bool>? RunInRevitContext { get; set; }
    /// <summary>창 연 문서가 지금 활성인지 확인. 아니면 던짐 → 결과창에 표시(docs/295 § B-3). null이면 확인 안 함.</summary>
    public Action? EnsureDocument { get; set; }
    /// <summary>세트 부재 Revit 선택·확대 요청(docs/295 § C). null이면 아무것도 안 함.</summary>
    public Action<IReadOnlyList<long>>? SelectInRevit { get; set; }
    /// <summary>새 세트 선택 시 캔버스 맞춤 요청. 인자 = 세트 범위(캔버스 좌표). View가 확대·이동(docs/295 후속)</summary>
    public event Action<Rect>? SetFocusRequested;
    public RelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ApplyAllSetsCommand { get; }
    public ObservableCollection<JunctionOffsetSetViewModel> OffsetSets { get; } = [];
    public ObservableCollection<JunctionPreviewDimensionItem> PreviewDimensions { get; } = [];
    public ObservableCollection<string> PreviewWarnings { get; } = [];
    public ObservableCollection<JunctionDetailTagType> TagTypes { get; }
    public ObservableCollection<JunctionPreviewCanvasItem> PreviewPoints { get; } = [];
    public ObservableCollection<JunctionDetailTableRow> TableRows { get; } = [];
    public ObservableCollection<JunctionDetailOffsetRow> OffsetRows { get; } = [];
    public ObservableCollection<string> ResultLines { get; } = [];
    public IReadOnlyList<JunctionDetailPreviewJunction> Junctions => _preview.Junctions;
    public IReadOnlyList<JunctionSymbolShapeDiagnostic> ShapeDiagnostics => _preview.ShapeDiagnostics ?? [];
    public string ShapeDiagnosticSummary
    {
        get
        {
            var items = _preview.Junctions.SelectMany(x => x.Items).ToList();
            var shapes = items.Count(x => x.Shape is { IsRenderable: true });
            var responsive = ShapeDiagnostics.Count(x => x.OffsetResponse == "반응함");
            return $"기호선을 읽은 부재 {shapes}/{items.Count}개 · 오프셋 반응 {responsive}개";
        }
    }
    public IReadOnlyList<string> ShapeDiagnosticLines => _preview.Junctions.SelectMany(x => x.Items)
        .Select(item =>
        {
            var diagnostic = ShapeDiagnostics.FirstOrDefault(x => x.ElementId == item.ElementId);
            // 패밀리 로컬 Z가 수평 = 눕혀 그린 패밀리를 세워 배치. 평면 기호선이 평면도에 안 보일 수 있음
            var rotated = item.IsVertical && Math.Abs(item.BasisZ.Z) < JunctionVerticalChain.VerticalCosine ? " · 회전 배치(패밀리 로컬 Z 수평) — 패밀리 평면 기호선이 안 보일 수 있음" : "";
            if (item.Shape is not { IsRenderable: true })
                return $"{item.ItemNumber} · {item.Name} · 개략 도형 사용 · {diagnostic?.Note ?? "기호선 정보 없음"}{rotated}";
            return $"{item.ItemNumber} · {item.Name}{rotated} · 기호선 {diagnostic?.CurveCount ?? 0}개 · 점 {diagnostic?.PointCount ?? 0}개 · 크기 {diagnostic?.WidthMm ?? 0:0.#}×{diagnostic?.HeightMm ?? 0:0.#} mm · 오프셋 {diagnostic?.OffsetResponse ?? "확인 안 함"} · {diagnostic?.Note ?? ""}";
        }).ToList();
    // 복사용 한 덩어리(요약 + 줄). 텍스트박스 표시 → 사용자 선택·복사
    public string ShapeDiagnosticText
        => string.Join(Environment.NewLine, ShapeDiagnosticLines.Prepend(ShapeDiagnosticSummary));
    public JunctionDetailPreviewJunction? FirstJunction => _preview.Junctions.FirstOrDefault();
    /// <summary>값 빈 배관 부재 수(수동 배치 밸브 등). 실행 시 자동 기록 대상.</summary>
    public int UntaggedCount { get; private set; }
    public bool CanPlaceTags => TagTypes.Count > 0;
    public bool IsRunning { get => _isRunning; private set { if (SetProperty(ref _isRunning, value)) RaiseCommands(); } }
    public string ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }
    public string ActiveTab { get => _activeTab; set => SetProperty(ref _activeTab, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    // 태그 후보 = 다중 카테고리·배관 이음쇠·배관 부속류 태그(RevitJunctionDetailRepo.GetTagTypes)
    public string TagTypeMessage => TagTypes.Count == 0
        ? "번호 태그 유형이 없습니다. 다중 카테고리·배관 이음쇠·배관 부속류 태그 패밀리를 로드하면 번호 원을 붙일 수 있습니다."
        : "범주 태그는 같은 범주 부재에만 붙습니다. 고른 유형이 안 맞는 부재는 맞는 유형이 하나뿐일 때 그 유형을 씁니다.";
    private double _previewCanvasWidth = MinCanvasWidth, _previewCanvasHeight = MinCanvasHeight;
    private Geometry? _previewCenterGeometry, _previewHighlightCenterGeometry;
    // 세트 선택(docs/290). 간격 바꿔 미리보기 다시 그려도 유지
    private long _selectedSetId;
    // 캔버스 좌표 중심선. 선택 바뀔 때 일반/강조 Geometry 다시 나눔
    private List<(long SetId, double X1, double Y1, double X2, double Y2)> _centerSegments = [];
    /// <summary>캔버스 크기 = 그린 내용 범위 + 여백. Viewbox가 창에 맞춰 줄인다.</summary>
    public double PreviewCanvasWidth { get => _previewCanvasWidth; private set => SetProperty(ref _previewCanvasWidth, value); }
    public double PreviewCanvasHeight { get => _previewCanvasHeight; private set => SetProperty(ref _previewCanvasHeight, value); }
    /// <summary>주관(수평 부재 사이) + 세트(뿌리→마지막 부재) 중심선.</summary>
    public Geometry? PreviewCenterGeometry { get => _previewCenterGeometry; private set => SetProperty(ref _previewCenterGeometry, value); }
    /// <summary>선택 세트 중심선만. 선택 없으면 null.</summary>
    public Geometry? PreviewHighlightCenterGeometry { get => _previewHighlightCenterGeometry; private set => SetProperty(ref _previewHighlightCenterGeometry, value); }
    // 밸브실 벽 외곽선·안쪽 치수 문자(docs/293). 문자는 벽 바깥 우측 상단, Canvas.Right/Bottom 기준
    private Geometry? _previewWallGeometry;
    private string _roomLabel = string.Empty;
    private double _roomLabelRight, _roomLabelBottom;
    public Geometry? PreviewWallGeometry { get => _previewWallGeometry; private set => SetProperty(ref _previewWallGeometry, value); }
    public string RoomLabel { get => _roomLabel; private set => SetProperty(ref _roomLabel, value); }
    public double RoomLabelRight { get => _roomLabelRight; private set => SetProperty(ref _roomLabelRight, value); }
    public double RoomLabelBottom { get => _roomLabelBottom; private set => SetProperty(ref _roomLabelBottom, value); }
    /// <summary>선택 세트 Id. 0 = 선택 없음.</summary>
    public long SelectedSetId => _selectedSetId;
    public bool HasSelectedSet => _selectedSetId != 0;
    // 세트 선택 중이면 일반 중심선 흐리게
    public bool IsPreviewDimmed => HasSelectedSet;
    public bool HasOffsetSets => OffsetSets.Count > 0;
    public bool BulkAxisX { get => _bulkAxisX; set { if (SetProperty(ref _bulkAxisX, value)) OnPropertyChanged(nameof(BulkAxisY)); } }
    public bool BulkAxisY { get => !_bulkAxisX; set => BulkAxisX = !value; }
    public string BulkStepText { get => _bulkStepText; set => SetProperty(ref _bulkStepText, value); }
    public string SummaryLine { get; private set; } = string.Empty;
    /// <summary>요약 줄 툴팁 = 줄인 단어의 긴 설명. 없으면 null → 빈 툴팁 안 뜸</summary>
    public string? SummaryToolTip { get; private set; }
    public string HeaderSummary => FirstJunction is null
        ? "격점 없음 · 평면 상세도 대상 부재 0개 · 수직분기 세트 0개"
        : $"격점 {FirstJunction.JunctionId} · {FirstJunction.RoomType} · 배관 부재 {FirstJunction.PartCount}개 · 수직분기 세트 {OffsetSets.Count}개(부재 {FirstJunction.Items.Count(x => x.Order is not null)}개)"
          + (_preview.Junctions.Count > 1 ? $" · 외 {_preview.Junctions.Count - 1}개" : "")
          + (FirstJunction.Warnings.Count > 0 ? " · 경고: " + string.Join("; ", FirstJunction.Warnings) : "");
    public int TotalPartCount => _preview.MaterialPartCount;
    public bool HasJunctionTargets => _preview.Junctions.Any(x => x.PartCount > 0);
    public bool CanRun => (CreateSchedule || CreateDrafting || CreatePlan)
        && (!(CreateSchedule || CreateDrafting) || TotalPartCount > 0 || UntaggedCount > 0)
        && (!CreatePlan || HasJunctionTargets);

    public bool CreateSchedule { get => _createSchedule; set { if (SetProperty(ref _createSchedule, value)) RefreshSummary(); } }
    public bool CreateDrafting { get => _createDrafting; set { if (SetProperty(ref _createDrafting, value)) RefreshSummary(); } }
    public bool CreatePlan { get => _createPlan; set { if (SetProperty(ref _createPlan, value)) RefreshSummary(); } }
    public bool ExpandVertical { get => _expandVertical; set { if (SetProperty(ref _expandVertical, value)) { RefreshPreview(); RefreshSummary(); } } }
    public int ViewScale
    {
        get => _viewScale;
        set
        {
            if (!SetProperty(ref _viewScale, value)) return;
            foreach (var set in OffsetSets) set.RefreshScale();
            RefreshPreview();
        }
    }
    // 태그 유형 없으면 체크 해제로 보이고 실행에도 false(docs/291). 종전엔 체크된 채 비활성이라 안 바뀌는 것처럼 보였음
    public bool PlaceTags { get => _placeTags && CanPlaceTags; set { if (CanPlaceTags && SetProperty(ref _placeTags, value)) RefreshSummary(); } }
    public bool UseTypeImage { get => _useTypeImage; set { if (SetProperty(ref _useTypeImage, value)) RefreshTableRows(); } }
    public JunctionDetailTagType? SelectedTagType { get => _selectedTagType; set => SetProperty(ref _selectedTagType, value); }
    /// <summary>번호 태그 방향(docs/296). 바꾸면 미리보기 다시 배치</summary>
    public JunctionTagSide TagSide { get => _tagSide; set { if (SetProperty(ref _tagSide, value)) RefreshPreview(); } }
    public IReadOnlyList<KeyValuePair<JunctionTagSide, string>> TagSideOptions { get; } =
    [
        new(JunctionTagSide.Auto, "자동"), new(JunctionTagSide.Right, "우측"), new(JunctionTagSide.Top, "상단"),
        new(JunctionTagSide.Left, "좌측"), new(JunctionTagSide.Bottom, "하단"),
    ];

    // 캔버스 배율: 도면 1mm = 8px. 여백·최소 크기는 px
    private const double PixelsPerPaperMm = 8, CanvasMargin = 40, MinCanvasWidth = 400, MinCanvasHeight = 260;
    private const double TagRadiusPaperMm = JunctionPreviewLayout.TagRadiusPaperMm, DimensionGapPaperMm = 3;
    // 벽 위 문자 간격·높이(도면 mm). 높이는 캔버스 범위 계산용 근사(FontSize 11px ÷ 8px/mm)
    private const double RoomLabelGapPaperMm = 1.5, RoomLabelHeightPaperMm = 1.8;

    private void RefreshPreview()
    {
        PreviewPoints.Clear();
        PreviewDimensions.Clear();
        PreviewWarnings.Clear();
        OffsetRows.Clear();
        TableRows.Clear();
        if (FirstJunction is null)
        {
            foreach (var row in _preview.UnassignedRows) TableRows.Add(ToTableRow(row));
            PreviewCanvasWidth = MinCanvasWidth;
            PreviewCanvasHeight = MinCanvasHeight;
            PreviewCenterGeometry = null;
            PreviewHighlightCenterGeometry = null;
            _centerSegments = [];
            PreviewWallGeometry = null;
            RoomLabel = string.Empty;
            return;
        }

        var sets = OffsetSets.Select(x => x.ToOffset()).ToList();
        var scene = JunctionPreviewLayout.Build(FirstJunction.Items, FirstJunction.Links ?? [], sets, ViewScale, ExpandVertical, TagSide);
        var dimensions = scene.Dimensions.Select(DimensionLine).ToList();
        // 밸브실 벽 사각형(실제 mm → 도면 mm). 문자 자리 = 바깥 우측 상단 위
        var room = FirstJunction.Room;
        var walls = room?.Walls.Select(w => w.Corners.Select(p => (X: p.X / ViewScale, Y: p.Y / ViewScale)).ToList()).ToList() ?? [];
        var labelX = (room?.OuterMaxX ?? 0) / ViewScale;
        var labelY = (room?.OuterMaxY ?? 0) / ViewScale + RoomLabelGapPaperMm;

        // 내용 범위(기호·번호 원·치수) → 캔버스 원점. Y는 위가 +라 뒤집음
        var xs = scene.Placements.SelectMany(p => new[] { p.MinX, p.MaxX, p.TagX - TagRadiusPaperMm, p.TagX + TagRadiusPaperMm })
            .Concat(dimensions.SelectMany(d => new[] { d.X1, d.X2, d.LabelX + 12 }))
            .Concat(walls.SelectMany(w => w.Select(p => p.X))).ToList();
        var ys = scene.Placements.SelectMany(p => new[] { p.MinY, p.MaxY, p.TagY - TagRadiusPaperMm, p.TagY + TagRadiusPaperMm })
            .Concat(dimensions.SelectMany(d => new[] { d.Y1, d.Y2, d.LabelY + 2 }))
            .Concat(walls.SelectMany(w => w.Select(p => p.Y)))
            .Concat(room is null ? [] : new[] { labelY + RoomLabelHeightPaperMm }).ToList();
        var minX = xs.Count > 0 ? xs.Min() : 0;
        var maxY = ys.Count > 0 ? ys.Max() : 0;
        var width = (xs.Count > 0 ? xs.Max() - minX : 0) * PixelsPerPaperMm + CanvasMargin * 2;
        var height = (ys.Count > 0 ? maxY - ys.Min() : 0) * PixelsPerPaperMm + CanvasMargin * 2;
        // 작은 그림은 최소 크기 안에서 가운데
        var padX = Math.Max(0, (MinCanvasWidth - width) / 2);
        var padY = Math.Max(0, (MinCanvasHeight - height) / 2);
        double Cx(double x) => (x - minX) * PixelsPerPaperMm + CanvasMargin + padX;
        double Cy(double y) => (maxY - y) * PixelsPerPaperMm + CanvasMargin + padY;
        PreviewCanvasWidth = Math.Max(MinCanvasWidth, width);
        PreviewCanvasHeight = Math.Max(MinCanvasHeight, height);
        PreviewWallGeometry = BuildSegments(walls.SelectMany(w => w.Select((p, i) =>
            (Cx(p.X), Cy(p.Y), Cx(w[(i + 1) % w.Count].X), Cy(w[(i + 1) % w.Count].Y)))));
        RoomLabel = room?.Label ?? string.Empty;
        RoomLabelRight = PreviewCanvasWidth - Cx(labelX);
        RoomLabelBottom = PreviewCanvasHeight - Cy(labelY);

        foreach (var placement in scene.Placements) PreviewPoints.Add(CreateCanvasItem(placement, Cx, Cy));
        _centerSegments = scene.CenterLines.Select(x => (x.SetId, Cx(x.X1), Cy(x.Y1), Cx(x.X2), Cy(x.Y2))).ToList();
        foreach (var dimension in dimensions)
            PreviewDimensions.Add(new JunctionPreviewDimensionItem(
                BuildSegments(dimension.Lines.Select(x => (Cx(x.X1), Cy(x.Y1), Cx(x.X2), Cy(x.Y2)))),
                Cx(dimension.LabelX), Cy(dimension.LabelY) - 8, dimension.Text, dimension.SetId));
        foreach (var warning in scene.Warnings) PreviewWarnings.Add(warning);

        RefreshTableRows();
        var setNumber = OffsetSets.ToDictionary(x => x.SetId, x => x.Number);
        foreach (var item in FirstJunction.Items.Where(x => x.IsVertical))
        {
            var set = OffsetSets.FirstOrDefault(x => x.SetId == item.SetId)?.ToOffset();
            var order = ExpandVertical && item.Connected && set is not null ? item.Order : null;
            var world = order is { } n ? n * set!.StepMm : 0;
            var direction = JunctionExpandDirection.Select(item.ViewRight, item.ViewUp, set?.Axis ?? JunctionExpandAxis.Y, true);
            var local = SymbolOffsetMath.ToLocal(new SymbolVector(direction.X * world, direction.Y * world,
                direction.Z * world), item.BasisX, item.BasisY, item.BasisZ);
            OffsetRows.Add(new JunctionDetailOffsetRow(item.ItemNumber, item.Name, item.Spec,
                item.Connected && order is { } o ? o.ToString() : "—", world == 0 ? "0" : $"{world:0}",
                $"{item.RotationDegrees:0}°", $"{(Math.Abs(local.X) < 0.5 ? 0 : local.X):0}",
                $"{(Math.Abs(local.Y) < 0.5 ? 0 : local.Y):0}", item.Connected ? "" : "연결 끊김",
                setNumber.TryGetValue(item.SetId, out var number) ? $"세트 {number} · {set!.Axis}" : "—"));
        }
        ApplySetHighlight();
        OnPropertyChanged(nameof(HeaderSummary));
        OnPropertyChanged(nameof(CanRun));
    }

    /// <summary>첫 격점의 세트 목록을 다시 만든다. 같은 SetId면 이전 축·간격 유지.</summary>
    private void SyncOffsetSets()
    {
        foreach (var set in OffsetSets) _setState[set.SetId] = set.ToOffset();
        OffsetSets.Clear();
        var items = FirstJunction?.Items ?? [];
        var setIds = items.Where(x => x.SetId != 0 && x.Order is not null).Select(x => x.SetId).Distinct().ToList();
        for (var i = 0; i < setIds.Count; i++)
        {
            var setId = setIds[i];
            var state = _setState.GetValueOrDefault(setId)
                ?? new JunctionSetOffset(setId, JunctionExpandAxis.Y, JunctionDetailParameters.StepMm);
            var members = string.Join(" → ", items.Where(x => x.SetId == setId).OrderBy(x => x.Order)
                .Select(x => x.ItemNumber > 0 ? $"{x.ItemNumber} {x.Name}" : x.Name));
            OffsetSets.Add(new JunctionOffsetSetViewModel(setId, i + 1, members, state.Axis, state.StepMm,
                () => ViewScale, OnSetChanged) { IsSelected = setId == _selectedSetId });
        }
        // 재조회 후 선택 세트가 사라졌으면 해제
        if (!setIds.Contains(_selectedSetId)) SetSelectedSetId(0);
        OnPropertyChanged(nameof(HasOffsetSets));
        ApplyAllSetsCommand?.RaiseCanExecuteChanged();
    }

    /// <summary>세트 카드 클릭. toggle = 같은 세트 다시 누르면 해제(입력 컨트롤 클릭은 false → 항상 선택).</summary>
    public void SelectSet(JunctionOffsetSetViewModel set, bool toggle)
    {
        var previous = _selectedSetId;
        SetSelectedSetId(toggle && set.SetId == _selectedSetId ? 0 : set.SetId);
        ApplySetHighlight();
        // 새 세트로 바뀔 때만 Revit 선택·확대. 해제·같은 세트 입력칸 클릭은 Revit 안 건드림(docs/295 § C)
        if (_selectedSetId != 0 && _selectedSetId != previous)
        {
            RequestRevitSelection(_selectedSetId);
            if (SelectedSetBounds() is { } bounds) SetFocusRequested?.Invoke(bounds);
        }
    }

    /// <summary>선택 세트 범위(캔버스 좌표). 기호 44px 상자 + 번호 원 + 간격 치수 포함. 없으면 null.</summary>
    public Rect? SelectedSetBounds()
    {
        if (_selectedSetId == 0) return null;
        var bounds = Rect.Empty;
        foreach (var p in PreviewPoints.Where(x => x.Item.SetId == _selectedSetId))
        {
            bounds.Union(new Rect(new Point(p.Left, p.Top), new Point(p.Right, p.Bottom)));
            bounds.Union(new Rect(p.X + p.SymbolLeft, p.Y + p.SymbolTop, 44, 44));
            bounds.Union(new Rect(p.X + p.NumberLeft, p.Y + p.NumberTop, 20, 20));
        }
        foreach (var d in PreviewDimensions.Where(x => x.SetId == _selectedSetId && x.Geometry is not null))
            bounds.Union(d.Geometry!.Bounds);
        return bounds.IsEmpty ? null : bounds;
    }

    private void RequestRevitSelection(long setId)
    {
        if (SelectInRevit is null) return;
        var ids = (FirstJunction?.Items ?? []).Where(x => x.SetId == setId && x.ElementId > 0)
            .Select(x => x.ElementId).Distinct().ToList();
        if (ids.Count > 0) SelectInRevit(ids);
    }

    // 모델리스 창 버튼은 API 컨텍스트 밖 → ExternalEvent로 넘김. 연결 없으면 바로 실행
    private bool InRevit(Action action)
    {
        if (RunInRevitContext is null) { action(); return true; }
        return RunInRevitContext(action);
    }

    private void OnRevitRejected()
    {
        IsRunning = false;
        StatusText = "실패";
        ResultText = "Revit이 요청을 받지 않았습니다. 열려 있는 대화상자나 명령을 끝낸 뒤 다시 시도하세요.";
    }

    /// <summary>Id로 선택(더블클릭 맞춤 시 첫 클릭이 지운 선택 복원용). 목록에 없는 Id면 무시.</summary>
    public void SelectSet(long setId)
    {
        if (OffsetSets.All(x => x.SetId != setId)) return;
        SetSelectedSetId(setId);
        ApplySetHighlight();
    }

    /// <summary>캔버스 빈 곳 클릭 → 선택 해제.</summary>
    public void ClearSetSelection()
    {
        if (_selectedSetId == 0) return;
        SetSelectedSetId(0);
        ApplySetHighlight();
    }

    private void SetSelectedSetId(long setId)
    {
        if (_selectedSetId == setId) return;
        _selectedSetId = setId;
        OnPropertyChanged(nameof(SelectedSetId));
        OnPropertyChanged(nameof(HasSelectedSet));
        OnPropertyChanged(nameof(IsPreviewDimmed));
    }

    // 미리보기 재생성 없이 항목 교체만. 표·오프셋 표 스크롤 유지
    private void ApplySetHighlight()
    {
        foreach (var set in OffsetSets) set.IsSelected = set.SetId == _selectedSetId;
        for (var i = 0; i < PreviewPoints.Count; i++)
        {
            var highlight = HighlightOf(PreviewPoints[i].Item.SetId);
            if (PreviewPoints[i].Highlight != highlight) PreviewPoints[i] = PreviewPoints[i] with { Highlight = highlight };
        }
        for (var i = 0; i < PreviewDimensions.Count; i++)
        {
            var highlight = HighlightOf(PreviewDimensions[i].SetId);
            if (PreviewDimensions[i].Highlight != highlight) PreviewDimensions[i] = PreviewDimensions[i] with { Highlight = highlight };
        }
        PreviewCenterGeometry = BuildSegments(_centerSegments.Where(x => !HasSelectedSet || x.SetId != _selectedSetId)
            .Select(x => (x.X1, x.Y1, x.X2, x.Y2)));
        PreviewHighlightCenterGeometry = HasSelectedSet
            ? BuildSegments(_centerSegments.Where(x => x.SetId == _selectedSetId).Select(x => (x.X1, x.Y1, x.X2, x.Y2)))
            : null;
    }

    // 수평 부재(SetId 0)는 선택 중이면 흐림
    private SetHighlight HighlightOf(long setId)
        => !HasSelectedSet ? SetHighlight.Normal : setId == _selectedSetId ? SetHighlight.Highlighted : SetHighlight.Dimmed;

    private void OnSetChanged()
    {
        RefreshPreview();
        RefreshSummary();
    }

    private void ApplyAllSets()
    {
        if (!JunctionOffsetSetViewModel.TryParse(BulkStepText, out var step)) return;
        var axis = BulkAxisX ? JunctionExpandAxis.X : JunctionExpandAxis.Y;
        foreach (var set in OffsetSets) set.Apply(axis, step);
        OnSetChanged();
    }

    private JunctionPreviewCanvasItem CreateCanvasItem(JunctionPreviewPlacement placement,
        Func<double, double> cx, Func<double, double> cy)
    {
        var item = placement.Item;
        var x = cx(placement.X);
        var y = cy(placement.Y);
        var shape = item.Shape is { IsRenderable: true } found ? found : null;
        // 기호선 없으면 "심볼 없음"을 앞에(빨강 X 상자 툴팁)
        var tip = (shape is null ? "심볼 없음 — " : "")
                  + $"{(item.ItemNumber > 0 ? item.ItemNumber + " · " : "")}{item.Name} {item.Spec}"
                  + (item.Connected ? "" : " — 연결 끊김");
        // 번호 원 20px: 중심이 태그 위치. 지시선 = 끝점 → 원 가장자리(docs/296). 번호 없으면 길이 0 → 안 그림
        var (headX, headY) = (cx(placement.TagX) - x, cy(placement.TagY) - y);
        var (startX, startY) = (cx(placement.LeaderX) - x, cy(placement.LeaderY) - y);
        var length = Math.Sqrt((headX - startX) * (headX - startX) + (headY - startY) * (headY - startY));
        var (endX, endY) = item.ItemNumber > 0 && length > 10
            ? (headX - (headX - startX) / length * 10, headY - (headY - startY) / length * 10)
            : (startX, startY);
        return new JunctionPreviewCanvasItem(item, x, y,
            shape is null ? null : BuildGeometry(shape, PixelsPerPaperMm / ViewScale), shape is not null,
            -22, -22, headX - 10, headY - 10,
            cx(placement.MinX), cx(placement.MaxX), cy(placement.MaxY), cy(placement.MinY), tip,
            LeaderX1: startX, LeaderY1: startY, LeaderX2: endX, LeaderY2: endY);
    }

    private static Geometry BuildGeometry(JunctionSymbolShape shape, double factor)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            foreach (var line in shape.Polylines.Where(x => x.Count >= 2))
            {
                context.BeginFigure(new Point(line[0].X * factor, -line[0].Y * factor), false, false);
                context.PolyLineTo(line.Skip(1).Select(x => new Point(x.X * factor, -x.Y * factor)).ToList(), true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry? BuildSegments(IEnumerable<(double X1, double Y1, double X2, double Y2)> segments)
    {
        var list = segments.ToList();
        if (list.Count == 0) return null;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
            foreach (var (x1, y1, x2, y2) in list)
            {
                context.BeginFigure(new Point(x1, y1), false, false);
                context.LineTo(new Point(x2, y2), true, false);
            }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>세트 순서 1→2 간격 치수. 번호 원 반대편(Y축 = 오른쪽, X축 = 위쪽). 좌표 도면 mm.</summary>
    private DimensionDraft DimensionLine(JunctionPreviewDimension dimension)
    {
        var (a, b) = (dimension.First, dimension.Second);
        var step = Math.Abs(dimension.StepMm);
        var text = $"{JunctionOffsetSetViewModel.Format(step)} mm (도면 {step / ViewScale:0.#})";
        if (dimension.Axis == JunctionExpandAxis.Y)
        {
            var x = Math.Max(a.MaxX, b.MaxX) + DimensionGapPaperMm;
            return new DimensionDraft([(x, a.Y, x, b.Y), (x - 1, a.Y, x + 1, a.Y), (x - 1, b.Y, x + 1, b.Y)],
                x + 1, (a.Y + b.Y) / 2, text, a.Item.SetId);
        }
        var y = Math.Max(a.MaxY, b.MaxY) + DimensionGapPaperMm;
        return new DimensionDraft([(a.X, y, b.X, y), (a.X, y - 1, a.X, y + 1), (b.X, y - 1, b.X, y + 1)],
            Math.Min(a.X, b.X), y + 3, text, a.Item.SetId);
    }

    private sealed record DimensionDraft(IReadOnlyList<(double X1, double Y1, double X2, double Y2)> Lines,
        double LabelX, double LabelY, string Text, long SetId)
    {
        public double X1 => Lines.Min(x => Math.Min(x.X1, x.X2));
        public double X2 => Lines.Max(x => Math.Max(x.X1, x.X2));
        public double Y1 => Lines.Min(x => Math.Min(x.Y1, x.Y2));
        public double Y2 => Lines.Max(x => Math.Max(x.Y1, x.Y2));
    }

    /// <summary>캔버스 부재 하나. X·Y = 부재 원점(px). 나머지 Left/Top은 X·Y 기준 상대 위치, Left~Bottom은 절대 범위.</summary>
    public sealed record JunctionPreviewCanvasItem(JunctionPreviewItem Item, double X, double Y,
        Geometry? ShapeGeometry, bool HasShape, double SymbolLeft, double SymbolTop,
        double NumberLeft, double NumberTop, double Left, double Right, double Top, double Bottom, string ToolTip,
        SetHighlight Highlight = SetHighlight.Normal,
        // 지시선(부재 위치 기준 px). 시작 = 지시선 끝(부재 중심), 끝 = 번호 원 가장자리
        double LeaderX1 = 0, double LeaderY1 = 0, double LeaderX2 = 0, double LeaderY2 = 0);

    public sealed record JunctionPreviewDimensionItem(Geometry? Geometry, double LabelLeft, double LabelTop, string Text,
        long SetId = 0, SetHighlight Highlight = SetHighlight.Normal);

    private void RefreshTableRows()
    {
        TableRows.Clear();
        var rows = FirstJunction?.TableRows ?? _preview.UnassignedRows;
        foreach (var row in rows) TableRows.Add(ToTableRow(row));
    }

    private JunctionDetailTableRow ToTableRow(PipeMaterialRow row)
        => new(row.ItemNumber, row.Name, row.Spec, row.Count, row.Remark,
            UseTypeImage ? row.TypeImagePath : null);

    private void RefreshSummary()
    {
        var outputs = new List<string>();
        if (CreateSchedule) outputs.Add("일람표");
        if (CreateDrafting) outputs.Add("도면");
        if (CreatePlan) outputs.Add("평면");
        // 단어 위주 요약. 0인 항목은 뺌, 긴 설명은 툴팁
        var head = outputs.Count == 0 ? "작성 항목 선택"
            : CanRun ? string.Join(" · ", outputs)
            : CreatePlan && !HasJunctionTargets ? "평면 대상 없음"
            : (CreateSchedule || CreateDrafting) && TotalPartCount == 0 ? "재료표 값 없음"
            : "대상 없음";
        var counts = new List<string>();
        var tips = new List<string>();
        var offsetCount = CanRun && CreatePlan && ExpandVertical
            ? OffsetRows.Count(x => x.WorldUpMm != "0" && x.Status.Length == 0) : 0;
        if (offsetCount > 0)
        {
            counts.Add($"오프셋 {offsetCount}");
            tips.Add($"오프셋 {offsetCount}: 모델에 심볼오프셋 {offsetCount}개 기록");
        }
        if (UntaggedCount > 0)
        {
            counts.Add($"값 기록 {UntaggedCount}");
            tips.Add($"값 기록 {UntaggedCount}: 값 없는 배관 부재 {UntaggedCount}개 자동 기록");
        }
        var orphanCount = CreatePlan ? FirstJunction?.Items.Count(x => !x.Connected) ?? 0 : 0;
        if (orphanCount > 0)
        {
            counts.Add($"미연결 {orphanCount}");
            tips.Add($"미연결 {orphanCount}: 뿌리와 이어지지 않은 부재는 오프셋 없이 처리");
        }
        SummaryLine = counts.Count == 0 ? head : head + " │ " + string.Join(" · ", counts);
        SummaryToolTip = tips.Count == 0 ? null : string.Join(Environment.NewLine, tips);
        OnPropertyChanged(nameof(SummaryLine));
        OnPropertyChanged(nameof(SummaryToolTip));
        OnPropertyChanged(nameof(CanRun));
        RaiseCommands();
    }

    private void Run()
    {
        IsRunning = true;
        ResultLines.Clear();
        if (!InRevit(RunCore)) OnRevitRejected();
    }

    private void RunCore()
    {
        try
        {
            EnsureDocument?.Invoke();
            // Backfill false 고정: 값 빈 부재 자동 기록만(docs/291)
            var options = new JunctionDetailOptions(CreateSchedule, CreateDrafting, CreatePlan, false,
                ExpandVertical, JunctionDetailParameters.StepMm, ViewScale, PlaceTags, SelectedTagType?.Id ?? 0,
                UseTypeImage, JunctionExpandAxis.Y, true,
                OffsetSets.Select(x => x.ToOffset()).ToList(), TagPlacements());
            var result = _runUseCase.Execute(options);
            if (result.Backfill is { } backfill)
            {
                ResultLines.Add($"값 재기록: {backfill.WrittenCount}개 기록, {backfill.SkippedCount}개 건너뜀");
                if (backfill.AdaptiveFilledCount > 0) ResultLines.Add($"적응형 값 기록: {backfill.AdaptiveFilledCount}개");
                if (backfill.RecountedCount > 0) ResultLines.Add($"접합·부설·절단 수 재산정: {backfill.RecountedCount}개");
                foreach (var warning in backfill.Warnings) ResultLines.Add("경고: " + warning);
            }
            AppendResult("일람표", result.Schedule);
            AppendResult("도면", result.Drafting);
            if (result.Plan is { } plan)
            {
                ResultLines.Add($"평면 상세도: 생성 {plan.Created.Count}개, 갱신 {plan.Updated.Count}개, 건너뜀 {plan.Skipped.Count}개");
                foreach (var skipped in plan.Skipped) ResultLines.Add("  건너뜀: " + skipped);
            }
            if (result.Sheet is { } sheet)
            {
                ResultLines.Add($"시트: 생성 {sheet.Created.Count}개, 갱신 {sheet.Updated.Count}개, 그대로 {sheet.Unchanged.Count}개, 건너뜀 {sheet.Skipped.Count}개");
                foreach (var skipped in sheet.Skipped) ResultLines.Add("  건너뜀: " + skipped);
            }
            foreach (var warning in result.Warnings.Concat(result.Plan?.Warnings ?? []).Concat(result.Sheet?.Warnings ?? []))
                ResultLines.Add("경고: " + warning);
            if (ResultLines.Count == 0) ResultLines.Add("요청한 작업 결과가 없습니다.");
            ResultText = string.Join(Environment.NewLine, ResultLines);
            StatusText = "완료";
            ReloadPreview();
        }
        catch (Exception ex) { ResultLines.Add("실행 실패: " + ex.Message); ResultText = ResultLines.Last(); StatusText = "실패"; }
        finally { IsRunning = false; }
    }

    /// <summary>실행 후 미리보기 재구성. 자동 기록된 수동 배치 부재가 재료표 탭에 바로 보이게.</summary>
    private void ReloadPreview()
    {
        try
        {
            _preview = _previewUseCase.Execute();
            UntaggedCount = _backfillUseCase.ScanCandidates().CandidateCount;
        }
        catch (Exception ex) { ResultLines.Add("미리보기 갱신 실패: " + ex.Message); return; }
        SyncOffsetSets();
        RefreshPreview();
        OnPropertyChanged(nameof(UntaggedCount));
        OnPropertyChanged(nameof(Junctions));
        OnPropertyChanged(nameof(FirstJunction));
        OnPropertyChanged(nameof(ShapeDiagnostics));
        OnPropertyChanged(nameof(ShapeDiagnosticSummary));
        OnPropertyChanged(nameof(ShapeDiagnosticLines));
        OnPropertyChanged(nameof(ShapeDiagnosticText));
        RefreshSummary();
    }

    private void AppendResult(string label, PipeMaterialScheduleResult? result)
    {
        if (result is null) return;
        if (label == "일람표" && result.Scan.TotalPartCount == 0)
        {
            ResultLines.Add("재료표 값(DH_Addin)이 기록된 배관 부재가 없습니다.");
            ResultLines.Add("[밸브실 배관]으로 관을 새로 생성하거나, 기존 관·곡관·T형을 선택한 뒤 이 버튼을 다시 눌러 값 재기록을 진행하세요.");
            return;
        }
        ResultLines.Add($"{label}: 생성 {result.Created.Count}개, 갱신 {result.Updated.Count}개, 건너뜀 {result.Skipped.Count}개"
            + (result.Unchanged.Count > 0 ? $", 변경 없음 {result.Unchanged.Count}개" : ""));
        foreach (var skipped in result.Skipped) ResultLines.Add("건너뜀: " + skipped);
    }

    // 실행 시점에 격점 전부 다시 배치 → Revit 태그 위치(docs/296). 첫 격점 = 미리보기와 같은 결과.
    // 세트 설정 없는 세트 = Revit 쪽 options.For 와 같은 기본 축·간격
    private IReadOnlyList<JunctionTagPlacement> TagPlacements()
    {
        var offsets = OffsetSets.Select(x => x.ToOffset()).ToList();
        return _preview.Junctions.SelectMany(junction =>
            {
                var sets = junction.Items.Where(x => x.SetId != 0).Select(x => x.SetId).Distinct()
                    .Select(id => JunctionSetOffsets.For(offsets, id, JunctionExpandAxis.Y, JunctionDetailParameters.StepMm))
                    .ToList();
                return JunctionPreviewLayout.Build(junction.Items, junction.Links ?? [], sets, ViewScale,
                    ExpandVertical, TagSide).Placements;
            })
            .Where(x => x.Item.ItemNumber > 0)
            .Select(x => new JunctionTagPlacement(x.Item.ElementId, x.TagX, x.TagY, x.LeaderX, x.LeaderY)).ToList();
    }

    private void RaiseCommands() => RunCommand?.RaiseCanExecuteChanged();
}
