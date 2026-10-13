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
    private readonly CreateJunctionDetailViewUseCase _detailUseCase;
    private readonly BackfillPipeMaterialParametersUseCase _backfillUseCase;
    private JunctionDetailPreview _preview;
    private bool _createSchedule = true, _createDrafting = true, _createPlan = true;
    private bool _backfill, _expandVertical = true, _placeTags = true, _useTypeImage = true, _redrawExistingDrafting;
    // 재실행·재조회해도 세트 설정 유지 (SetId = 세트 첫 부재 ElementId)
    private readonly Dictionary<long, JunctionSetOffset> _setState = [];
    private bool _bulkAxisX;
    private string _bulkStepText = JunctionOffsetSetViewModel.Format(JunctionDetailParameters.StepPaperMm);
    private int _viewScale = JunctionDetailParameters.ViewScale;
    private JunctionDetailTagType? _selectedTagType;
    private string _activeTab = "Plan";
    private bool _isRunning;
    private string _resultText = string.Empty;
    private bool _fromSelection;
    private string _statusText = string.Empty;

    public JunctionDetailViewModel(BuildJunctionDetailPreviewUseCase previewUseCase,
        RunJunctionDetailUseCase runUseCase, CreateJunctionDetailViewUseCase detailUseCase,
        BackfillPipeMaterialParametersUseCase backfillUseCase)
    {
        _previewUseCase = previewUseCase;
        _runUseCase = runUseCase;
        _detailUseCase = detailUseCase;
        _backfillUseCase = backfillUseCase;
        _preview = previewUseCase.Execute();
        TagTypes = new ObservableCollection<JunctionDetailTagType>(_preview.TagTypes);
        _selectedTagType = TagTypes.Count == 1 ? TagTypes[0] : null;
        var scan = backfillUseCase.ScanCandidates();
        HasBackfillWork = scan.HasWork;
        _fromSelection = scan.FromSelection;
        BackfillCandidateCount = scan.CandidateCount + scan.RecountCount + scan.AdaptiveCandidateCount;
        UntaggedCount = scan.CandidateCount;
        RunCommand = new RelayCommand(_ => Run(), _ => CanRun && !IsRunning);
        ResetOffsetsCommand = new RelayCommand(_ => ResetOffsets(), _ => !IsRunning);
        CancelCommand = new RelayCommand(_ => CloseAction?.Invoke());
        ApplyAllSetsCommand = new RelayCommand(_ => ApplyAllSets(), _ => OffsetSets.Count > 0);
        SyncOffsetSets();
        RefreshPreview();
        RefreshSummary();
    }

    public Action? CloseAction { get; set; }
    public RelayCommand RunCommand { get; }
    public RelayCommand ResetOffsetsCommand { get; }
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
    public JunctionDetailPreviewJunction? FirstJunction => _preview.Junctions.FirstOrDefault();
    public int BackfillCandidateCount { get; }
    /// <summary>값 빈 배관 부재 수(수동 배치 밸브 등). 실행 시 자동 기록 대상.</summary>
    public int UntaggedCount { get; private set; }
    public bool HasBackfillWork { get; }
    public bool FromSelection { get => _fromSelection; private set => SetProperty(ref _fromSelection, value); }
    public bool IsBackfillEnabled => HasBackfillWork;
    public bool CanPlaceTags => TagTypes.Count > 0;
    public bool IsRunning { get => _isRunning; private set { if (SetProperty(ref _isRunning, value)) RaiseCommands(); } }
    public string ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }
    public string ActiveTab { get => _activeTab; set => SetProperty(ref _activeTab, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string TagTypeMessage => TagTypes.Count == 0 ? "태그 유형이 없습니다" : string.Empty;
    public string BackfillMessage => HasBackfillWork ? (FromSelection ? "선택한 요소 기준" : "") : "재기록할 부재가 없습니다";
    private double _previewCanvasWidth = MinCanvasWidth, _previewCanvasHeight = MinCanvasHeight;
    private Geometry? _previewCenterGeometry;
    /// <summary>캔버스 크기 = 그린 내용 범위 + 여백. Viewbox가 창에 맞춰 줄인다.</summary>
    public double PreviewCanvasWidth { get => _previewCanvasWidth; private set => SetProperty(ref _previewCanvasWidth, value); }
    public double PreviewCanvasHeight { get => _previewCanvasHeight; private set => SetProperty(ref _previewCanvasHeight, value); }
    /// <summary>주관(수평 부재 사이) + 세트(뿌리→마지막 부재) 중심선.</summary>
    public Geometry? PreviewCenterGeometry { get => _previewCenterGeometry; private set => SetProperty(ref _previewCenterGeometry, value); }
    public bool HasOffsetSets => OffsetSets.Count > 0;
    public bool BulkAxisX { get => _bulkAxisX; set { if (SetProperty(ref _bulkAxisX, value)) OnPropertyChanged(nameof(BulkAxisY)); } }
    public bool BulkAxisY { get => !_bulkAxisX; set => BulkAxisX = !value; }
    public string BulkStepText { get => _bulkStepText; set => SetProperty(ref _bulkStepText, value); }
    public string SummaryLine { get; private set; } = string.Empty;
    public string HeaderSummary => FirstJunction is null
        ? "격점 없음 · 평면 상세도 대상 부재 0개 · 수직분기 세트 0개"
        : $"격점 {FirstJunction.JunctionId} · {FirstJunction.RoomType} · 배관 부재 {FirstJunction.PartCount}개 · 수직분기 세트 {OffsetSets.Count}개(부재 {FirstJunction.Items.Count(x => x.Order is not null)}개)"
          + (_preview.Junctions.Count > 1 ? $" · 외 {_preview.Junctions.Count - 1}개" : "")
          + (FirstJunction.Warnings.Count > 0 ? " · 경고: " + string.Join("; ", FirstJunction.Warnings) : "");
    public int TotalPartCount => _preview.MaterialPartCount;
    public bool HasJunctionTargets => _preview.Junctions.Any(x => x.PartCount > 0);
    public bool CanRun => (CreateSchedule || CreateDrafting || CreatePlan || Backfill)
        && (!(CreateSchedule || CreateDrafting) || TotalPartCount > 0 || UntaggedCount > 0)
        && (!CreatePlan || HasJunctionTargets)
        && (!Backfill || HasBackfillWork || CreateSchedule || CreateDrafting || CreatePlan);

    public bool CreateSchedule { get => _createSchedule; set { if (SetProperty(ref _createSchedule, value)) RefreshSummary(); } }
    public bool CreateDrafting { get => _createDrafting; set { if (SetProperty(ref _createDrafting, value)) RefreshSummary(); } }
    public bool CreatePlan { get => _createPlan; set { if (SetProperty(ref _createPlan, value)) RefreshSummary(); } }
    public bool Backfill { get => _backfill; set { if (SetProperty(ref _backfill, value)) RefreshSummary(); } }
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
    public bool PlaceTags { get => _placeTags; set { if (CanPlaceTags && SetProperty(ref _placeTags, value)) RefreshSummary(); } }
    public bool UseTypeImage { get => _useTypeImage; set { if (SetProperty(ref _useTypeImage, value)) RefreshTableRows(); } }
    public bool RedrawExistingDrafting { get => _redrawExistingDrafting; set { if (SetProperty(ref _redrawExistingDrafting, value)) RefreshSummary(); } }
    public JunctionDetailTagType? SelectedTagType { get => _selectedTagType; set => SetProperty(ref _selectedTagType, value); }

    // 캔버스 배율: 도면 1mm = 8px. 여백·최소 크기는 px
    private const double PixelsPerPaperMm = 8, CanvasMargin = 40, MinCanvasWidth = 400, MinCanvasHeight = 260;
    private const double TagRadiusPaperMm = JunctionPreviewLayout.TagRadiusPaperMm, DimensionGapPaperMm = 3;

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
            return;
        }

        var sets = OffsetSets.Select(x => x.ToOffset()).ToList();
        var scene = JunctionPreviewLayout.Build(FirstJunction.Items, FirstJunction.Links ?? [], sets, ViewScale, ExpandVertical);
        var dimensions = scene.Dimensions.Select(DimensionLine).ToList();

        // 내용 범위(기호·번호 원·치수) → 캔버스 원점. Y는 위가 +라 뒤집음
        var xs = scene.Placements.SelectMany(p => new[] { p.MinX, p.MaxX, p.TagX - TagRadiusPaperMm, p.TagX + TagRadiusPaperMm })
            .Concat(dimensions.SelectMany(d => new[] { d.X1, d.X2, d.LabelX + 12 })).ToList();
        var ys = scene.Placements.SelectMany(p => new[] { p.MinY, p.MaxY, p.TagY - TagRadiusPaperMm, p.TagY + TagRadiusPaperMm })
            .Concat(dimensions.SelectMany(d => new[] { d.Y1, d.Y2, d.LabelY + 2 })).ToList();
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

        foreach (var placement in scene.Placements) PreviewPoints.Add(CreateCanvasItem(placement, Cx, Cy));
        PreviewCenterGeometry = BuildSegments(scene.CenterLines.Select(x => (Cx(x.X1), Cy(x.Y1), Cx(x.X2), Cy(x.Y2))));
        foreach (var dimension in dimensions)
            PreviewDimensions.Add(new JunctionPreviewDimensionItem(
                BuildSegments(dimension.Lines.Select(x => (Cx(x.X1), Cy(x.Y1), Cx(x.X2), Cy(x.Y2)))),
                Cx(dimension.LabelX), Cy(dimension.LabelY) - 8, dimension.Text));
        foreach (var warning in scene.Warnings) PreviewWarnings.Add(warning);

        RefreshTableRows();
        var setNumber = OffsetSets.ToDictionary(x => x.SetId, x => x.Number);
        foreach (var item in FirstJunction.Items.Where(x => x.IsVertical))
        {
            var set = OffsetSets.FirstOrDefault(x => x.SetId == item.SetId)?.ToOffset();
            var order = ExpandVertical && item.Connected && set is not null ? item.Order : null;
            var world = order is { } n ? n * set!.StepPaperMm * ViewScale : 0;
            var direction = JunctionExpandDirection.Select(item.ViewRight, item.ViewUp, set?.Axis ?? JunctionExpandAxis.Y, true);
            var local = SymbolOffsetMath.ToLocal(new SymbolVector(direction.X * world, direction.Y * world,
                direction.Z * world), item.BasisX, item.BasisY, item.BasisZ);
            OffsetRows.Add(new JunctionDetailOffsetRow(item.ItemNumber, item.Name, item.Spec,
                item.Connected && order is { } o ? o.ToString() : "—", world == 0 ? "0" : $"{world:0}",
                $"{item.RotationDegrees:0}°", $"{(Math.Abs(local.X) < 0.5 ? 0 : local.X):0}",
                $"{(Math.Abs(local.Y) < 0.5 ? 0 : local.Y):0}", item.Connected ? "" : "연결 끊김",
                setNumber.TryGetValue(item.SetId, out var number) ? $"세트 {number} · {set!.Axis}" : "—"));
        }
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
                ?? new JunctionSetOffset(setId, JunctionExpandAxis.Y, JunctionDetailParameters.StepPaperMm);
            var members = string.Join(" → ", items.Where(x => x.SetId == setId).OrderBy(x => x.Order)
                .Select(x => x.ItemNumber > 0 ? $"{x.ItemNumber} {x.Name}" : x.Name));
            OffsetSets.Add(new JunctionOffsetSetViewModel(setId, i + 1, members, state.Axis, state.StepPaperMm,
                () => ViewScale, OnSetChanged));
        }
        OnPropertyChanged(nameof(HasOffsetSets));
        ApplyAllSetsCommand?.RaiseCanExecuteChanged();
    }

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
        var tip = $"{(item.ItemNumber > 0 ? item.ItemNumber + " · " : "")}{item.Name} {item.Spec}"
                  + (shape is null ? " — 기호선 없음(대체 기호)" : "") + (item.Connected ? "" : " — 연결 끊김");
        // 번호 원 20px: 중심이 태그 위치
        return new JunctionPreviewCanvasItem(item, x, y,
            shape is null ? null : BuildGeometry(shape, PixelsPerPaperMm / ViewScale), shape is not null,
            -22, -22, cx(placement.TagX) - x - 10, cy(placement.TagY) - y - 10,
            cx(placement.MinX), cx(placement.MaxX), cy(placement.MaxY), cy(placement.MinY), tip);
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
        var step = Math.Abs(dimension.StepPaperMm);
        var text = $"{JunctionOffsetSetViewModel.Format(step)} mm (실제 {step * ViewScale:0.#})";
        if (dimension.Axis == JunctionExpandAxis.Y)
        {
            var x = Math.Max(a.MaxX, b.MaxX) + DimensionGapPaperMm;
            return new DimensionDraft([(x, a.Y, x, b.Y), (x - 1, a.Y, x + 1, a.Y), (x - 1, b.Y, x + 1, b.Y)],
                x + 1, (a.Y + b.Y) / 2, text);
        }
        var y = Math.Max(a.MaxY, b.MaxY) + DimensionGapPaperMm;
        return new DimensionDraft([(a.X, y, b.X, y), (a.X, y - 1, a.X, y + 1), (b.X, y - 1, b.X, y + 1)],
            Math.Min(a.X, b.X), y + 3, text);
    }

    private sealed record DimensionDraft(IReadOnlyList<(double X1, double Y1, double X2, double Y2)> Lines,
        double LabelX, double LabelY, string Text)
    {
        public double X1 => Lines.Min(x => Math.Min(x.X1, x.X2));
        public double X2 => Lines.Max(x => Math.Max(x.X1, x.X2));
        public double Y1 => Lines.Min(x => Math.Min(x.Y1, x.Y2));
        public double Y2 => Lines.Max(x => Math.Max(x.Y1, x.Y2));
    }

    /// <summary>캔버스 부재 하나. X·Y = 부재 원점(px). 나머지 Left/Top은 X·Y 기준 상대 위치, Left~Bottom은 절대 범위.</summary>
    public sealed record JunctionPreviewCanvasItem(JunctionPreviewItem Item, double X, double Y,
        Geometry? ShapeGeometry, bool HasShape, double SymbolLeft, double SymbolTop,
        double NumberLeft, double NumberTop, double Left, double Right, double Top, double Bottom, string ToolTip);

    public sealed record JunctionPreviewDimensionItem(Geometry? Geometry, double LabelLeft, double LabelTop, string Text);

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
        if (CreatePlan) outputs.Add("평면 상세도");
        SummaryLine = outputs.Count == 0 ? "작성 항목을 하나 이상 체크하세요."
            : CanRun ? "만들 것: " + string.Join(" · ", outputs)
              + (CreatePlan && ExpandVertical ? $" · 모델에 심볼오프셋 {OffsetRows.Count(x => x.WorldUpMm != "0" && x.Status.Length == 0)}개 기록" : "")
            : (CreatePlan && !HasJunctionTargets ? "평면 상세도 대상 배관 이음쇠·밸브류가 없습니다."
                : (CreateSchedule || CreateDrafting) && TotalPartCount == 0 ? "재료표 값이 있는 배관 부재가 없습니다."
                : "값 재기록 대상이 없습니다.");
        if (UntaggedCount > 0) SummaryLine += $" · 값 없는 배관 부재 {UntaggedCount}개 자동 기록";
        if (CreateDrafting && RedrawExistingDrafting) SummaryLine += " · 기존 도면 뷰의 주석·치수가 지워집니다.";
        if (CreatePlan && FirstJunction?.Items.Any(x => !x.Connected) == true)
            SummaryLine += " · 뿌리와 이어지지 않은 부재는 오프셋 없이 처리합니다.";
        OnPropertyChanged(nameof(SummaryLine));
        OnPropertyChanged(nameof(CanRun));
        RaiseCommands();
    }

    private void Run()
    {
        IsRunning = true;
        ResultLines.Clear();
        try
        {
            var options = new JunctionDetailOptions(CreateSchedule, CreateDrafting, CreatePlan, Backfill,
                ExpandVertical, JunctionDetailParameters.StepPaperMm, ViewScale, PlaceTags, SelectedTagType?.Id ?? 0,
                UseTypeImage, RedrawExistingDrafting, JunctionExpandAxis.Y, true,
                OffsetSets.Select(x => x.ToOffset()).ToList());
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
            foreach (var warning in result.Warnings.Concat(result.Plan?.Warnings ?? [])) ResultLines.Add("경고: " + warning);
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
        RefreshSummary();
    }

    private void ResetOffsets()
    {
        try
        {
            var result = _detailUseCase.Execute(resetOnly: true);
            ResultLines.Clear();
            ResultLines.Add($"오프셋 초기화: {result.Reset.Count}개 격점");
            foreach (var warning in result.Warnings) ResultLines.Add(warning);
            ResultText = string.Join(Environment.NewLine, ResultLines);
            StatusText = "초기화 완료";
        }
        catch (Exception ex) { ResultText = "초기화 실패: " + ex.Message; StatusText = "실패"; }
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
        ResultLines.Add($"{label}: 생성 {result.Created.Count}개, 갱신 {result.Updated.Count}개, 건너뜀 {result.Skipped.Count}개");
        foreach (var skipped in result.Skipped) ResultLines.Add("건너뜀: " + skipped);
    }

    private void RaiseCommands() { RunCommand?.RaiseCanExecuteChanged(); ResetOffsetsCommand?.RaiseCanExecuteChanged(); }
}
