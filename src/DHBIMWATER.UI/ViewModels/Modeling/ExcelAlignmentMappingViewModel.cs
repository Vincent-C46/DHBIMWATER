using DHBIMWATER.UI.Utilities;
using System.Collections.ObjectModel;
using System.Data;
using System.Windows.Input;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;

namespace DHBIMWATER.UI.ViewModels.Modeling;

public sealed class ExcelSheetPickItem : ViewModelBase
{
    private bool _isChecked;
    public required string Name { get; init; }
    public bool IsChecked { get => _isChecked; set { if (SetProperty(ref _isChecked, value)) CheckedChanged?.Invoke(); } }
    public Action? CheckedChanged { get; init; }
}

/// <summary>열 매핑 콤보박스 항목. Index는 0-based 열 번호(ExcelAlignmentMapping과 동일 기준)이고, 표시 텍스트에 열 문자를 함께 보여줘 헤더가 비었거나 중복돼도 구분할 수 있다.</summary>
public sealed record ExcelColumnOption(int Index, string Display)
{
    public override string ToString() => Display;
}

/// <summary>엑셀 좌표표(코리더 점 보고서 등)의 시트·헤더행·시작행·X·Y·Z·Station 열을 지정하는 대화상자.</summary>
public sealed class ExcelAlignmentMappingViewModel : ViewModelBase
{
    private readonly IExcelAlignmentSourceReader _reader;
    private readonly IDialogService _dialog;
    private int _headerRow = 1, _dataStartRow = 2;
    private ExcelSheetPickItem? _previewSheet;
    private ExcelColumnOption? _xColumn, _yColumn, _zColumn, _stationColumn, _bendKindColumn, _vertexDiameterColumn, _valveRoomTypeColumn, _groundElColumn, _cumulativeDistanceColumn;
    private bool _isValveRoomMode;
    // 밸브실 모드 "기타 열" Expander(docs/310). X·Y 추정 실패 시 펼친다.
    private bool _isOtherColumnsExpanded;
    // 밸브실 모드 하단 목록(docs/304). 미리보기 갱신 중엔 열 세터마다 파일을 다시 읽지 않게 막는다.
    private ValveRoomListRow? _selectedValveRoomRow;
    private bool _suspendValveRoomRefresh;
    private string _valveRoomListStatus = string.Empty;
    private StationParseMode _stationMode = StationParseMode.Station;
    private double _numberChainIntervalM = 20d;
    private DataTable _previewTable = new();

    public ExcelAlignmentMappingViewModel(IExcelAlignmentSourceReader reader, IDialogService dialog, string filePath)
    {
        _reader = reader; _dialog = dialog; FilePath = filePath;
        foreach (var name in reader.GetSheetNames(filePath)) Sheets.Add(new ExcelSheetPickItem { Name = name, CheckedChanged = OnSheetCheckedChanged });
        OkCommand = new RelayCommand(_ => Confirm());
        CancelCommand = new RelayCommand(_ => CloseAction?.Invoke());
        RefreshPreview();
    }

