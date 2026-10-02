using System.Windows.Input;
using System.Windows.Threading;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.UI.Commands;
using DHBIMWATER.UI.Utilities;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>
/// 구조물 창(펌프장·배수지·밸브실)의 [콘크리트 강도] 버튼 동작(docs/249 § E). 문서 설정을 읽어 공용 창을 띄우고, 확인하면 저장한다.
/// <para>펌프장·배수지 창은 IExternalCommand 안의 모달이라 바로 Revit API를 부른다.
/// 밸브실 창은 모델리스라 <see cref="RunInRevitContext"/>(ExternalEvent)로 읽기·저장을 돌린다.</para>
/// </summary>
public sealed class ConcreteSettingsLauncher
{
    private const string Title = "콘크리트 강도";

    private readonly IConcreteSettingsRepo? _repo;
    private readonly SaveConcreteSettingsUseCase? _save;
    private readonly IDialogService _dialog;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public ConcreteSettingsLauncher(IConcreteSettingsRepo? repo, SaveConcreteSettingsUseCase? save, IDialogService dialog)
    {
        _repo = repo;
        _save = save;
        _dialog = dialog;
        OpenCommand = new RelayCommand(_ => Open());
    }

    public ICommand OpenCommand { get; }
    /// <summary>View 코드비하인드가 연결한다. 확인으로 닫히면 true.</summary>
    public Func<ConcreteSettingsViewModel, bool>? ShowDialog { get; set; }
    /// <summary>모델리스 창(밸브실)만 연결한다. null이면 지금 컨텍스트에서 바로 실행한다.</summary>
    public Action<Action>? RunInRevitContext { get; set; }

    /// <summary>문서 저장값, 없으면 기본값. Revit API 컨텍스트에서 부른다.</summary>
    public ConcreteSettings LoadOrDefault() => _repo?.Load() ?? ConcreteSettings.Default;

    public void Open()
    {
        if (RunInRevitContext is { } run)
            // 읽기는 API 컨텍스트에서 하고, 창은 그 컨텍스트가 끝난 뒤 띄운다 — 모달 창이 ExternalEvent를 붙잡지 않게 한다.
            run(() =>
            {
                if (TryLoad() is { } loaded)
                    _dispatcher.BeginInvoke(() => ShowAndSave(loaded));
            });
        else if (TryLoad() is { } loaded)
            ShowAndSave(loaded);
    }

    private ConcreteSettings? TryLoad()
    {
        try { return LoadOrDefault(); }
        catch (Exception ex)
        {
            _dialog.Warn(Title, UiErrors.Report("ConcreteSettings", ex));
            return null;
        }
    }

    private void ShowAndSave(ConcreteSettings current)
    {
        var vm = new ConcreteSettingsViewModel(current);
        if (ShowDialog?.Invoke(vm) != true || _save == null) return;

        var settings = vm.ToSettings();
        void Save()
        {
            try { _save.Execute(settings); }
            catch (Exception ex) { _dialog.Warn(Title, UiErrors.Report("ConcreteSettings", ex)); }
        }

        if (RunInRevitContext is { } run) run(Save);
        else Save();
    }
}
