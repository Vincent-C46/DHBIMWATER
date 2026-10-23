using System.Windows.Input;
using System.Windows.Threading;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.UI.Commands;
using DHBIMWATER.UI.Utilities;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>
/// 밸브실 창 [제원표] 버튼 동작(docs/312). 문서 저장본(없으면 내장 표준표)을 읽어 편집 창을 띄우고, 확인하면 문서에 저장한다.
/// <para>밸브실 창은 모델리스라 <see cref="RunInRevitContext"/>(ExternalEvent)로 읽기·저장을 돌린다. 선례: <see cref="ConcreteSettingsLauncher"/>.</para>
/// </summary>
public sealed class ValveRoomDimensionCatalogLauncher
{
    private const string Title = "밸브실 제원표";

    private readonly IValveRoomDimensionCatalogRepo? _repo;
    private readonly SaveValveRoomDimensionCatalogUseCase? _save;
    private readonly IDialogService _dialog;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public ValveRoomDimensionCatalogLauncher(IValveRoomDimensionCatalogRepo? repo, SaveValveRoomDimensionCatalogUseCase? save, IDialogService dialog)
    {
        _repo = repo;
        _save = save;
        _dialog = dialog;
        OpenCommand = new RelayCommand(_ => Open());
    }

    public ICommand OpenCommand { get; }
    /// <summary>View 코드비하인드가 연결한다. 확인으로 닫히면 true.</summary>
    public Func<ValveRoomDimensionCatalogViewModel, bool>? ShowDialog { get; set; }
    /// <summary>모델리스 창만 연결한다. null이면 지금 컨텍스트에서 바로 실행한다.</summary>
    public Action<Action>? RunInRevitContext { get; set; }
    /// <summary>창을 열 때 처음 보여줄 유형. 밸브실 창이 지금 유형을 넘긴다.</summary>
    public Func<string?>? InitialRoomKind { get; set; }
    /// <summary>저장 성공 뒤 호출. 밸브실 창이 새 표로 바꿔 끼운다.</summary>
    public Action<ValveRoomDimensionTable>? Saved { get; set; }

    /// <summary>문서 저장본, 없으면 내장 표준표. Revit API 컨텍스트에서 부른다.</summary>
    public ValveRoomDimensionTable LoadOrDefault() => _repo?.Load() ?? ValveRoomStandardDimensionCatalog.Default;

    public void Open()
    {
        if (RunInRevitContext is { } run)
            // 읽기는 API 컨텍스트에서, 창은 그 컨텍스트가 끝난 뒤 띄운다 — 모달 창이 ExternalEvent를 붙잡지 않게 한다.
            run(() =>
            {
                if (TryLoad() is { } loaded)
                    _dispatcher.BeginInvoke(() => ShowAndSave(loaded));
            });
        else if (TryLoad() is { } loaded)
            ShowAndSave(loaded);
    }

    private ValveRoomDimensionTable? TryLoad()
    {
        try { return LoadOrDefault(); }
        catch (Exception ex)
        {
            _dialog.Warn(Title, UiErrors.Report("ValveRoomDimensionCatalog", ex));
            return null;
        }
    }

    private void ShowAndSave(ValveRoomDimensionTable current)
    {
        var vm = new ValveRoomDimensionCatalogViewModel(current, InitialRoomKind?.Invoke());
        if (ShowDialog?.Invoke(vm) != true) return;

        var table = vm.ToTable();
        void Save()
        {
            try
            {
                _save?.Execute(table);
                Saved?.Invoke(table);
                _dialog.Info(Title, "제원표를 저장했습니다. 지금 입력칸은 그대로입니다 — 새 값을 쓰려면 [기본값]을 누르거나 위치 자동지정을 다시 하세요.");
            }
            catch (Exception ex) { _dialog.Warn(Title, UiErrors.Report("ValveRoomDimensionCatalog", ex)); }
        }

        if (RunInRevitContext is { } run) run(Save);
        else Save();
    }
}
