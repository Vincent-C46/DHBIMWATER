using System.Collections.ObjectModel;
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
    private readonly JunctionDetailPreview _preview;
    private bool _createSchedule = true, _createDrafting = true, _createPlan = true;
    private bool _backfill, _expandVertical = true, _placeTags = true, _useTypeImage = true, _redrawExistingDrafting;
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
    public ObservableCollection<JunctionPreviewPoint> PreviewPoints { get; } = [];
    public ObservableCollection<JunctionDetailTableRow> TableRows { get; } = [];
    public ObservableCollection<JunctionDetailOffsetRow> OffsetRows { get; } = [];
    public ObservableCollection<string> ResultLines { get; } = [];
    public IReadOnlyList<JunctionDetailPreviewJunction> Junctions => _preview.Junctions;
    public JunctionDetailPreviewJunction? FirstJunction => _preview.Junctions.FirstOrDefault();
    public int BackfillCandidateCount { get; }
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
    public string SummaryLine { get; private set; } = string.Empty;
    public string HeaderSummary => FirstJunction is null
        ? $"격점 없음 · 배관 부재 {TotalPartCount}개 · 수직분기 부재 0개"
        : $"격점 {FirstJunction.JunctionId} · {FirstJunction.RoomType} · 배관 부재 {FirstJunction.PartCount}개 · 수직분기 부재 {FirstJunction.VerticalItems.Count}개"
          + (_preview.Junctions.Count > 1 ? $" · 외 {_preview.Junctions.Count - 1}개" : "")
          + (FirstJunction.Warnings.Count > 0 ? " · 경고: " + string.Join("; ", FirstJunction.Warnings) : "");
    public int TotalPartCount => _preview.Junctions.Sum(x => x.PartCount) + _preview.UnassignedRows.Sum(x => x.Count);
    public bool CanRun => (CreateSchedule || CreateDrafting || CreatePlan || Backfill)
        && (TotalPartCount > 0 || (Backfill && HasBackfillWork));

    public bool CreateSchedule { get => _createSchedule; set { if (SetProperty(ref _createSchedule, value)) RefreshSummary(); } }
    public bool CreateDrafting { get => _createDrafting; set { if (SetProperty(ref _createDrafting, value)) RefreshSummary(); } }
    public bool CreatePlan { get => _createPlan; set { if (SetProperty(ref _createPlan, value)) RefreshSummary(); } }
    public bool Backfill { get => _backfill; set { if (SetProperty(ref _backfill, value)) RefreshSummary(); } }
    public bool ExpandVertical { get => _expandVertical; set { if (SetProperty(ref _expandVertical, value)) { RefreshPreview(); RefreshSummary(); } } }
    public double StepPaperMm { get => _stepPaperMm; set { if (SetProperty(ref _stepPaperMm, Math.Clamp(value, 5, 10))) RefreshPreview(); } }
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
            return;
        }
        foreach (var point in JunctionPreviewLayout.Layout(FirstJunction.VerticalItems, StepPaperMm, ExpandVertical))
            PreviewPoints.Add(point);
        RefreshTableRows();
        foreach (var item in FirstJunction.VerticalItems)
        {
            var order = ExpandVertical ? item.Order : null;
            var world = order is { } n && item.Connected ? n * StepPaperMm * ViewScale : 0;
            var local = SymbolOffsetMath.ToLocal(new SymbolVector(item.WorldUp.X * world, item.WorldUp.Y * world,
                item.WorldUp.Z * world), item.BasisX, item.BasisY, item.BasisZ);
            OffsetRows.Add(new JunctionDetailOffsetRow(item.ItemNumber, item.Name, item.Spec,
                item.Connected && order is { } o ? o.ToString() : "—", world == 0 ? "0" : $"+{world:0}",
                $"{item.RotationDegrees:0}°", $"{local.X:0}", $"{local.Y:0}", item.Connected ? "" : "연결 끊김"));
        }
        OnPropertyChanged(nameof(HeaderSummary));
        OnPropertyChanged(nameof(CanRun));
    }

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
            : "대상 배관 부재가 없습니다.";
        if (CreateDrafting && RedrawExistingDrafting) SummaryLine += " · 기존 도면 뷰의 주석·치수가 지워집니다.";
        if (CreatePlan && FirstJunction?.Warnings.Count > 0) SummaryLine += " · 연결 끊긴 부재는 오프셋 없이 처리합니다.";
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
                UseTypeImage, RedrawExistingDrafting);
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
            if (result.Plan is { } plan) ResultLines.Add($"평면 상세도: 생성 {plan.Created.Count}개, 갱신 {plan.Updated.Count}개, 건너뜀 {plan.Skipped.Count}개");
            foreach (var warning in result.Warnings.Concat(result.Plan?.Skipped ?? []).Concat(result.Plan?.Warnings ?? [])) ResultLines.Add("경고: " + warning);
            if (ResultLines.Count == 0) ResultLines.Add("요청한 작업 결과가 없습니다.");
            ResultText = string.Join(Environment.NewLine, ResultLines);
            StatusText = "완료";
        }
        catch (Exception ex) { ResultLines.Add("실행 실패: " + ex.Message); ResultText = ResultLines.Last(); StatusText = "실패"; }
        finally { IsRunning = false; }
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
