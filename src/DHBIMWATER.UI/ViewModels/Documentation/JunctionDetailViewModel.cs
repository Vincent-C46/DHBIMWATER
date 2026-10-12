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
    private JunctionExpandAxis _expandAxis = JunctionExpandAxis.Y;
    private bool _expandPositive = true;
    private double _stepPaperMm = JunctionDetailParameters.StepPaperMm;
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
        RefreshPreview();
        RefreshSummary();
    }

    public Action? CloseAction { get; set; }
    public RelayCommand RunCommand { get; }
    public RelayCommand ResetOffsetsCommand { get; }
    public RelayCommand CancelCommand { get; }
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
            var items = _preview.Junctions.SelectMany(x => x.VerticalItems).ToList();
            var shapes = items.Count(x => HasRenderableShape(x.Shape));
            var responsive = ShapeDiagnostics.Count(x => x.OffsetResponse == "반응함");
            return $"기호선을 읽은 부재 {shapes}/{items.Count}개 · 오프셋 반응 {responsive}개";
        }
    }
    public IReadOnlyList<string> ShapeDiagnosticLines => _preview.Junctions.SelectMany(x => x.VerticalItems)
        .Select(item =>
        {
            var diagnostic = ShapeDiagnostics.FirstOrDefault(x => x.ElementId == item.ElementId);
            // 패밀리 로컬 Z가 수평 = 눕혀 그린 패밀리를 세워 배치. 평면 기호선이 평면도에 안 보일 수 있음
            var rotated = Math.Abs(item.BasisZ.Z) < JunctionVerticalChain.VerticalCosine ? " · 회전 배치(패밀리 로컬 Z 수평) — 패밀리 평면 기호선이 안 보일 수 있음" : "";
            if (!HasRenderableShape(item.Shape))
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
    private double _previewBoxTop = 285, _previewBoxHeight = 145, _previewStackTop = 337, _previewDimTop, _previewDimBottom;
    private double _previewBoxLeft = 190, _previewBoxWidth = 160, _previewStackRight = 270;
    private double _previewDimLeft = 524, _previewDimRight = 524, _previewDimLabelLeft = 534;
    private string _previewExtentMessage = string.Empty;
    private bool _isPreviewDimVisible;
    public double PreviewBoxTop { get => _previewBoxTop; private set => SetProperty(ref _previewBoxTop, value); }
    public double PreviewBoxHeight { get => _previewBoxHeight; private set => SetProperty(ref _previewBoxHeight, value); }
    public double PreviewBoxLeft { get => _previewBoxLeft; private set => SetProperty(ref _previewBoxLeft, value); }
    public double PreviewBoxWidth { get => _previewBoxWidth; private set => SetProperty(ref _previewBoxWidth, value); }
    public double PreviewStackTop { get => _previewStackTop; private set => SetProperty(ref _previewStackTop, value); }
    public double PreviewStackRight { get => _previewStackRight; private set => SetProperty(ref _previewStackRight, value); }
    /// <summary>첫 두 수직 부재 사이 간격 치수선(시안의 파란 치수). 펼침이 꺼져 있거나 부재가 1개 이하면 숨긴다.</summary>
    public double PreviewDimTop { get => _previewDimTop; private set => SetProperty(ref _previewDimTop, value); }
    public double PreviewDimBottom { get => _previewDimBottom; private set => SetProperty(ref _previewDimBottom, value); }
    public double PreviewDimLeft { get => _previewDimLeft; private set => SetProperty(ref _previewDimLeft, value); }
    public double PreviewDimRight { get => _previewDimRight; private set => SetProperty(ref _previewDimRight, value); }
    public double PreviewDimLabelLeft { get => _previewDimLabelLeft; private set => SetProperty(ref _previewDimLabelLeft, value); }
    public string PreviewExtentMessage { get => _previewExtentMessage; private set => SetProperty(ref _previewExtentMessage, value); }
    public bool IsPreviewX => _expandAxis == JunctionExpandAxis.X;
    public bool IsPreviewDimVisible { get => _isPreviewDimVisible; private set => SetProperty(ref _isPreviewDimVisible, value); }
    public string SummaryLine { get; private set; } = string.Empty;
    public string HeaderSummary => FirstJunction is null
        ? "격점 없음 · 평면 상세도 대상 부재 0개 · 수직분기 부재 0개"
        : $"격점 {FirstJunction.JunctionId} · {FirstJunction.RoomType} · 배관 부재 {FirstJunction.PartCount}개 · 수직분기 부재 {FirstJunction.VerticalItems.Count}개"
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
    public bool ExpandX { get => _expandAxis == JunctionExpandAxis.X; set { if (value) SetExpandAxis(JunctionExpandAxis.X); } }
    public bool ExpandY { get => _expandAxis == JunctionExpandAxis.Y; set { if (value) SetExpandAxis(JunctionExpandAxis.Y); } }
    public bool ExpandPositive { get => _expandPositive; set { if (SetProperty(ref _expandPositive, value)) { OnPropertyChanged(nameof(ExpandNegative)); RefreshPreview(); RefreshSummary(); } } }
    public bool ExpandNegative { get => !_expandPositive; set { if (value) ExpandPositive = false; } }
    private void SetExpandAxis(JunctionExpandAxis axis)
    {
        if (_expandAxis == axis) return;
        _expandAxis = axis;
        OnPropertyChanged(nameof(ExpandX));
        OnPropertyChanged(nameof(ExpandY));
        OnPropertyChanged(nameof(IsPreviewX));
        RefreshPreview();
        RefreshSummary();
    }
    public double StepPaperMm
    {
        get => _stepPaperMm;
        set
        {
            var clamped = Math.Clamp(value, 5, 10);
            if (SetProperty(ref _stepPaperMm, clamped)) { RefreshPreview(); RefreshSummary(); }
            // 텍스트 박스에 범위 밖 값 입력 시 값이 안 바뀌어도 잘린 값으로 되돌려 보임
            else if (clamped != value) OnPropertyChanged();
        }
    }
    public int ViewScale { get => _viewScale; set { if (SetProperty(ref _viewScale, value)) RefreshPreview(); } }
    public bool PlaceTags { get => _placeTags; set { if (CanPlaceTags && SetProperty(ref _placeTags, value)) RefreshSummary(); } }
    public bool UseTypeImage { get => _useTypeImage; set { if (SetProperty(ref _useTypeImage, value)) RefreshTableRows(); } }
    public bool RedrawExistingDrafting { get => _redrawExistingDrafting; set { if (SetProperty(ref _redrawExistingDrafting, value)) RefreshSummary(); } }
    public JunctionDetailTagType? SelectedTagType { get => _selectedTagType; set => SetProperty(ref _selectedTagType, value); }

    private void RefreshPreview()
    {
        PreviewPoints.Clear();
        OffsetRows.Clear();
        TableRows.Clear();
        if (FirstJunction is null)
        {
            foreach (var row in _preview.UnassignedRows) TableRows.Add(ToTableRow(row));
            UpdatePreviewFrame();
            return;
        }
        foreach (var point in JunctionPreviewLayout.Layout(FirstJunction.VerticalItems, StepPaperMm, ExpandVertical,
                     _expandAxis, ExpandPositive))
            PreviewPoints.Add(CreateCanvasItem(point));
        UpdatePreviewFrame();
        RefreshTableRows();
        foreach (var item in FirstJunction.VerticalItems)
        {
            var order = ExpandVertical ? item.Order : null;
            var world = order is { } n && item.Connected ? n * StepPaperMm * ViewScale : 0;
            var direction = JunctionExpandDirection.Select(item.ViewRight, item.ViewUp, _expandAxis, ExpandPositive);
            var local = SymbolOffsetMath.ToLocal(new SymbolVector(direction.X * world, direction.Y * world,
                direction.Z * world), item.BasisX, item.BasisY, item.BasisZ);
            OffsetRows.Add(new JunctionDetailOffsetRow(item.ItemNumber, item.Name, item.Spec,
                item.Connected && order is { } o ? o.ToString() : "—", world == 0 ? "0" : $"{world:0}",
                $"{item.RotationDegrees:0}°", $"{(Math.Abs(local.X) < 0.5 ? 0 : local.X):0}",
                $"{(Math.Abs(local.Y) < 0.5 ? 0 : local.Y):0}", item.Connected ? "" : "연결 끊김"));
        }
        OnPropertyChanged(nameof(HeaderSummary));
        OnPropertyChanged(nameof(CanRun));
    }

    private JunctionPreviewCanvasItem CreateCanvasItem(JunctionPreviewPoint point)
    {
        var shape = HasRenderableShape(point.Item.Shape) ? point.Item.Shape : null;
        if (shape is null)
            return new JunctionPreviewCanvasItem(point.Item, point.X, point.Y, null, false,
                -22, -22, _expandAxis == JunctionExpandAxis.X ? -10 : -52,
                _expandAxis == JunctionExpandAxis.X ? 22 : -10,
                _expandAxis == JunctionExpandAxis.X ? -20 : 34,
                _expandAxis == JunctionExpandAxis.X ? -55 : -15,
                point.X - 22, point.X + 22, point.Y - 22, point.Y + 22);

        var factor = 8d / ViewScale;
        var left = point.X + shape.MinX * factor;
        var right = point.X + shape.MaxX * factor;
        var top = point.Y - shape.MaxY * factor;
        var bottom = point.Y - shape.MinY * factor;
        var middleY = (top + bottom) / 2;
        var middleX = (left + right) / 2;
        var numberLeft = _expandAxis == JunctionExpandAxis.Y ? left - 28 : middleX - 10;
        var numberTop = _expandAxis == JunctionExpandAxis.Y ? middleY - 10 : bottom + 8;
        var labelLeft = _expandAxis == JunctionExpandAxis.Y ? right + 8 : left;
        var labelTop = _expandAxis == JunctionExpandAxis.Y ? middleY - 15 : top - 40;
        return new JunctionPreviewCanvasItem(point.Item, point.X, point.Y, BuildGeometry(shape, factor), true,
            0, 0, numberLeft - point.X, numberTop - point.Y, labelLeft - point.X, labelTop - point.Y,
            left, right, top, bottom);
    }

    private static bool HasRenderableShape(JunctionSymbolShape? shape)
        => shape is { Polylines.Count: > 0 } && shape.MaxX - shape.MinX > 0.01 && shape.MaxY - shape.MinY > 0.01;

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

    /// <summary>수직 사슬 높이에 맞춰 밸브실 점선 박스와 분기 중심선 길이를 정한다.
    /// 좌표는 뷰의 760×450 설계 좌표다(주관 y=370, T관 분기 윗면 y=337, 박스 아래 y=430).</summary>
    private void UpdatePreviewFrame()
    {
        const double branchTopY = 337, boxBottomY = 430, boxDefaultTopY = 285;
        var topY = PreviewPoints.Count > 0 ? PreviewPoints.Min(p => p.Top) : branchTopY;
        var bottomY = PreviewPoints.Count > 0 ? PreviewPoints.Max(p => p.Bottom) : branchTopY;
        var leftX = PreviewPoints.Count > 0 ? PreviewPoints.Min(p => p.Left) : 270;
        var rightX = PreviewPoints.Count > 0 ? PreviewPoints.Max(p => p.Right) : 270;
        PreviewStackTop = _expandAxis == JunctionExpandAxis.Y
            ? (ExpandPositive ? Math.Min(branchTopY, topY - 26) : Math.Max(370, bottomY + 26)) : 370;
        PreviewStackRight = _expandAxis == JunctionExpandAxis.X
            ? (ExpandPositive ? rightX : leftX) : 270;
        PreviewBoxTop = Math.Min(boxDefaultTopY, topY - 44);
        PreviewBoxHeight = Math.Max(boxBottomY, bottomY + 44) - PreviewBoxTop;
        PreviewBoxLeft = Math.Min(190, leftX - 80);
        PreviewBoxWidth = Math.Max(350, rightX + 80) - PreviewBoxLeft;
        PreviewExtentMessage = PreviewBoxLeft < 0 || PreviewBoxLeft + PreviewBoxWidth > 760 ||
            PreviewBoxTop < 0 || PreviewBoxTop + PreviewBoxHeight > 450
            ? "미리보기가 760×450 설계 좌표를 벗어납니다. 간격 또는 방향을 확인하세요." : string.Empty;

        var spread = ExpandVertical && PreviewPoints.Count >= 2 && PreviewPoints[0].Item.Order is not null && PreviewPoints[1].Item.Order is not null;
        IsPreviewDimVisible = spread;
        if (spread)
        {
            PreviewDimTop = _expandAxis == JunctionExpandAxis.Y ? PreviewPoints[1].Y : 412;
            PreviewDimBottom = _expandAxis == JunctionExpandAxis.Y ? PreviewPoints[0].Y : 412;
            PreviewDimLeft = _expandAxis == JunctionExpandAxis.X ? PreviewPoints[0].X : 524;
            PreviewDimRight = _expandAxis == JunctionExpandAxis.X ? PreviewPoints[1].X : 524;
            PreviewDimLabelLeft = _expandAxis == JunctionExpandAxis.X
                ? Math.Min(PreviewDimLeft, PreviewDimRight) + 8 : 534;
        }
    }

    public sealed record JunctionPreviewCanvasItem(JunctionPreviewItem Item, double X, double Y,
        Geometry? ShapeGeometry, bool HasShape, double SymbolLeft, double SymbolTop,
        double NumberLeft, double NumberTop, double LabelLeft, double LabelTop,
        double Left, double Right, double Top, double Bottom);

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
        if (CreatePlan && FirstJunction?.VerticalItems.Any(x => !x.Connected) == true)
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
                ExpandVertical, StepPaperMm, ViewScale, PlaceTags, SelectedTagType?.Id ?? 0,
                UseTypeImage, RedrawExistingDrafting, _expandAxis, ExpandPositive);
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
