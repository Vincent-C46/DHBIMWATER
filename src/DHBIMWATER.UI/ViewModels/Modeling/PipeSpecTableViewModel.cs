using DHBIMWATER.UI.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.UseCases.Gis;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>
/// 관·곡관 설정 창의 통합 ViewModel — 직관 제원 · 곡관치수 · 허용굴곡 3탭을 한 창에서 편집한다
/// (2026-08-20, 이전에는 곡관치수까지가 이 창이고 허용굴곡은 별도 창이었다).
/// 강관(KS D 3565/3578)은 외경이 주철관과 달라 별도 탭으로 둔다(docs/211 § H).
/// </summary>
public sealed class PipeSpecTableViewModel : ViewModelBase
{
    private BendSettings _baseSettings;
    private readonly SaveBendSettingsUseCase _save;
    private readonly IDialogService _dialog;
    private readonly IFileDialogService _fileDialog;
    private readonly IBendSettingsFileStore _fileStore;
    private readonly IRevitDispatcher _revit;
    private int _selectedFittingTabIndex, _selectedTabIndex;
    private SteelFittingClass _selectedSteelFittingClass = SteelFittingClass.F12;
    private ICollectionView? _steelSocketFittingRowsView, _steelFlangedFittingRowsView;
    private PipeMaterial _selectedMaterial;
    /// <summary>주 탭 인덱스 — 0 주철관 직관 제원, 1 강관 직관 제원, 2 주철관 직관 무게, 3 곡관 치수, 4 허용굴곡(주철관).</summary>
    private const int DuctileStraightTab = 0, SteelStraightTab = 1, FittingTab = 3;
    private const int DuctileSocketFittingTab = 0, SteelWeldFittingTab = 2;
    /// <param name="initialMaterial">모델링 창의 관종 토글 값. 창을 열 때 그 관종 탭만 보인다(docs/211 § N).</param>
    public PipeSpecTableViewModel(BendSettings source, SaveBendSettingsUseCase save, IDialogService dialog,
        IFileDialogService fileDialog, IBendSettingsFileStore fileStore, IRevitDispatcher revit, PipeMaterial initialMaterial = PipeMaterial.DuctileIron)
    {
        _baseSettings = source; _save = save; _dialog = dialog; _fileDialog = fileDialog; _fileStore = fileStore; _revit = revit;
        _selectedMaterial = initialMaterial;
        _selectedTabIndex = initialMaterial == PipeMaterial.Steel ? SteelStraightTab : DuctileStraightTab;
        _selectedFittingTabIndex = initialMaterial == PipeMaterial.Steel ? SteelWeldFittingTab : DuctileSocketFittingTab;
        Joint = new JointDeflectionSettingsViewModel(source);
        AddStraightCommand = new RelayCommand(_ => StraightRows.Add(new StraightPipeSpecRow()));
        RemoveStraightCommand = new RelayCommand(_ => { if (SelectedStraight is not null) StraightRows.Remove(SelectedStraight); });
        AddSteelStraightCommand = new RelayCommand(_ => SteelStraightRows.Add(new SteelStraightPipeSpecRow()));
        RemoveSteelStraightCommand = new RelayCommand(_ => { if (SelectedSteelStraight is not null) SteelStraightRows.Remove(SelectedSteelStraight); });
        AddFittingCommand = new RelayCommand(_ => ActiveFittingRows().Add(NewFittingRow(ActiveConnection(), ActiveMaterial(), ActiveMaterial() == PipeMaterial.Steel ? SelectedSteelFittingClass : SteelFittingClass.None)));
        RemoveFittingCommand = new RelayCommand(_ => RemoveSelectedFitting());
        RestoreCommand = new RelayCommand(_ => LoadDefaults());
        SaveCommand = new RelayCommand(_ => Confirm());
        ExportCommand = new RelayCommand(_ => Export());
        ImportCommand = new RelayCommand(_ => Import());
        CancelCommand = new RelayCommand(_ => CloseAction?.Invoke());
        Load(source.StraightPipes, source.Fittings);
    }
    public ObservableCollection<StraightPipeSpecRow> StraightRows { get; } = new();
    /// <summary>강관 직관 제원(docs/211 § H-1). 주철관과 외경이 달라 별도 행이다.</summary>
    public ObservableCollection<SteelStraightPipeSpecRow> SteelStraightRows { get; } = new();
    public ObservableCollection<BendFittingRow> SocketFittingRows { get; } = new();
    public ObservableCollection<BendFittingRow> FlangedFittingRows { get; } = new();
    /// <summary>강관 용접곡관(Socket=용접)·플랜지곡관 전 종류(F12/F15/F20) 행. 그리드에는 <see cref="SelectedSteelFittingClass"/>로 거른 뷰를 보인다.</summary>
    public ObservableCollection<BendFittingRow> SteelSocketFittingRows { get; } = new();
    public ObservableCollection<BendFittingRow> SteelFlangedFittingRows { get; } = new();
    /// <summary>허용굴곡 탭. 독립 창이던 시절의 ViewModel을 그대로 세 번째 탭으로 얹는다.</summary>
    public JointDeflectionSettingsViewModel Joint { get; }
    public StraightPipeSpecRow? SelectedStraight { get; set; }
    public SteelStraightPipeSpecRow? SelectedSteelStraight { get; set; }
    public BendFittingRow? SelectedSocketFitting { get; set; }
    public BendFittingRow? SelectedFlangedFitting { get; set; }
    public BendFittingRow? SelectedSteelSocketFitting { get; set; }
    public BendFittingRow? SelectedSteelFlangedFitting { get; set; }
    /// <summary>곡관 치수 탭 인덱스 — 0 소켓곡관, 1 플랜지곡관, 2 강관 용접곡관, 3 강관 플랜지곡관.</summary>
    public int SelectedFittingTabIndex { get => _selectedFittingTabIndex; set => SetProperty(ref _selectedFittingTabIndex, value); }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }
    /// <summary>관종 토글(docs/211 § N). 그 관종의 탭만 보이고, 바꾸면 첫 탭으로 옮긴다. 저장은 두 관종 행을 모두 보존한다.</summary>
    public PipeMaterial SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (!SetProperty(ref _selectedMaterial, value)) return;
            OnPropertyChanged(nameof(IsDuctileIronSelected)); OnPropertyChanged(nameof(IsSteelSelected));
            // 접힌 탭이 선택된 채 남지 않도록 관종의 첫 탭으로 옮긴다. 곡관 치수 탭이 열려 있었으면 그 자리는 유지한다.
            if (SelectedTabIndex != FittingTab) SelectedTabIndex = value == PipeMaterial.Steel ? SteelStraightTab : DuctileStraightTab;
            SelectedFittingTabIndex = value == PipeMaterial.Steel ? SteelWeldFittingTab : DuctileSocketFittingTab;
        }
    }
    public bool IsDuctileIronSelected { get => _selectedMaterial == PipeMaterial.DuctileIron; set { if (value) SelectedMaterial = PipeMaterial.DuctileIron; } }
    public bool IsSteelSelected { get => _selectedMaterial == PipeMaterial.Steel; set { if (value) SelectedMaterial = PipeMaterial.Steel; } }
    public IReadOnlyList<SteelFittingClass> SteelFittingClassOptions { get; } = new[] { SteelFittingClass.F12, SteelFittingClass.F15, SteelFittingClass.F20 };
    /// <summary>강관 곡관 탭에 보일 종류. 행 자체는 세 종류를 다 갖고 있고 표시만 거른다(docs/211 § H-2).</summary>
    public SteelFittingClass SelectedSteelFittingClass
    {
        get => _selectedSteelFittingClass;
        set { if (SetProperty(ref _selectedSteelFittingClass, value)) { _steelSocketFittingRowsView?.Refresh(); _steelFlangedFittingRowsView?.Refresh(); } }
    }
    // CollectionView는 처음 만들어진 스레드의 Dispatcher에 묶이므로 View 스레드가 바인딩할 때 처음 만든다
    // (PipeAlignmentModelingViewModel.DiameterMappingsView와 같은 이유 — 이 창은 모델링 창의 STA 스레드에서 열린다).
    public ICollectionView SteelSocketFittingRowsView => _steelSocketFittingRowsView ??= CreateSteelView(SteelSocketFittingRows);
    public ICollectionView SteelFlangedFittingRowsView => _steelFlangedFittingRowsView ??= CreateSteelView(SteelFlangedFittingRows);
    private ICollectionView CreateSteelView(ObservableCollection<BendFittingRow> rows)
    {
        var view = CollectionViewSource.GetDefaultView(rows);
        view.Filter = item => item is BendFittingRow row && row.FittingClass == SelectedSteelFittingClass;
        // 행 추가 후에도 DN → 각도 순을 유지한다(2026-09-22).
        view.SortDescriptions.Add(new SortDescription(nameof(BendFittingRow.DiameterMm), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(BendFittingRow.AngleDeg), ListSortDirection.Ascending));
        return view;
    }
    public ICommand AddStraightCommand { get; } public ICommand RemoveStraightCommand { get; }
    public ICommand AddSteelStraightCommand { get; } public ICommand RemoveSteelStraightCommand { get; }
    public ICommand AddFittingCommand { get; } public ICommand RemoveFittingCommand { get; }
    public ICommand RestoreCommand { get; } public ICommand SaveCommand { get; } public ICommand ExportCommand { get; } public ICommand ImportCommand { get; } public ICommand CancelCommand { get; }
    public Action? CloseAction { get; set; }
    public BendSettings? Result { get; private set; }

    private static BendFittingRow NewFittingRow(BendConnection connection, PipeMaterial material = PipeMaterial.DuctileIron, SteelFittingClass fittingClass = SteelFittingClass.None)
        => new() { Connection = connection, Material = material, FittingClass = fittingClass };
    private BendConnection ActiveConnection() => SelectedFittingTabIndex is 1 or 3 ? BendConnection.Flanged : BendConnection.Socket;
    private PipeMaterial ActiveMaterial() => SelectedFittingTabIndex >= 2 ? PipeMaterial.Steel : PipeMaterial.DuctileIron;
    private ObservableCollection<BendFittingRow> ActiveFittingRows() => SelectedFittingTabIndex switch
    {
        1 => FlangedFittingRows,
        2 => SteelSocketFittingRows,
        3 => SteelFlangedFittingRows,
        _ => SocketFittingRows
    };
    private void RemoveSelectedFitting()
    {
        switch (SelectedFittingTabIndex)
        {
            case 1: if (SelectedFlangedFitting is not null) FlangedFittingRows.Remove(SelectedFlangedFitting); break;
            case 2: if (SelectedSteelSocketFitting is not null) SteelSocketFittingRows.Remove(SelectedSteelSocketFitting); break;
            case 3: if (SelectedSteelFlangedFitting is not null) SteelFlangedFittingRows.Remove(SelectedSteelFlangedFitting); break;
            default: if (SelectedSocketFitting is not null) SocketFittingRows.Remove(SelectedSocketFitting); break;
        }
    }
    private void LoadDefaults() { Load(StraightPipeSpecTable.Default, BendFittingCatalog.Default); Joint.RestoreCommand.Execute(null); }
    private void Load(StraightPipeSpecTable straight, BendFittingCatalog fittings)
    {
        StraightRows.Clear(); SteelStraightRows.Clear();
        SocketFittingRows.Clear(); FlangedFittingRows.Clear(); SteelSocketFittingRows.Clear(); SteelFlangedFittingRows.Clear();
        // 등급으로 관종을 나눈다. 알 수 없는 등급은 주철관 탭에 남겨 사용자가 보게 한다(관종 축 도입 전과 같은 자리).
        var byMaterial = straight.Entries.ToLookup(x => PipeKindCatalog.MaterialOf(x.PipeKind) ?? PipeMaterial.DuctileIron);
        foreach (var group in byMaterial[PipeMaterial.DuctileIron].GroupBy(x => x.DiameterMm).OrderBy(x => x.Key))
        {
            var first = group.First();
            var row = new StraightPipeSpecRow
            {
                DiameterMm = group.Key, OuterDiameterMm = first.OuterDiameterMm,
                // 소켓 무게는 DN 단일값이라 OD처럼 첫 행에서 가져온다(docs/178 § A-2).
                SocketWeightKpKg = first.SocketWeightKpKg, SocketWeightTytonKg = first.SocketWeightTytonKg
            };
            foreach (var x in group) SetKindValues(row, x.PipeKind, x.ThicknessMm, x.UnitWeightKgPerM);
            StraightRows.Add(row);
        }
        foreach (var group in byMaterial[PipeMaterial.Steel].GroupBy(x => x.DiameterMm).OrderBy(x => x.Key))
        {
            var row = new SteelStraightPipeSpecRow { DiameterMm = group.Key, OuterDiameterMm = group.First().OuterDiameterMm };
            foreach (var x in group) row.SetKindValues(x.PipeKind, x.ThicknessMm, x.UnitWeightKgPerM);
            SteelStraightRows.Add(row);
        }
        // DN×각도 한 행 — A형/B형은 더 이상 별도 행이 아니다(2026-08-20, e·R·t는 형식 무관·s와 무게만 다름).
        // 형식(A/B)은 2026-09-03부터 프로젝트 전역 설정(_baseSettings.ActiveBendForm)이다. 행별 형식은 저장하지 않는다.
        // 강관 핸드북 데이터는 부도(각도) 순으로 만들어져 각도가 바깥이었다 → 네 표 모두 DN → 각도 순으로 넣는다(2026-09-22 사용자 지시).
        foreach (var x in fittings.Entries.OrderBy(x => x.DiameterMm).ThenBy(x => x.AngleDeg))
        {
            var row = NewFittingRow(x.Connection, x.Material, x.FittingClass); row.DiameterMm = x.DiameterMm; row.AngleDeg = x.AngleDeg; row.Form = _baseSettings.ActiveBendForm;
            row.WallThicknessMm = x.WallThicknessMm; row.LayingLengthMm = x.LayingLengthMm; row.CenterlineRadiusMm = x.CenterlineRadiusMm;
            row.WeightKpMechanicalKg = x.WeightKpMechanicalKg; row.WeightTytonKg = x.WeightTytonKg; row.WeightKg = x.WeightKg;
            (x.Material == PipeMaterial.Steel
                ? x.Connection == BendConnection.Flanged ? SteelFlangedFittingRows : SteelSocketFittingRows
                : x.Connection == BendConnection.Flanged ? FlangedFittingRows : SocketFittingRows).Add(row);
        }
    }
    private void Confirm()
    {
        Result = BuildResult();
        WarnOuterDiameterConflicts(Result);
        try { _revit.Run(() => _save.Execute(Result)); }
        catch (Exception ex) { _dialog.Warn("관로 규격 설정", $"저장에 실패했습니다.\n{UiErrors.Report("PipeSpecTable", ex)}"); }
        CloseAction?.Invoke();
    }
    private void Export()
    {
        var path = _fileDialog.SaveFile("관·곡관 설정 내보내기", "JSON 파일 (*.json)|*.json", "DHBIMWATER_PipeSettings.json");
        if (string.IsNullOrWhiteSpace(path)) return;
        var settings = BuildResult();
        WarnOuterDiameterConflicts(settings);
        try
        {
            _fileStore.Save(path, settings);
            _dialog.Info("관로 규격 설정", $"설정 파일을 내보냈습니다.\n{path}");
        }
        catch (Exception ex) { _dialog.Warn("관로 규격 설정", $"설정 파일을 내보내지 못했습니다.\n{UiErrors.Report("PipeSpecTable", ex)}"); }
    }
    private void Import()
    {
        var path = _fileDialog.OpenFile("관·곡관 설정 불러오기", "JSON 파일 (*.json)|*.json");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var settings = _fileStore.Load(path);
            _baseSettings = settings;
            Load(settings.StraightPipes, settings.Fittings);
            Joint.LoadSettings(settings);
            Result = null;
            _dialog.Info("관로 규격 설정", "설정 파일을 불러왔습니다. 내용을 확인한 뒤 [확인]을 눌러 현재 프로젝트에 저장하세요.");
        }
        catch (Exception ex) { _dialog.Warn("관로 규격 설정", $"설정 파일을 불러오지 못했습니다.\n{UiErrors.Report("PipeSpecTable", ex)}"); }
    }
    private BendSettings BuildResult()
    {
        var straight = new StraightPipeSpecTable(StraightRows.Where(x => x.DiameterMm > 0 && x.OuterDiameterMm > 0).SelectMany(x => x.ToSpecs()).Where(x => x.ThicknessMm > 0)
            .Concat(SteelStraightRows.Where(x => x.DiameterMm > 0 && x.OuterDiameterMm > 0).SelectMany(x => x.ToSpecs()))
            .ToList());
        var fittings = new BendFittingCatalog(SocketFittingRows.Concat(FlangedFittingRows).Concat(SteelSocketFittingRows).Concat(SteelFlangedFittingRows)
            .Where(x => x.DiameterMm > 0 && x.AngleDeg > 0 && x.LayingLengthMm > 0 && x.CenterlineRadiusMm > 0)
            .Select(x => new BendFittingEntry(x.DiameterMm, x.AngleDeg, x.Form, x.LayingLengthMm, x.CenterlineRadiusMm, x.WallThicknessMm, x.WeightKpMechanicalKg, x.WeightTytonKg,
                x.Material, x.Connection, x.FittingClass, x.WeightKg))
            .ToList());
        return Joint.BuildResult(_baseSettings with { StraightPipes = straight, Fittings = fittings });
    }
    private void WarnOuterDiameterConflicts(BendSettings settings)
    {
        var conflicts = settings.StraightPipes.FindOuterDiameterConflicts();
        if (conflicts.Count > 0)
            _dialog.Warn("직관 제원 확인", $"같은 관종·DN의 OD가 등급별로 다른 항목: {string.Join(", ", conflicts.Select(x => $"{PipeAlignmentParameters.Label(x.Material)} DN{x.DiameterMm:0.##}"))}");
    }
    private static void SetKindValues(StraightPipeSpecRow r, string kind, double thicknessMm, double unitWeightKgPerM)
    {
        switch (kind)
        {
            case PipeKindCatalog.Water1: r.Water1 = thicknessMm; r.WeightWater1 = unitWeightKgPerM; break;
            case PipeKindCatalog.Water2: r.Water2 = thicknessMm; r.WeightWater2 = unitWeightKgPerM; break;
            case PipeKindCatalog.Water3: r.Water3 = thicknessMm; r.WeightWater3 = unitWeightKgPerM; break;
            case PipeKindCatalog.Water4: r.Water4 = thicknessMm; r.WeightWater4 = unitWeightKgPerM; break;
            case PipeKindCatalog.Sewer1: r.Sewer1 = thicknessMm; r.WeightSewer1 = unitWeightKgPerM; break;
            case PipeKindCatalog.Sewer2: r.Sewer2 = thicknessMm; r.WeightSewer2 = unitWeightKgPerM; break;
            case PipeKindCatalog.Sewer3: r.Sewer3 = thicknessMm; r.WeightSewer3 = unitWeightKgPerM; break;
        }
    }
}
