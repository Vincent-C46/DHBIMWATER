using DHBIMWATER.UI.Base;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>밸브 목록 항목. Name은 "패밀리명 : 타입명", DnMm은 유형명에서 파싱한 호칭지름이다.</summary>
public sealed class FittingFamilyItem(string name, double? dnMm) : ViewModelBase
{
    private bool _isDiameterMatched;

    public string Name { get; } = name;
    public double? DnMm { get; } = dnMm;
    /// <summary>현재 관경과 DN 일치 여부. 관경 변경 시 갱신한다.</summary>
    public bool IsDiameterMatched { get => _isDiameterMatched; set => SetProperty(ref _isDiameterMatched, value); }
}
