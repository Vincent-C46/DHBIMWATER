using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>공용 콘크리트 강도 설정창의 편집 사본(docs/249 § E). 저장은 호출한 <see cref="ConcreteSettingsLauncher"/>가 한다.</summary>
public sealed class ConcreteSettingsViewModel : ViewModelBase
{
    public ConcreteSettingsViewModel(ConcreteSettings settings)
    {
        Rows = new ObservableCollection<ConcreteMaterialRowViewModel>(ConcreteSettings.Members.Select(m => CreateRow(m, settings.Get(m))));
        ResetCommand = new RelayCommand(_ => Apply(ConcreteSettings.Default));
        ConfirmCommand = new RelayCommand(_ => CloseAction?.Invoke(true));
        CancelCommand = new RelayCommand(_ => CloseAction?.Invoke(false));
    }

    public ObservableCollection<ConcreteMaterialRowViewModel> Rows { get; }
    public ICommand ResetCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }
    /// <summary>View가 연결한다. 인자는 DialogResult.</summary>
    public Action<bool>? CloseAction { get; set; }

    public ConcreteSettings ToSettings()
    {
        var result = ConcreteSettings.Default;
        foreach (var row in Rows) result = result.With(row.Member, row.ToConcreteSpec());
        return result;
    }

    private void Apply(ConcreteSettings settings)
    {
        foreach (var row in Rows)
        {
            var spec = settings.Get(row.Member);
            row.MaxAggregateSize = spec.MaxAggregateSize;
            row.CompressiveStrength = spec.CompressiveStrength;
            row.Slump = spec.Slump;
        }
    }

    private static ConcreteMaterialRowViewModel CreateRow(ConcreteMember member, ConcreteSpec spec)
        => new(ConcreteSettings.DisplayName(member), spec.CompressiveStrength)
        {
            Member = member,
            MaxAggregateSize = spec.MaxAggregateSize,
            Slump = spec.Slump,
        };
}