    public ExcelAlignmentMappingViewModel(IExcelAlignmentSourceReader reader, IDialogService dialog, string filePath, ExcelAlignmentMapping existingMapping)
        : this(reader, dialog, filePath)
    {
        IsSheetSelectionLocked = true;
        HeaderRow = existingMapping.HeaderRow;
        DataStartRow = existingMapping.DataStartRow;
        StationMode = existingMapping.StationMode;
        NumberChainIntervalM = existingMapping.NumberChainIntervalM;
        var sheet = Sheets.SingleOrDefault(x => string.Equals(x.Name, existingMapping.SheetName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"시트 '{existingMapping.SheetName}'를 찾을 수 없습니다.");
        sheet.IsChecked = true;
        PreviewSheet = sheet;
        XColumn = FindColumn(existingMapping.XColumnIndex);
        YColumn = FindColumn(existingMapping.YColumnIndex);
        ZColumn = FindColumn(existingMapping.ZColumnIndex);
        StationColumn = FindOptionalColumn(existingMapping.StationColumnIndex);
        BendKindColumn = FindOptionalColumn(existingMapping.BendKindColumnIndex);
        VertexDiameterColumn = FindOptionalColumn(existingMapping.VertexDiameterColumnIndex);
        ValveRoomTypeColumn = FindOptionalColumn(existingMapping.ValveRoomTypeColumnIndex);
        GroundElColumn = FindOptionalColumn(existingMapping.GroundElColumnIndex);
    }

    public string FilePath { get; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public bool IsSheetSelectionLocked { get; }
    public bool IsSheetSelectionEnabled => !IsSheetSelectionLocked;
    public ObservableCollection<ExcelSheetPickItem> Sheets { get; } = new();
    public IEnumerable<ExcelSheetPickItem> CheckedSheets => Sheets.Where(x => x.IsChecked);
    public ObservableCollection<ExcelColumnOption> ColumnOptions { get; } = new();
    /// <summary>Station처럼 선택 사항인 열의 콤보박스 목록. 맨 앞에 "(사용 안 함)"을 둔다.</summary>
    public ObservableCollection<ExcelColumnOption> OptionalColumnOptions { get; } = new();
    public DataView PreviewView => _previewTable.DefaultView;

    public int HeaderRow
    {
        get => _headerRow;
        set
        {
            if (!SetProperty(ref _headerRow, value)) return;
            // 헤더 변경 시 시작 행을 다음 행으로 맞춤. 직접 수정값은 다음 헤더 변경까지 유지.
            // 백킹 필드 직접 변경: RefreshPreview() 중복 호출 방지.
            _dataStartRow = value + 1;
            OnPropertyChanged(nameof(DataStartRow));
            RefreshPreview();
        }
    }
    public int DataStartRow { get => _dataStartRow; set { if (SetProperty(ref _dataStartRow, value)) RefreshPreview(); } }
    public ExcelSheetPickItem? PreviewSheet { get => _previewSheet; set { if (SetProperty(ref _previewSheet, value)) RefreshPreview(); } }

    public ExcelColumnOption? XColumn { get => _xColumn; set { if (SetProperty(ref _xColumn, value)) OnPropertyChanged(nameof(CanConfirm)); } }
    public ExcelColumnOption? YColumn { get => _yColumn; set { if (SetProperty(ref _yColumn, value)) OnPropertyChanged(nameof(CanConfirm)); } }
    public ExcelColumnOption? ZColumn { get => _zColumn; set { if (SetProperty(ref _zColumn, value)) NotifyConfirmState(); } }
    /// <summary>선택 사항. null이면 "(사용 안 함)".</summary>
    public ExcelColumnOption? StationColumn { get => _stationColumn; set { if (SetProperty(ref _stationColumn, value)) NotifyConfirmState(); } }
    public ExcelColumnOption? BendKindColumn { get => _bendKindColumn; set => SetProperty(ref _bendKindColumn, value); }
    public ExcelColumnOption? VertexDiameterColumn { get => _vertexDiameterColumn; set { if (SetProperty(ref _vertexDiameterColumn, value)) NotifyConfirmState(); } }
    /// <summary>밸브실 유형 열(docs/304). 값 있는 행 = 밸브실 위치. 밸브실 모드에서만 보이고 필수.</summary>
    public ExcelColumnOption? ValveRoomTypeColumn { get => _valveRoomTypeColumn; set { if (SetProperty(ref _valveRoomTypeColumn, value)) NotifyConfirmState(); } }
    /// <summary>원지반 Z 열(docs/304). 밸브실 모드에서만 보이고 필수.</summary>
    public ExcelColumnOption? GroundElColumn { get => _groundElColumn; set { if (SetProperty(ref _groundElColumn, value)) NotifyConfirmState(); } }
    /// <summary>누가거리 열(docs/310). 밸브실 목록 표시용만 — 위치는 측점 텍스트로 찾는다. 매핑 레코드에 안 넣는다.</summary>
    public ExcelColumnOption? CumulativeDistanceColumn { get => _cumulativeDistanceColumn; set { if (SetProperty(ref _cumulativeDistanceColumn, value)) RefreshValveRoomRows(); } }
    /// <summary>밸브실 모드에서 X·Y·Station 방식·곡관 종류를 담은 "기타 열" 펼침 상태.</summary>
    public bool IsOtherColumnsExpanded { get => _isOtherColumnsExpanded; set => SetProperty(ref _isOtherColumnsExpanded, value); }
    /// <summary>true = 밸브실 위치 자동지정(docs/304). 선형 시트 1장에 밸브실 행을 끼운 표 — Z는 관중심으로 읽고(docs/310),
    /// 측점·직경(DN)·밸브실 유형·원지반 열을 필수로 본다. 관로 모델링은 false.</summary>
    public bool IsValveRoomMode
    {
        get => _isValveRoomMode;
        set
        {
            if (!SetProperty(ref _isValveRoomMode, value)) return;
            OnPropertyChanged(nameof(ZLabel));
            OnPropertyChanged(nameof(IsPipeMode));
            if (value && PreviewSheet is not null && (XColumn is null || YColumn is null)) IsOtherColumnsExpanded = true;
            NotifyConfirmState();
        }
    }
    /// <summary>관로 모델링 모드(밸브실 모드 반대). 열 지정 레이아웃 전환용.</summary>
    public bool IsPipeMode => !IsValveRoomMode;
    /// <summary>현재 열 매핑으로 읽은 밸브실 행(유형 칸 채움). 열을 바꾸면 다시 읽는다.</summary>
    public ObservableCollection<ValveRoomListRow> ValveRoomRows { get; } = new();
    /// <summary>[확인] 시 밸브실 창에 넣을 행. 밸브실 모드에선 필수.</summary>
    public ValveRoomListRow? SelectedValveRoomRow
    {
        get => _selectedValveRoomRow;
        set { if (SetProperty(ref _selectedValveRoomRow, value)) { OnPropertyChanged(nameof(CanConfirm)); OnPropertyChanged(nameof(StatusText)); } }
    }
    /// <summary>목록 상태 안내(개수·누락 경고·오류).</summary>
    public string ValveRoomListStatus { get => _valveRoomListStatus; private set => SetProperty(ref _valveRoomListStatus, value); }
    public string ZLabel => IsValveRoomMode ? "표고 (관중심) *" : "Z *";
    public StationParseMode StationMode { get => _stationMode; set { if (SetProperty(ref _stationMode, value)) { OnPropertyChanged(nameof(IsStationMode)); OnPropertyChanged(nameof(IsNumberChainMode)); } } }
    public bool IsStationMode { get => StationMode == StationParseMode.Station; set { if (value) StationMode = StationParseMode.Station; } }
    public bool IsNumberChainMode { get => StationMode == StationParseMode.NumberChain; set { if (value) StationMode = StationParseMode.NumberChain; } }
    public double NumberChainIntervalM { get => _numberChainIntervalM; set => SetProperty(ref _numberChainIntervalM, value); }

    public int CheckedCount => Sheets.Count(x => x.IsChecked);
    public bool CanConfirm => CheckedCount > 0 && XColumn is not null && YColumn is not null && ZColumn is not null
        && (!IsValveRoomMode || (UsedIndex(StationColumn) is not null && UsedIndex(VertexDiameterColumn) is not null
            && UsedIndex(ValveRoomTypeColumn) is not null && UsedIndex(GroundElColumn) is not null && SelectedValveRoomRow is not null));
    public string StatusText => $"시트 {CheckedCount}개" + (IsValveRoomMode
        ? (CanConfirm ? $" · 선택: {SelectedValveRoomRow?.Display}" : " · 열 지정 후 아래 목록에서 밸브실을 고르세요")
        : (CanConfirm ? " · X·Y·Z 지정 완료" : " · X·Y·Z 미지정"));

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }
    public Action? CloseAction { get; set; }
    public IReadOnlyList<ExcelAlignmentMapping>? Result { get; private set; }

    private void NotifyConfirmState() { RefreshValveRoomRows(); OnPropertyChanged(nameof(CanConfirm)); OnPropertyChanged(nameof(StatusText)); }

    private void OnSheetCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedSheets));
        OnPropertyChanged(nameof(CheckedCount)); OnPropertyChanged(nameof(CanConfirm)); OnPropertyChanged(nameof(StatusText));
        if (PreviewSheet is null || !PreviewSheet.IsChecked) PreviewSheet = Sheets.FirstOrDefault(x => x.IsChecked);
    }

    private void RefreshPreview()
    {
        _suspendValveRoomRefresh = true;
        try { RefreshPreviewCore(); }
        finally { _suspendValveRoomRefresh = false; }
        RefreshValveRoomRows();
    }

    /// <summary>밸브실 모드 하단 목록을 현재 매핑으로 다시 읽는다. 같은 엑셀 행 번호면 선택을 유지한다.</summary>
    private void RefreshValveRoomRows()
    {
        if (!IsValveRoomMode || _suspendValveRoomRefresh) return;
        var previousRow = SelectedValveRoomRow?.RowNumber;
        ValveRoomRows.Clear();
        _selectedValveRoomRow = null;
        if (PreviewSheet is null || BuildMapping(PreviewSheet.Name) is not { } mapping)
        {
            ValveRoomListStatus = "측점·표고(관중심)·호칭지름(DN)·밸브실 유형·원지반 열과 X·Y(기타 열)를 지정하면 밸브실 목록이 표시됩니다.";
        }
        else
        {
            try
            {
                var list = _reader.ReadValveRoomList(FilePath, mapping, UsedIndex(CumulativeDistanceColumn));
                foreach (var row in list.Rows) ValveRoomRows.Add(row);
                ValveRoomListStatus = $"밸브실 {list.Rows.Count}개" + (list.Warnings.Count > 0 ? " · " + string.Join(" · ", list.Warnings) : string.Empty);
            }
            catch (Exception ex)
            {
                ValveRoomListStatus = ex is DHBIMWATER.Shared.Errors.DhbimwaterException ? ex.Message : UiErrors.Report("ValveRoom", ex);
            }
        }
        _selectedValveRoomRow = ValveRoomRows.FirstOrDefault(x => x.RowNumber == previousRow);
        OnPropertyChanged(nameof(SelectedValveRoomRow));
    }

    /// <summary>현재 콤보 값으로 매핑을 만든다. 필수 열(밸브실 모드면 측점·DN·유형·원지반 포함)이 비면 null.</summary>
    private ExcelAlignmentMapping? BuildMapping(string sheetName)
    {
        if (XColumn is null || YColumn is null || ZColumn is null) return null;
        var station = UsedIndex(StationColumn);
        var vertexDiameter = UsedIndex(VertexDiameterColumn);
        var valveRoomType = UsedIndex(ValveRoomTypeColumn);
        var groundEl = UsedIndex(GroundElColumn);
        if (IsValveRoomMode && (station is null || vertexDiameter is null || valveRoomType is null || groundEl is null)) return null;
        return new ExcelAlignmentMapping(sheetName, HeaderRow, DataStartRow, XColumn.Index, YColumn.Index, ZColumn.Index, station, StationMode, NumberChainIntervalM,
            UsedIndex(BendKindColumn), vertexDiameter, valveRoomType, groundEl);
    }

    private void RefreshPreviewCore()
    {
        var previousX = XColumn?.Index; var previousY = YColumn?.Index; var previousZ = ZColumn?.Index;
        // 선택 열이 "(사용 안 함)"(-1)이면 이전 값이 없는 것으로 보고 다시 추정한다. 헤더가 4행인 코리더 점 보고서는
        // 처음(헤더 1행) 추정이 실패해 -1로 굳고, 헤더 행을 4로 바꿔도 Station·곡관유형·호칭지름이 매핑되지 않았다.
        var previousStation = UsedIndex(StationColumn); var previousBendKind = UsedIndex(BendKindColumn); var previousVertexDiameter = UsedIndex(VertexDiameterColumn);
        var previousValveRoomType = UsedIndex(ValveRoomTypeColumn); var previousGroundEl = UsedIndex(GroundElColumn); var previousCumulative = UsedIndex(CumulativeDistanceColumn);
        ColumnOptions.Clear(); OptionalColumnOptions.Clear();
        if (PreviewSheet is null || HeaderRow < 1) { _previewTable = new DataTable(); OnPropertyChanged(nameof(PreviewView)); XColumn = YColumn = ZColumn = StationColumn = BendKindColumn = VertexDiameterColumn = ValveRoomTypeColumn = GroundElColumn = CumulativeDistanceColumn = null; return; }

        IReadOnlyList<IReadOnlyList<string?>> rows;
        try { rows = _reader.PreviewRows(FilePath, PreviewSheet.Name, Math.Max(HeaderRow, DataStartRow) + 3); }
        catch (Exception ex) { _dialog.Warn("엑셀 미리보기", UiErrors.Report("PipeAlignment.Excel", ex)); return; }

        // 미리보기는 엑셀처럼 열 문자(A, B, C ...)를 그대로 헤더로 쓴다 — 실제 헤더 텍스트는 중복·공백일 수 있어 DataTable 열 이름으로 못 쓴다.
        var table = new DataTable();
        table.Columns.Add("#");
        var columnCount = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        for (var c = 0; c < columnCount; c++) table.Columns.Add(ColumnLetter(c));
        for (var i = 0; i < rows.Count; i++)
        {
            var rowNumber = i + 1;
            var row = table.NewRow();
            row[0] = rowNumber == HeaderRow ? $"{rowNumber} (헤더)" : rowNumber.ToString();
            for (var c = 0; c < rows[i].Count; c++) row[c + 1] = rows[i][c];
            table.Rows.Add(row);
        }
        _previewTable = table;
        OnPropertyChanged(nameof(PreviewView));

        if (HeaderRow <= rows.Count)
        {
            var header = rows[HeaderRow - 1];
            for (var c = 0; c < header.Count; c++)
            {
                var text = string.IsNullOrWhiteSpace(header[c]) ? $"(빈 열 {ColumnLetter(c)})" : header[c];
                ColumnOptions.Add(new ExcelColumnOption(c, $"{text} ({ColumnLetter(c)})"));
            }
        }
        OptionalColumnOptions.Add(new ExcelColumnOption(-1, "(사용 안 함)"));
        foreach (var option in ColumnOptions) OptionalColumnOptions.Add(option);

        var headers = HeaderRow <= rows.Count ? rows[HeaderRow - 1] : Array.Empty<string?>();
        XColumn = previousX is null ? GuessColumn(headers, XKeywords) : ColumnOptions.FirstOrDefault(x => x.Index == previousX);
        YColumn = previousY is null ? GuessColumn(headers, YKeywords) : ColumnOptions.FirstOrDefault(x => x.Index == previousY);
        ZColumn = previousZ is null ? GuessColumn(headers, ZKeywords) : ColumnOptions.FirstOrDefault(x => x.Index == previousZ);
        StationColumn = previousStation is null
            ? OptionalMatch(GuessColumn(headers, StationKeywords))
            : OptionalColumnOptions.FirstOrDefault(x => x.Index == previousStation) ?? OptionalColumnOptions.First();
        BendKindColumn = previousBendKind is null
            ? OptionalMatch(GuessColumn(headers, BendKindKeywords))
            : OptionalColumnOptions.FirstOrDefault(x => x.Index == previousBendKind) ?? OptionalColumnOptions.First();
        VertexDiameterColumn = previousVertexDiameter is null
            ? OptionalMatch(GuessColumn(headers, VertexDiameterKeywords))
            : OptionalColumnOptions.FirstOrDefault(x => x.Index == previousVertexDiameter) ?? OptionalColumnOptions.First();
        ValveRoomTypeColumn = previousValveRoomType is null
            ? OptionalMatch(GuessColumn(headers, ValveRoomTypeKeywords))
            : OptionalColumnOptions.FirstOrDefault(x => x.Index == previousValveRoomType) ?? OptionalColumnOptions.First();
        GroundElColumn = previousGroundEl is null
            ? OptionalMatch(GuessColumn(headers, GroundElKeywords))
            : OptionalColumnOptions.FirstOrDefault(x => x.Index == previousGroundEl) ?? OptionalColumnOptions.First();
        CumulativeDistanceColumn = previousCumulative is null
            ? OptionalMatch(GuessColumn(headers, CumulativeDistanceKeywords))
            : OptionalColumnOptions.FirstOrDefault(x => x.Index == previousCumulative) ?? OptionalColumnOptions.First();
        // 접힌 X·Y를 못 잡았으면 펼쳐서 보여준다(docs/310).
        if (IsValveRoomMode && (XColumn is null || YColumn is null)) IsOtherColumnsExpanded = true;
        OnPropertyChanged(nameof(CanConfirm)); OnPropertyChanged(nameof(StatusText));
    }

    private void Confirm()
    {
        if (CheckedCount == 0) { _dialog.Warn("입력 확인", "시트를 1개 이상 선택하세요."); return; }
        if (IsValveRoomMode && SelectedValveRoomRow is null) { _dialog.Warn("입력 확인", "아래 목록에서 밸브실을 고르세요."); return; }
        if (HeaderRow < 1) { _dialog.Warn("입력 확인", "헤더 행은 1 이상이어야 합니다."); return; }
        if (DataStartRow <= HeaderRow) { _dialog.Warn("입력 확인", "데이터 시작 행은 헤더 행보다 커야 합니다."); return; }
        if (XColumn is null || YColumn is null || ZColumn is null) { _dialog.Warn("입력 확인", "X, Y, Z 열은 필수입니다."); return; }
        var station = StationColumn is { Index: >= 0 } ? StationColumn.Index : (int?)null;
        if (StationMode == StationParseMode.NumberChain && NumberChainIntervalM <= 0) { _dialog.Warn("입력 확인", "NumberChain 간격은 0보다 커야 합니다."); return; }
        var bendKind = BendKindColumn is { Index: >= 0 } ? BendKindColumn.Index : (int?)null;
        var vertexDiameter = VertexDiameterColumn is { Index: >= 0 } ? VertexDiameterColumn.Index : (int?)null;
        var valveRoomType = ValveRoomTypeColumn is { Index: >= 0 } ? ValveRoomTypeColumn.Index : (int?)null;
        var groundEl = GroundElColumn is { Index: >= 0 } ? GroundElColumn.Index : (int?)null;
        if (IsValveRoomMode && (station is null || vertexDiameter is null || valveRoomType is null || groundEl is null))
        { _dialog.Warn("입력 확인", "밸브실 위치 자동지정은 측점·직경(DN)·밸브실 유형·원지반 열이 필수입니다."); return; }
        Result = Sheets.Where(x => x.IsChecked)
            .Select(x => new ExcelAlignmentMapping(x.Name, HeaderRow, DataStartRow, XColumn.Index, YColumn.Index, ZColumn.Index, station, StationMode, NumberChainIntervalM, bendKind, vertexDiameter, valveRoomType, groundEl))
            .ToList();
        CloseAction?.Invoke();
    }

    private static string ColumnLetter(int index)
    {
        var letter = string.Empty; var n = index;
        do { letter = (char)('A' + n % 26) + letter; n = n / 26 - 1; } while (n >= 0);
        return letter;
    }

    private static readonly string[] XKeywords = ["X", "EAST", "EASTING", "E/W", "동서", "동/서",];
    private static readonly string[] YKeywords = ["Y", "NORTH", "NORTHING", "N/S", "남북", "남/북",];
    private static readonly string[] ZKeywords = ["Z", "표고", "고도", "EL", "ELEV", "LEVEL", "높이"];
    private static readonly string[] StationKeywords = ["STA", "STATION", "측점", "측점번호", "측점명", "누가거리", "CHAINAGE", "NO"];
    private static readonly string[] BendKindKeywords = ["곡관유형", "곡관종류", "곡관", "BEND"];
    private static readonly string[] ValveRoomTypeKeywords = ["밸브실유형", "밸브실종류", "밸브실", "유형"];
    private static readonly string[] GroundElKeywords = ["원지반", "원지반Z", "원지반EL", "원지반고", "지반고", "지표EL", "지표고", "GL"];
    private static readonly string[] CumulativeDistanceKeywords = ["누가거리", "누가 거리", "CUMULATIVE", "CHAINAGE(M)"];
    private static readonly string[] VertexDiameterKeywords = ["호칭지름(DN)", "호칭지름\n(DN)", "호칭지름", "DN", "직경", "관경", "구경", "호구경"];

    private ExcelColumnOption OptionalMatch(ExcelColumnOption? option)
        => option is null
            ? OptionalColumnOptions.First()
            : OptionalColumnOptions.FirstOrDefault(x => x.Index == option.Index) ?? OptionalColumnOptions.First();

    private static int? UsedIndex(ExcelColumnOption? option) => option is { Index: >= 0 } ? option.Index : null;

    private ExcelColumnOption? FindColumn(int index) => ColumnOptions.FirstOrDefault(x => x.Index == index);
    private ExcelColumnOption FindOptionalColumn(int? index) => index is { } value
        ? OptionalColumnOptions.FirstOrDefault(x => x.Index == value) ?? OptionalColumnOptions.First()
        : OptionalColumnOptions.First();

    private ExcelColumnOption? GuessColumn(IReadOnlyList<string?> headers, IEnumerable<string> keywords)
    {
        var normalizedKeywords = new HashSet<string>(keywords.Select(AlignmentAttributeParser.NormalizeHeader), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
            if (normalizedKeywords.Contains(AlignmentAttributeParser.NormalizeHeader(headers[i])))
                return ColumnOptions.FirstOrDefault(x => x.Index == i);
        return null;
    }
}
