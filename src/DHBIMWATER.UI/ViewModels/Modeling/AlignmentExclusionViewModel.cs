using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;

namespace DHBIMWATER.UI.ViewModels.Modeling;

public enum ExclusionRowStatus { Ok, Warning, Error }

/// <summary>제외 구간을 걸 대상 선형(파일·시트 + 레코드). 측점은 선형마다 따로라 구간마다 고른다(docs/219 D1).</summary>
public sealed class AlignmentExclusionTargetOption
{
    public required string FileKey { get; init; }
    public required string FileName { get; init; }
    public required string RecordNumber { get; init; }
    /// <summary>리더가 읽은 원본(반전 전) 선형. 측점 범위는 반전해도 같다.</summary>
    public required PipeAlignment Feature { get; init; }
    public double MinStation { get; init; }
    public double MaxStation { get; init; }
    public bool HasSourceStation { get; init; }
    public string Label => $"{FileName} · 레코드 {RecordNumber} ({StationTextFormatter.Format(MinStation)} ~ {StationTextFormatter.Format(MaxStation)})";
    public override string ToString() => Label;

    public static AlignmentExclusionTargetOption Create(string fileKey, string fileName, PipeAlignment feature)
    {
        var profile = new AlignmentStationProfile(feature);
        return new AlignmentExclusionTargetOption
        {
            FileKey = fileKey, FileName = fileName, RecordNumber = feature.RecordNumber, Feature = feature,
            MinStation = profile.MinStation, MaxStation = profile.MaxStation, HasSourceStation = profile.HasSourceStation
        };
    }
}

/// <summary>배치 제외 구간 1행. 사용자가 고치는 값(이름·대상·시작·끝)이 바뀔 때만 <see cref="Changed"/>를 부른다 — 상태 갱신이 다시 검증을 부르지 않게.</summary>
public sealed class AlignmentExclusionRow : ViewModelBase
{
    private string _name = string.Empty, _startText = string.Empty, _endText = string.Empty;
    private AlignmentExclusionTargetOption? _target;
    private ExclusionRowStatus _status;
    private IReadOnlyList<string> _errors = Array.Empty<string>(), _warnings = Array.Empty<string>();
    private int _index;

    public required int Id { get; init; }
    public Action? Changed { get; set; }
    /// <summary>1부터. 오류 문구·preflight에 쓰는 표시 번호.</summary>
    public int Index { get => _index; set => SetProperty(ref _index, value); }
    public string Name { get => _name; set { if (SetProperty(ref _name, value?.Trim() ?? string.Empty)) { OnPropertyChanged(nameof(DisplayName)); Changed?.Invoke(); } } }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "이름 없음" : Name;
    public AlignmentExclusionTargetOption? Target { get => _target; set { if (SetProperty(ref _target, value)) Changed?.Invoke(); } }
    public string StartText { get => _startText; set { if (SetStationText(ref _startText, value, nameof(StartText))) Changed?.Invoke(); } }
    public string EndText { get => _endText; set { if (SetStationText(ref _endText, value, nameof(EndText))) Changed?.Invoke(); } }
    public double? StartStationM => StationTextFormatter.Parse(StartText);
    public double? EndStationM => StationTextFormatter.Parse(EndText);
    public string LengthText => StartStationM is { } s && EndStationM is { } e && e > s ? (e - s).ToString("0.000") : "—";
    public ExclusionRowStatus Status { get => _status; private set { if (SetProperty(ref _status, value)) OnPropertyChanged(nameof(StatusText)); } }
    public string StatusText => Status switch { ExclusionRowStatus.Error => "오류", ExclusionRowStatus.Warning => "경고", _ => "정상" };
    public IReadOnlyList<string> Errors => _errors;
    public IReadOnlyList<string> Warnings => _warnings;
    /// <summary>상태 셀 한 줄 — 첫 오류, 없으면 첫 경고.</summary>
    public string FirstMessage => _errors.FirstOrDefault() ?? _warnings.FirstOrDefault() ?? string.Empty;
    public string MessagesText => string.Join(Environment.NewLine, _errors.Concat(_warnings));
    public bool HasError => _errors.Count > 0;

