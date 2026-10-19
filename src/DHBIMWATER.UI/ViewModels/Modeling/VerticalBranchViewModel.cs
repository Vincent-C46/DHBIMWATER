using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>입면 창 부품 행 1개(docs/301 § D). 단관만 길이 편집.</summary>
public sealed class VerticalBranchPartItem : ViewModelBase
{
    private double _lengthMm;

    public VerticalBranchPartItem(string familyTypeName, double lengthMm = 0)
    {
        FamilyTypeName = familyTypeName;
        _lengthMm = lengthMm;
    }

    /// <summary>"패밀리 : 유형". 빈 문자열 = 단관.</summary>
    public string FamilyTypeName { get; }
    public bool IsPipe => string.IsNullOrWhiteSpace(FamilyTypeName);
    public string DisplayName => IsPipe ? VerticalBranchViewModel.PipeOption : FamilyTypeName;
    public double LengthMm
    {
        get => _lengthMm;
        set { if (SetProperty(ref _lengthMm, value)) Changed?.Invoke(); }
    }
    /// <summary>길이 바뀜 → 창 VM이 검증·미리보기 다시 계산.</summary>
    internal Action? Changed { get; set; }
    public VerticalBranchPart ToPart() => new(FamilyTypeName, IsPipe ? LengthMm : 0);
}

/// <summary>입면 미리보기 도형 1개. 위치는 캔버스 px.</summary>
public sealed record VerticalBranchPreviewItem(double Top, double Height, bool IsPipe, string Label, string LengthText)
{
    /// <summary>도형 안 라벨 위치(도형 기준). 짧은 단관이면 위 끝에 붙임.</summary>
    public double LabelTopInItem => Math.Max(Height / 2 - 14, 0);
}

/// <summary>수직분기 입면 창(docs/301 § D). 시작 커넥터에서 +Z로 부품을 순서대로 쌓는다 — 끝 높이 입력 없음.</summary>
public sealed class VerticalBranchViewModel : ViewModelBase
{
    public const string PipeOption = "단관";
    /// <summary>새 단관 기본 길이(mm).</summary>
    public const double DefaultPipeLengthMm = 300;
    /// <summary>미리보기 캔버스 높이(px). 시작 부속 상자 위로 이만큼 그린다.</summary>
    public const double PreviewHeight = 380;
    /// <summary>밸브 기호 높이(px). 실폭은 배치 때 재므로 기호 크기 고정.</summary>
    private const double ValveSymbolHeight = 36;
    private const double MinPipeHeight = 6;

    private readonly IReadOnlyList<string> _valveNames;
    private readonly HashSet<string> _familiesWithDiameterMatch;
    private bool _showAllDiameters;
    private string? _selectedAddOption;
    private string _errorText = "";
    private string _summaryText = "";

    public VerticalBranchViewModel(VerticalBranchStart start, IEnumerable<string> valveFamilyTypeNames)
    {
        Start = start;
        _valveNames = valveFamilyTypeNames.Distinct().OrderBy(x => x, NominalDiameterParser.FamilyGroupedComparer).ToList();
        // 관경 맞는 유형 있는 패밀리만 거름. 없는 패밀리(공기밸브 등 — 유형명 DN이 본관과 다르거나 없음)는 전 유형 표시.
        _familiesWithDiameterMatch = _valveNames.Where(MatchesDiameter).Select(FamilyNameOf).ToHashSet(StringComparer.Ordinal);
        // 관경 맞는 유형이 하나도 없으면 거를 의미 없음 — 전체 표시.
        _showAllDiameters = _familiesWithDiameterMatch.Count == 0;
        Parts = [];
        Parts.CollectionChanged += (_, _) => { RefreshPartsTopDown(); Refresh(); };
        AddOptions = [];
        RefreshAddOptions();
        AddCommand = new RelayCommand(_ => Add(), _ => !string.IsNullOrWhiteSpace(SelectedAddOption));
        RemoveCommand = new RelayCommand(x => { if (x is VerticalBranchPartItem item) Parts.Remove(item); });
        MoveTowardStartCommand = new RelayCommand(x => Move(x as VerticalBranchPartItem, -1));
        MoveTowardEndCommand = new RelayCommand(x => Move(x as VerticalBranchPartItem, 1));
        PlaceCommand = new RelayCommand(_ => Close(true), _ => CanPlace);
        CancelCommand = new RelayCommand(_ => Close(false));
        AddPart(new VerticalBranchPartItem("", DefaultPipeLengthMm));
    }

    public VerticalBranchStart Start { get; }
    public string StartText => $"{Start.FamilyTypeName} (ID {Start.ElementId})";
    public string StartConnectorText => FormattableString.Invariant($"Ø{Start.DiameterMm:0.#} · Z {Start.ElevationMm:N0}mm");
    public ObservableCollection<VerticalBranchPartItem> Parts { get; }
    /// <summary>목록 표시용 — 위 = 끝, 아래 = 시작(입면과 같은 방향). 순서 원본은 <see cref="Parts"/>.</summary>
    public ObservableCollection<VerticalBranchPartItem> PartsTopDown { get; } = [];
    public ObservableCollection<string> AddOptions { get; }
    public ObservableCollection<VerticalBranchPreviewItem> PreviewItems { get; } = [];

