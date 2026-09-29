using DHBIMWATER.UI.Base;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.UI.ViewModels.Modeling;

public sealed record DiameterMappingOption(string Display, double? DiameterMm);

public sealed class DiameterMappingRow : ViewModelBase
{
    private double? _diameterMm;
    private string _pipeKind = string.Empty;
    private IReadOnlyList<DiameterMappingOption> _diameterOptions = Array.Empty<DiameterMappingOption>();
    private DiameterMappingOption? _selectedDiameterOption;
    private double? _outerDiameterMm, _thicknessMm;
    private string _status = string.Empty;
    private bool _isSelected, _isHighlighted;

    public required string RawValue { get; init; }
    public required string KindRawValue { get; init; }
    public string MappingKey => DiameterMappingKey.Of(RawValue, KindRawValue);
    public required string FileName { get; init; }
    public required string FileKey { get; init; }
    public required int Count { get; init; }
    public double? AutoDiameterMm { get; init; }
    public string AutoDiameterDisplay => AutoDiameterMm is > 0 ? $"{AutoDiameterMm:0.##}" : "(실패)";
    public double? DiameterMm
    {
        get => _diameterMm;
        set
        {
            if (!SetProperty(ref _diameterMm, value)) return;
            IsUserModified = true;
            SyncSelectedDiameterOption();
            Changed?.Invoke();
        }
    }
    public DiameterMappingOption? SelectedDiameterOption
    {
        get => _selectedDiameterOption;
        set
        {
            if (!SetProperty(ref _selectedDiameterOption, value)) return;
            // 콤보 선택은 사용자 입력이다. 일괄 적용(DiameterMm 직접 설정)과 구분해 표시한다.
            IsDiameterUserModified = true;
            DiameterMm = value?.DiameterMm;
        }
    }
    public string PipeKind
    {
        get => _pipeKind;
        set
        {
            if (!SetProperty(ref _pipeKind, value)) return;
            IsUserModified = true;
            IsDefaultPipeKind = false;
            Changed?.Invoke();
        }
    }
    public bool IsUserModified { get; private set; }
    /// <summary>사용자가 확정 DN 콤보를 직접 고른 행. 일괄 적용이 이 행의 DN은 덮어쓰지 않는다(2026-09-23 사용자 지시).
    /// <see cref="IsUserModified"/>는 등급·일괄 적용 변경에도 켜지므로 따로 둔다.</summary>
    public bool IsDiameterUserModified { get; private set; }
    public bool IsDefaultPipeKind { get; private set; }
    /// <summary>등급 없이 관종만 온 강관을 DN으로 기본 등급 잡은 행(docs/212 § A-3). <see cref="IsDefaultPipeKind"/>(상수 1종관 폴백)과 구분한다.</summary>
    public bool IsSteelDefault { get; private set; }
    /// <summary>등급 콤보가 "(관종 필드 사용)"일 때 Loader가 실제로 잡을 등급·관종(docs/212 § A-4). 행이 다시 만들어질 때 채운다.</summary>
    public string? FieldGrade { get; set; }
    public PipeMaterial? FieldMaterial { get; set; }

    /// <summary>배치에 실제로 쓰일 관종. 등급이 확정돼 있으면 그 등급의 관종, "(관종 필드 사용)"이면 <see cref="FieldMaterial"/>.</summary>
    public PipeMaterial? EffectiveMaterial(string useKindFieldOption)
        => PipeKind == useKindFieldOption ? FieldMaterial : PipeKindCatalog.MaterialOf(PipeKind);

    /// <summary>규격표 조회에 쓸 등급. "(관종 필드 사용)"이면 필드에서 해석한 등급(docs/212 § C).</summary>
    public string? LookupGrade(string useKindFieldOption)
        => PipeKind == useKindFieldOption ? FieldGrade : PipeKind;
    public double? OuterDiameterMm { get => _outerDiameterMm; private set => SetProperty(ref _outerDiameterMm, value); }
    public double? ThicknessMm { get => _thicknessMm; private set => SetProperty(ref _thicknessMm, value); }
    public string SpecificationDisplay => OuterDiameterMm is > 0 && ThicknessMm is > 0 ? $"{OuterDiameterMm:0.##}/{ThicknessMm:0.##}" : "-";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public bool IsHighlighted { get => _isHighlighted; set => SetProperty(ref _isHighlighted, value); }
    public Action? Changed { get; init; }

    public void InitializeValues(IReadOnlyList<DiameterMappingOption> diameterOptions, double? diameterMm, string pipeKind, bool isUserModified, bool isDefaultPipeKind = false, bool isSteelDefault = false, bool isDiameterUserModified = false)
    {
        IsDiameterUserModified = isDiameterUserModified;
        _diameterOptions = diameterOptions;
        _diameterMm = diameterMm;
        _pipeKind = pipeKind;
        _selectedDiameterOption = FindDiameterOption(diameterMm);
        IsUserModified = isUserModified;
        IsDefaultPipeKind = isDefaultPipeKind;
        IsSteelDefault = isSteelDefault;
    }

    /// <summary>DN 콤보 선택지를 관종에 맞춰 바꾼다(docs/212 § D-7). 현재 값은 새 목록에 없어도 그대로 둔다(사용자 입력 존중).</summary>
    public IReadOnlyList<DiameterMappingOption> DiameterOptions
    {
        get => _diameterOptions;
        set
        {
            if (ReferenceEquals(_diameterOptions, value)) return;
            _diameterOptions = value;
            OnPropertyChanged();
            SyncSelectedDiameterOption();
        }
    }

    public void SetPreview(double? outerDiameterMm, double? thicknessMm, string status)
    {
        OuterDiameterMm = outerDiameterMm;
        ThicknessMm = thicknessMm;
        OnPropertyChanged(nameof(SpecificationDisplay));
        Status = status;
    }

    private void SyncSelectedDiameterOption()
    {
        var option = FindDiameterOption(DiameterMm);
        SetProperty(ref _selectedDiameterOption, option, nameof(SelectedDiameterOption));
    }

    private DiameterMappingOption? FindDiameterOption(double? diameterMm)
        => _diameterOptions.FirstOrDefault(x => Nullable.Equals(x.DiameterMm, diameterMm));
}