    internal void SetValidation(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        _errors = errors; _warnings = warnings;
        Status = errors.Count > 0 ? ExclusionRowStatus.Error : warnings.Count > 0 ? ExclusionRowStatus.Warning : ExclusionRowStatus.Ok;
        OnPropertyChanged(nameof(Errors)); OnPropertyChanged(nameof(Warnings)); OnPropertyChanged(nameof(FirstMessage));
        OnPropertyChanged(nameof(MessagesText)); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(LengthText));
    }

    /// <summary>해석되는 입력은 "0+352.500"으로 다듬어 보인다. 해석 안 되면 입력 그대로 두고 오류로 표시한다.</summary>
    private bool SetStationText(ref string field, string? value, string propertyName)
    {
        var text = value?.Trim() ?? string.Empty;
        if (StationTextFormatter.Parse(text) is { } parsed) text = StationTextFormatter.Format(parsed);
        if (field == text) return false;
        field = text;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(propertyName == nameof(StartText) ? nameof(StartStationM) : nameof(EndStationM));
        OnPropertyChanged(nameof(LengthText));
        return true;
    }
}

public sealed record ExclusionPreviewZone(AlignmentExclusionRow Row, string Label, double StartStation, double EndStation, bool IsError, bool IsFocus);
public sealed record ExclusionPreviewPipe(double StartStation, double EndStation, bool IsCut)
{
    public double Length => Math.Abs(EndStation - StartStation);
}
public sealed record ExclusionPreviewBend(double Station, bool IsSkipped);

/// <summary>미리보기 한 장(선택 행의 대상 선형). 측점 좌표계로 그린다.</summary>
/// <param name="IsDescending">진행 방향으로 측점이 줄어드는 선형(역방향). 절단/재시작 라벨 방향이 바뀐다.</param>
public sealed record ExclusionPreviewModel(double MinStation, double MaxStation, bool IsDescending, double IntervalM,
    IReadOnlyList<ExclusionPreviewZone> Zones, IReadOnlyList<ExclusionPreviewPipe> Pipes, IReadOnlyList<ExclusionPreviewBend> Bends)
{
    public ExclusionPreviewZone? Focus => Zones.FirstOrDefault(x => x.IsFocus);
}

/// <summary>
/// [배치 제외 구간] 탭(docs/219 § D). 서비스 의존이 없어 단위 테스트한다.
/// 미리보기는 실제 모델링과 같은 절단기(<see cref="AlignmentExclusionSplitter"/>)·분절기(<see cref="AlignmentIntervalSampler"/>)로 계산한다.
/// </summary>
public sealed class AlignmentExclusionViewModel : ViewModelBase
{
    /// <summary>경계 절단관이 이보다 짧으면 경고(docs/219 D6).</summary>
    public const double ShortPipeWarningM = 1.0;
    private const double Tolerance = 1e-6;
    private AlignmentExclusionRow? _selectedRow;
    private double _intervalM = 6d;
    private int _nextId;
    private bool _suspendValidation;
    private ExclusionPreviewModel? _preview;
    private string _previewSummary = string.Empty;
    private readonly Dictionary<string, TargetLayout> _layouts = new(StringComparer.Ordinal);
    private HashSet<(string FileKey, string RecordNumber)> _focusTargets = new();
    private bool _showAllRows;

    public AlignmentExclusionViewModel()
    {
        AddCommand = new RelayCommand(_ => Add());
        RemoveCommand = new RelayCommand(items => Remove(items as IList));
        SortCommand = new RelayCommand(_ => Sort(), _ => Rows.Count > 1);
        SelectZoneCommand = new RelayCommand(row => { if (row is AlignmentExclusionRow r) SelectedRow = r; });
    }

