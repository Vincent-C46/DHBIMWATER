using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>밸브실 제원표 1행 편집 사본(docs/312).</summary>
public sealed class ValveRoomDimensionRowViewModel : ViewModelBase
{
    private string? _variant;
    private int _group;
    private double _diameterMm;
    private double _lengthMm;
    private double _widthMm;
    private double _heightMm;
    private string _section;

    public ValveRoomDimensionRowViewModel(ValveRoomStandardDimension source)
    {
        RoomKind = source.RoomKind;
        _variant = source.Variant;
        _group = source.Group;
        _diameterMm = source.DiameterMm;
        _lengthMm = source.LengthMm;
        _widthMm = source.WidthMm;
        _heightMm = source.HeightMm;
        _section = source.Section;
    }

    public string RoomKind { get; }
    public string? Variant { get => _variant; set => SetProperty(ref _variant, value); }
    public int Group { get => _group; set => SetProperty(ref _group, value); }
    public double DiameterMm { get => _diameterMm; set => SetProperty(ref _diameterMm, value); }
    public double LengthMm { get => _lengthMm; set => SetProperty(ref _lengthMm, value); }
    public double WidthMm { get => _widthMm; set => SetProperty(ref _widthMm, value); }
    public double HeightMm { get => _heightMm; set => SetProperty(ref _heightMm, value); }
    public string Section { get => _section; set => SetProperty(ref _section, value ?? string.Empty); }

    public ValveRoomStandardDimension ToDimension()
        => new(RoomKind, string.IsNullOrWhiteSpace(Variant) ? null : Variant, Group, DiameterMm, LengthMm, WidthMm, HeightMm, Section);
}

/// <summary>밸브실 제원표 확인·편집 창(docs/312). 유형 하나씩 골라 행을 보고 고친다. 저장은 <see cref="ValveRoomDimensionCatalogLauncher"/>가 한다.
/// <para>유형·형식 목록은 내장 표준표 기준 고정. 행 추가·삭제·값 수정만 한다.</para></summary>
public sealed class ValveRoomDimensionCatalogViewModel : ViewModelBase
{
    // 유형별 행 전체. 화면에는 고른 유형만 VisibleRows로 보인다.
    private readonly List<ValveRoomDimensionRowViewModel> _rows = [];
    private string _selectedRoomKind;
    private ValveRoomDimensionRowViewModel? _selectedRow;
    private string? _errorMessage;

    public ValveRoomDimensionCatalogViewModel(ValveRoomDimensionTable table, string? initialRoomKind = null)
    {
        RoomKinds = ValveRoomStandardDimensionCatalog.RoomKinds;
        _selectedRoomKind = initialRoomKind is not null && RoomKinds.Contains(initialRoomKind) ? initialRoomKind : RoomKinds[0];
        Load(table);

        AddRowCommand = new RelayCommand(_ => AddRow());
        DeleteRowCommand = new RelayCommand(_ => DeleteRow(), _ => SelectedRow is not null);
        ResetCommand = new RelayCommand(_ => Load(ValveRoomStandardDimensionCatalog.Default));
        ConfirmCommand = new RelayCommand(_ => Confirm());
        CancelCommand = new RelayCommand(_ => CloseAction?.Invoke(false));
    }

    public IReadOnlyList<string> RoomKinds { get; }
    public ObservableCollection<ValveRoomDimensionRowViewModel> VisibleRows { get; } = [];
    public ICommand AddRowCommand { get; }
    public ICommand DeleteRowCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }
    /// <summary>View가 연결한다. 인자는 DialogResult.</summary>
    public Action<bool>? CloseAction { get; set; }

    public string SelectedRoomKind
    {
        get => _selectedRoomKind;
        set
        {
            if (value is null || !SetProperty(ref _selectedRoomKind, value)) return;
            RefreshVisibleRows();
            OnPropertyChanged(nameof(VariantOptions));
            OnPropertyChanged(nameof(HasVariants));
        }
    }

    /// <summary>고른 유형의 형식 목록(내장 표준표 기준). 형식 없는 유형이면 빈 목록.</summary>
    public IReadOnlyList<string> VariantOptions => ValveRoomStandardDimensionCatalog.Variants(SelectedRoomKind);
    public bool HasVariants => VariantOptions.Count > 0;

    public ValveRoomDimensionRowViewModel? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }

    /// <summary>[확인] 검사 실패 문구. 비면 숨긴다.</summary>
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public ValveRoomDimensionTable ToTable()
    {
        // 유형 순서는 내장 표 순서, 그 안에서 형식 → 관경 순으로 정렬해 저장한다.
        var kindOrder = RoomKinds.Select((kind, index) => (kind, index)).ToDictionary(x => x.kind, x => x.index);
        return new ValveRoomDimensionTable(_rows
            .Select(x => x.ToDimension())
            .OrderBy(x => kindOrder.TryGetValue(x.RoomKind, out var i) ? i : int.MaxValue)
            .ThenBy(x => VariantOrder(x.RoomKind, x.Variant))
            .ThenBy(x => x.DiameterMm));
    }

    private static int VariantOrder(string roomKind, string? variant)
    {
        if (variant is null) return 0;
        var index = ValveRoomStandardDimensionCatalog.Variants(roomKind).ToList().IndexOf(variant);
        return index < 0 ? int.MaxValue : index;
    }

    private void Load(ValveRoomDimensionTable table)
    {
        _rows.Clear();
        _rows.AddRange(table.Entries.Select(x => new ValveRoomDimensionRowViewModel(x)));
        ErrorMessage = null;
        RefreshVisibleRows();
    }

    private void RefreshVisibleRows()
    {
        VisibleRows.Clear();
        foreach (var row in _rows.Where(x => x.RoomKind == SelectedRoomKind)) VisibleRows.Add(row);
        SelectedRow = null;
    }

    /// <summary>고른 유형 끝에 행 추가. 마지막 행 값을 복사해 시작점으로 쓴다(없으면 0).</summary>
    private void AddRow()
    {
        var last = VisibleRows.LastOrDefault();
        var seed = last?.ToDimension()
            ?? new ValveRoomStandardDimension(SelectedRoomKind, VariantOptions.FirstOrDefault(), 0, 0, 0, 0, 0, string.Empty);
        var row = new ValveRoomDimensionRowViewModel(seed with { DiameterMm = 0 });
        _rows.Add(row);
        VisibleRows.Add(row);
        SelectedRow = row;
    }

    private void DeleteRow()
    {
        if (SelectedRow is not { } row) return;
        _rows.Remove(row);
        VisibleRows.Remove(row);
        SelectedRow = null;
    }

    private void Confirm()
    {
        ErrorMessage = ToTable().Validate();
        if (ErrorMessage is null) CloseAction?.Invoke(true);
    }
}