    /// <summary>끄면 시작 관경과 DN이 같은 유형만 보임.</summary>
    public bool ShowAllDiameters
    {
        get => _showAllDiameters;
        set { if (SetProperty(ref _showAllDiameters, value)) RefreshAddOptions(); }
    }
    public string? SelectedAddOption { get => _selectedAddOption; set => SetProperty(ref _selectedAddOption, value); }
    public string ErrorText { get => _errorText; private set { if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => ErrorText.Length > 0;
    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }
    public bool CanPlace => VerticalBranchRules.Validate(ToParts()).Count == 0;
    /// <summary>[Revit에 배치]면 true. View가 이 값으로 DialogResult를 정함.</summary>
    public bool Confirmed { get; private set; }
    public Action<bool>? CloseAction { get; set; }

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand MoveTowardStartCommand { get; }
    public ICommand MoveTowardEndCommand { get; }
    public ICommand PlaceCommand { get; }
    public ICommand CancelCommand { get; }

    public IReadOnlyList<VerticalBranchPart> ToParts() => Parts.Select(x => x.ToPart()).ToList();

    private bool MatchesDiameter(string name)
        => NominalDiameterParser.Parse(name) is { } dn && Math.Abs(dn - Start.DiameterMm) < 0.5;

    private void RefreshAddOptions()
    {
        var keep = SelectedAddOption;
        AddOptions.Clear();
        AddOptions.Add(PipeOption);
        foreach (var name in _valveNames.Where(x => ShowAllDiameters || MatchesDiameter(x) || !_familiesWithDiameterMatch.Contains(FamilyNameOf(x))))
            AddOptions.Add(name);
        SelectedAddOption = keep is not null && AddOptions.Contains(keep) ? keep : AddOptions.FirstOrDefault();
    }

    private void Add()
    {
        var option = SelectedAddOption;
        if (string.IsNullOrWhiteSpace(option)) return;
        AddPart(option == PipeOption ? new VerticalBranchPartItem("", DefaultPipeLengthMm) : new VerticalBranchPartItem(option));
    }

    private void AddPart(VerticalBranchPartItem item)
    {
        item.Changed = Refresh;
        Parts.Add(item);
    }

    private void Move(VerticalBranchPartItem? item, int delta)
    {
        if (item is null) return;
        var index = Parts.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Parts.Count) return;
        Parts.Move(index, target);
    }

    // 추가·삭제·이동 때만 다시 만듦. 길이 입력 중 재생성하면 TextBox 포커스 잃음.
    private void RefreshPartsTopDown()
    {
        PartsTopDown.Clear();
        foreach (var item in Parts.Reverse()) PartsTopDown.Add(item);
    }

    private void Close(bool confirmed)
    {
        Confirmed = confirmed;
        CloseAction?.Invoke(confirmed);
    }

    /// <summary>검증·요약·미리보기 다시 계산. 행 추가·삭제·이동·길이 변경마다.</summary>
    private void Refresh()
    {
        var parts = ToParts();
        ErrorText = string.Join(Environment.NewLine, VerticalBranchRules.Validate(parts));
        var pipeSum = VerticalBranchRules.PipeLengthSum(parts);
        var valveCount = parts.Count(x => !x.IsPipe);
        SummaryText = FormattableString.Invariant(
            $"부품 {parts.Count}개 · 단관 합 {pipeSum:N0}mm · 밸브 {valveCount}개(폭은 배치 때 실측)");
        OnPropertyChanged(nameof(CanPlace));
        CommandManager.InvalidateRequerySuggested();

        // 아래(시작)부터 위로. 단관은 길이 비례, 밸브는 기호 고정.
        PreviewItems.Clear();
        var pipeSpace = Math.Max(PreviewHeight - valveCount * ValveSymbolHeight, MinPipeHeight * parts.Count);
        var scale = pipeSum > 0 ? pipeSpace / pipeSum : 0;
        var bottom = PreviewHeight;
        foreach (var item in Parts)
        {
            var height = item.IsPipe ? Math.Max(item.LengthMm * scale, MinPipeHeight) : ValveSymbolHeight;
            var top = bottom - height;
            PreviewItems.Add(new VerticalBranchPreviewItem(top, height, item.IsPipe,
                item.IsPipe ? PipeOption : ShortName(item.FamilyTypeName),
                item.IsPipe ? FormattableString.Invariant($"{item.LengthMm:N0}") : "실측"));
            bottom = top;
        }
    }

    private static string FamilyNameOf(string familyTypeName)
    {
        var separator = familyTypeName.IndexOf(" : ", StringComparison.Ordinal);
        return separator < 0 ? familyTypeName : familyTypeName[..separator];
    }

    private static string ShortName(string familyTypeName)
    {
        var family = FamilyNameOf(familyTypeName);
        return family.StartsWith("DH_", StringComparison.Ordinal) ? family["DH_".Length..] : family;
    }
}