    public ObservableCollection<AlignmentExclusionRow> Rows { get; } = new();
    public ObservableCollection<AlignmentExclusionTargetOption> Targets { get; } = new();
    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand SortCommand { get; }
    public ICommand SelectZoneCommand { get; }
    /// <summary>검증 결과가 바뀌면 메인 VM이 preflight를 다시 계산한다.</summary>
    public Action? Changed { get; set; }
    /// <summary>(파일키, 레코드) → [선형 방향] 탭에서 반전했는지. 미리보기 분절 시작 방향에만 쓴다.</summary>
    public Func<string, string, bool>? IsReversed { get; set; }
    /// <summary>[선형 방향]과 병합한 탭 헤더(docs/220 § A).</summary>
    public string HeaderText => Rows.Count == 0 ? "선형·제외 구간" : $"선형·제외 구간 ({Rows.Count})";
    /// <summary>표에 보이는 행 — 개요도·목록에서 고른 선형의 구간만(docs/220 § D). 선택이 없거나 <see cref="ShowAllRows"/>면 전부, 대상 미지정 행은 항상.</summary>
    public ObservableCollection<AlignmentExclusionRow> VisibleRows { get; } = new();
    public bool ShowAllRows { get => _showAllRows; set { if (SetProperty(ref _showAllRows, value)) RefreshVisibleRows(); } }
    /// <summary>[＋]·붙여넣기의 대상 선형 — 메인 VM이 첫 선택 선형을 준다. null이면 종전 규칙(선택 행의 대상 → 첫 대상).</summary>
    public Func<AlignmentExclusionTargetOption?>? PreferredTarget { get; set; }
    public bool HasRows => Rows.Count > 0;
    public bool HasErrors => Rows.Any(x => x.HasError);
    public AlignmentExclusionRow? SelectedRow { get => _selectedRow; set { if (SetProperty(ref _selectedRow, value)) RefreshPreview(); } }
    public double IntervalM { get => _intervalM; set { if (SetProperty(ref _intervalM, value)) Revalidate(); } }
    public ExclusionPreviewModel? Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public string PreviewSummary { get => _previewSummary; private set => SetProperty(ref _previewSummary, value); }
    public string DetailLabel => SelectedRow is not { } row ? "확대 — 구간 선택 필요"
        : $"확대 — {row.DisplayName}";

    /// <summary>입력 파일·레이어가 바뀌면 대상 목록을 다시 만든다. 같은 (파일키, 레코드) 행은 새 항목으로 다시 잇고, 사라진 대상은 비워 오류로 보인다.</summary>
    public void RebuildTargets(IEnumerable<AlignmentExclusionTargetOption> options)
    {
        var list = options.ToList();
        _suspendValidation = true;
        try
        {
            Targets.Clear();
            foreach (var option in list) Targets.Add(option);
            foreach (var row in Rows)
                if (row.Target is { } old)
                    row.Target = list.FirstOrDefault(x => x.FileKey == old.FileKey && x.RecordNumber == old.RecordNumber);
        }
        finally { _suspendValidation = false; }
        Revalidate();
        RefreshVisibleRows();
    }

    /// <summary>개요도·목록에서 고른 선형(docs/220 § D). 표 필터에 쓴다.</summary>
    public void SetFocusTargets(IEnumerable<(string FileKey, string RecordNumber)> keys)
    {
        _focusTargets = keys.ToHashSet();
        RefreshVisibleRows();
    }

    private void RefreshVisibleRows()
    {
        var visible = Rows.Where(row => ShowAllRows || _focusTargets.Count == 0 || row.Target is null
            || _focusTargets.Contains((row.Target.FileKey, row.Target.RecordNumber))).ToList();
        if (visible.SequenceEqual(VisibleRows)) return;
        VisibleRows.Clear();
        foreach (var row in visible) VisibleRows.Add(row);
    }

    public AlignmentExclusionRow Add()
    {
        var target = PreferredTarget?.Invoke() ?? SelectedRow?.Target ?? Targets.FirstOrDefault();
        var row = NewRow(string.Empty, target, null, null);
        if (target is not null)
        {
            var ends = Rows.Where(x => ReferenceEquals(x.Target, target) && x.EndStationM is not null).Select(x => x.EndStationM!.Value).ToList();
            var start = ends.Count > 0 ? ends.Max() + 50d : target.MinStation + 50d;
            if (start + 10d > target.MaxStation) start = Math.Max(target.MinStation, target.MaxStation - 10d);
            var end = Math.Min(start + 10d, target.MaxStation);
            _suspendValidation = true;
            try { row.StartText = StationTextFormatter.Format(start); row.EndText = StationTextFormatter.Format(end); }
            finally { _suspendValidation = false; }
        }
        Rows.Add(row);
        AfterRowsChanged();
        SelectedRow = row;
        return row;
    }

    /// <summary>선택 행 삭제. <paramref name="items"/>가 비면 현재 행만.</summary>
    public void Remove(IList? items)
    {
        var targets = items?.OfType<AlignmentExclusionRow>().ToList() is { Count: > 0 } selected ? selected
            : SelectedRow is { } current ? new List<AlignmentExclusionRow> { current } : new List<AlignmentExclusionRow>();
        if (targets.Count == 0) return;
        foreach (var row in targets) Rows.Remove(row);
        if (SelectedRow is null || !Rows.Contains(SelectedRow)) SelectedRow = Rows.FirstOrDefault();
        AfterRowsChanged();
    }

    public void Sort()
    {
        var sorted = Rows.OrderBy(x => x.Target?.Label ?? "￿", StringComparer.Ordinal).ThenBy(x => x.StartStationM ?? double.MaxValue).ToList();
        var selected = SelectedRow;
        Rows.Clear();
        foreach (var row in sorted) Rows.Add(row);
        SelectedRow = selected;
        AfterRowsChanged();
    }

    /// <summary>엑셀에서 복사한 탭 구분 행. 3열 이상 = 구간명·시작·끝, 2열 = 시작·끝. 대상 선형은 현재 행의 것. 추가한 행 수를 돌려준다.</summary>
    public int Paste(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains('\t')) return 0;
        var target = PreferredTarget?.Invoke() ?? SelectedRow?.Target ?? Targets.FirstOrDefault();
        AlignmentExclusionRow? last = null;
        var count = 0;
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = line.Split('\t').Select(x => x.Trim()).ToArray();
            if (cells.Length < 2) continue;
            var (name, start, end) = cells.Length >= 3 ? (cells[0], cells[1], cells[2]) : (string.Empty, cells[0], cells[1]);
            last = NewRow(name, target, start, end);
            Rows.Add(last);
            count++;
        }
        if (count == 0) return 0;
        AfterRowsChanged();
        SelectedRow = last;
        return count;
    }

    public void SelectById(string? id)
    {
        if (int.TryParse(id, out var value) && Rows.FirstOrDefault(x => x.Id == value) is { } row) SelectedRow = row;
    }

    /// <summary>모델링 요청에 실을 구간(해당 파일, 오류 없는 행만). 오류 행이 있으면 모델 생성 자체가 막힌다.</summary>
    public IReadOnlyList<AlignmentExclusionRange> RangesFor(string fileKey)
        => Rows.Where(x => !x.HasError && x.Target?.FileKey == fileKey && x.StartStationM is not null && x.EndStationM is not null)
            .Select(x => new AlignmentExclusionRange(x.Target!.RecordNumber, x.StartStationM!.Value, x.EndStationM!.Value, string.IsNullOrWhiteSpace(x.Name) ? null : x.Name))
            .ToList();

    /// <summary>행 검증(docs/219 D5·D6) + 미리보기 갱신.</summary>
    public void Revalidate()
    {
        if (_suspendValidation) return;
        for (var i = 0; i < Rows.Count; i++) Rows[i].Index = i + 1;
        var errors = Rows.ToDictionary(x => x, _ => new List<string>());
        var warnings = Rows.ToDictionary(x => x, _ => new List<string>());
        foreach (var row in Rows)
        {
            var e = errors[row];
            if (row.Target is null) e.Add("대상 선형을 고르세요.");
            if (row.StartStationM is null) e.Add("시작 Sta. 형식 오류 (예: 0+352.000 또는 352)");
            if (row.EndStationM is null) e.Add("끝 Sta. 형식 오류 (예: 0+364.500 또는 364.5)");
            if (e.Count > 0) continue;
            var (s, en, t) = (row.StartStationM!.Value, row.EndStationM!.Value, row.Target!);
            if (en <= s + Tolerance) e.Add("끝 Sta.가 시작 Sta.보다 커야 합니다.");
            else if (s < t.MinStation - Tolerance || en > t.MaxStation + Tolerance)
                e.Add($"선형 측점 범위 {StationTextFormatter.Format(t.MinStation)} ~ {StationTextFormatter.Format(t.MaxStation)} 밖입니다.");
        }
        // 같은 선형 구간끼리 겹침 — 앞 검증을 통과한 행끼리만.
        var candidates = Rows.Where(x => errors[x].Count == 0).ToList();
        for (var i = 0; i < candidates.Count; i++)
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var (a, b) = (candidates[i], candidates[j]);
                if (!ReferenceEquals(a.Target, b.Target)) continue;
                if (a.StartStationM < b.EndStationM - Tolerance && b.StartStationM < a.EndStationM - Tolerance)
                {
                    errors[a].Add($"구간 {b.Index}({b.DisplayName})과 겹칩니다.");
                    errors[b].Add($"구간 {a.Index}({a.DisplayName})과 겹칩니다.");
                }
            }

        _layouts.Clear();
        foreach (var row in Rows.Where(x => errors[x].Count == 0))
        {
            var layout = LayoutFor(row.Target!, errors);
            var (s, en) = (row.StartStationM!.Value, row.EndStationM!.Value);
            var inside = layout.Bends.Where(x => x.Station > s + Tolerance && x.Station < en - Tolerance).ToList();
            if (inside.Count > 0)
                warnings[row].Add($"구간 안 곡관 {inside.Count}개 생략 ({string.Join(", ", inside.Take(5).Select(x => StationTextFormatter.Format(x.Station)))}{(inside.Count > 5 ? " …" : string.Empty)})");
            foreach (var pipe in layout.Pipes.Where(p => p.IsCut && p.Length < ShortPipeWarningM - Tolerance))
            {
                var low = Math.Min(pipe.StartStation, pipe.EndStation);
                var high = Math.Max(pipe.StartStation, pipe.EndStation);
                if (Math.Abs(high - s) <= 1e-4) warnings[row].Add($"시작 경계 앞 절단관 {pipe.Length:0.000} m — 너무 짧습니다.");
                else if (Math.Abs(low - en) <= 1e-4) warnings[row].Add($"끝 경계 뒤 절단관 {pipe.Length:0.000} m — 너무 짧습니다.");
            }
        }
        foreach (var row in Rows) row.SetValidation(errors[row], warnings[row]);
        OnPropertyChanged(nameof(HasErrors));
        RefreshPreview();
        Changed?.Invoke();
    }

    private AlignmentExclusionRow NewRow(string name, AlignmentExclusionTargetOption? target, string? start, string? end)
    {
        var row = new AlignmentExclusionRow { Id = ++_nextId };
        row.Name = name;
        row.Target = target;
        row.StartText = start ?? string.Empty;
        row.EndText = end ?? string.Empty;
        row.Changed = Revalidate;
        return row;
    }

    private void AfterRowsChanged()
    {
        OnPropertyChanged(nameof(HeaderText)); OnPropertyChanged(nameof(HasRows));
        RefreshVisibleRows();
        Revalidate();
    }

    private void RefreshPreview()
    {
        OnPropertyChanged(nameof(DetailLabel));
        var target = SelectedRow?.Target ?? Targets.FirstOrDefault(t => Rows.Any(r => ReferenceEquals(r.Target, t)));
        if (target is null) { Preview = null; PreviewSummary = string.Empty; return; }
        var key = LayoutKey(target);
        if (!_layouts.TryGetValue(key, out var layout))
            layout = LayoutFor(target, Rows.ToDictionary(x => x, x => x.Errors.ToList()));
        var zones = Rows.Where(x => ReferenceEquals(x.Target, target) && x.StartStationM is { } s && x.EndStationM is { } e && e > s)
            .Select(x => new ExclusionPreviewZone(x, x.DisplayName, x.StartStationM!.Value, x.EndStationM!.Value, x.HasError, ReferenceEquals(x, SelectedRow)))
            .ToList();
        Preview = new ExclusionPreviewModel(target.MinStation, target.MaxStation, layout.IsDescending, IntervalM, zones, layout.Pipes, layout.Bends);
        var excluded = zones.Where(x => !x.IsError).Sum(x => x.EndStation - x.StartStation);
        PreviewSummary = $"{(target.HasSourceStation ? "엑셀 Station" : "Station 없음 → 0부터 평면 누적")} · 직관 {layout.Pipes.Count:N0}개(절단관 {layout.Pipes.Count(x => x.IsCut):N0}) · 제외 {excluded:0.000} m";
    }

    private static string LayoutKey(AlignmentExclusionTargetOption target) => $"{target.FileKey}\n{target.RecordNumber}";

    /// <summary>대상 선형 1개의 미리보기 배치 — 오류 없는 행만 반영해 자르고, 실제 모델링과 같은 분절 함수로 나눈다.</summary>
    private TargetLayout LayoutFor(AlignmentExclusionTargetOption target, IReadOnlyDictionary<AlignmentExclusionRow, List<string>> errors)
    {
        var key = LayoutKey(target);
        if (_layouts.TryGetValue(key, out var cached)) return cached;
        var feature = IsReversed?.Invoke(target.FileKey, target.RecordNumber) == true ? Reverse(target.Feature) : target.Feature;
        var ranges = Rows.Where(x => ReferenceEquals(x.Target, target) && errors.TryGetValue(x, out var e) && e.Count == 0)
            .Select(x => new AlignmentExclusionRange(target.RecordNumber, x.StartStationM!.Value, x.EndStationM!.Value, x.Name))
            .ToList();
        var profile = new AlignmentStationProfile(feature);
        var split = AlignmentExclusionSplitter.Split(feature, ranges);
        var pipes = new List<ExclusionPreviewPipe>();
        // 하한은 UseCase와 같다(docs/209 § C) — 오타 간격으로 수백만 조각을 만들지 않는다.
        if (IntervalM * 1000d >= DHBIMWATER.Application.UseCases.Gis.ModelPipeAlignmentUseCase.MinIntervalMm)
            foreach (var part in split.Parts)
            {
                var partProfile = new AlignmentStationProfile(part);
                foreach (var segment in AlignmentIntervalSampler.SampleSegments(part, IntervalM))
                {
                    var length = segment.Start.DistanceTo(segment.End);
                    pipes.Add(new ExclusionPreviewPipe(partProfile.StationAt(segment.DistanceFromStart), partProfile.StationAt(segment.DistanceFromStart + length),
                        length < IntervalM - 1e-6));
                }
            }
        var zoneRanges = ranges.Select(x => (Low: Math.Min(x.StartStationM, x.EndStationM), High: Math.Max(x.StartStationM, x.EndStationM))).ToList();
        var bends = BendVertexIndices(feature)
            .Select(v => profile.StationAtVertex(v))
            .Select(station => new ExclusionPreviewBend(station, zoneRanges.Any(z => station > z.Low + Tolerance && station < z.High - Tolerance)))
            .ToList();
        var layout = new TargetLayout(pipes, bends, profile.EndStation < profile.StartStation);
        _layouts[key] = layout;
        return layout;
    }

    /// <summary>곡관 자리로 볼 정점 — 곡관유형이 지정됐거나 방향이 0.5° 넘게 꺾이는 내부 정점(샘플러와 같은 허용각).</summary>
    private static IEnumerable<int> BendVertexIndices(PipeAlignment alignment)
    {
        var vertices = alignment.Vertices;
        for (var v = 1; v < vertices.Count - 1; v++)
        {
            var declared = alignment.VertexBendKinds is { } kinds && v < kinds.Count && !string.IsNullOrWhiteSpace(kinds[v]);
            if (declared || Deflects(vertices[v - 1], vertices[v], vertices[v + 1])) yield return v;
        }
    }

    private static bool Deflects(Point3D previous, Point3D current, Point3D next)
    {
        var a = new Vector3D(current.X - previous.X, current.Y - previous.Y, current.Z - previous.Z);
        var b = new Vector3D(next.X - current.X, next.Y - current.Y, next.Z - current.Z);
        if (a.Length <= 1e-9 || b.Length <= 1e-9) return false;
        var (ax, ay, az) = (a.X / a.Length, a.Y / a.Length, a.Z / a.Length);
        var (bx, by, bz) = (b.X / b.Length, b.Y / b.Length, b.Z / b.Length);
        var dot = ax * bx + ay * by + az * bz;
        var cx = ay * bz - az * by; var cy = az * bx - ax * bz; var cz = ax * by - ay * bx;
        return dot <= 0d || Math.Sqrt(cx * cx + cy * cy + cz * cz) > AlignmentIntervalSampler.StraightSinTolerance;
    }

    /// <summary>Loader의 레코드 반전과 같은 규칙(DN은 구간 시작 값이라 한 칸 밀어 뒤집는다).</summary>
    internal static PipeAlignment Reverse(PipeAlignment feature) => feature with
    {
        Vertices = feature.Vertices.Reverse().ToList(),
        VertexBendKinds = feature.VertexBendKinds?.Reverse().ToList(),
        VertexDiameterMm = ReverseSegmentStartValues(feature.VertexDiameterMm),
        VertexDiameterRawValues = ReverseSegmentStartValues(feature.VertexDiameterRawValues),
        VertexStationTexts = feature.VertexStationTexts?.Reverse().ToList(),
        VertexStationM = feature.VertexStationM?.Reverse().ToList()
    };

    private static IReadOnlyList<T>? ReverseSegmentStartValues<T>(IReadOnlyList<T>? source)
    {
        if (source is null || source.Count == 0) return source;
        var reversed = new List<T>(source.Count);
        for (var index = source.Count - 2; index >= 0; index--) reversed.Add(source[index]);
        reversed.Add(source[0]);
        return reversed;
    }

    private sealed record TargetLayout(IReadOnlyList<ExclusionPreviewPipe> Pipes, IReadOnlyList<ExclusionPreviewBend> Bends, bool IsDescending);
}
