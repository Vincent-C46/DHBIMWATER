using System.Globalization;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.Base;

namespace DHBIMWATER.UI.ViewModels.Documentation;

/// <summary>수직분기 세트 하나의 펼침 축·간격. 간격은 실제 mm(심볼오프셋 값), 음수 = 반대 방향, 0 = 펼치지 않음, 상한 없음.</summary>
public sealed class JunctionOffsetSetViewModel : ViewModelBase
{
    private readonly Action _changed;
    private readonly Func<int> _viewScale;
    private JunctionExpandAxis _axis;
    private double _stepMm;
    private string _stepText;
    private bool _hasError;
    private bool _isSelected;

    public JunctionOffsetSetViewModel(long setId, int number, string members, JunctionExpandAxis axis,
        double stepMm, Func<int> viewScale, Action changed)
    {
        SetId = setId;
        Number = number;
        Members = members;
        _axis = axis;
        _stepMm = stepMm;
        _stepText = Format(stepMm);
        _viewScale = viewScale;
        _changed = changed;
    }

    public long SetId { get; }
    public int Number { get; }
    public string Label => $"세트 {Number}";
    // 세트마다 축 라디오 묶음이 따로 동작하도록 그룹 이름 분리
    public string AxisGroupName => $"JunctionSetAxis{SetId}";
    public string Members { get; }
    public JunctionExpandAxis Axis => _axis;
    public double StepMm => _stepMm;
    public bool IsX { get => _axis == JunctionExpandAxis.X; set { if (value) SetAxis(JunctionExpandAxis.X); } }
    public bool IsY { get => _axis == JunctionExpandAxis.Y; set { if (value) SetAxis(JunctionExpandAxis.Y); } }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    // 카드 테두리 표시용. 선택 원본은 JunctionDetailViewModel.SelectedSetId
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    // 입력 중 "-"·빈칸은 숫자 아님 → 이전 값 유지, 빨간 표시만
    public string StepText
    {
        get => _stepText;
        set
        {
            if (!SetProperty(ref _stepText, value)) return;
            if (!TryParse(value, out var step)) { HasError = true; return; }
            HasError = false;
            if (step == _stepMm) return;
            _stepMm = step;
            OnPropertyChanged(nameof(StepMm));
            OnPropertyChanged(nameof(DirectionText));
            _changed();
        }
    }

    public string DirectionText => _stepMm == 0 ? "0 — 펼치지 않음"
        : $"{(_axis == JunctionExpandAxis.X ? "X" : "Y")}{(_stepMm > 0 ? "+" : "−")} 방향 · {Math.Abs(_stepMm):0.#} mm씩 (도면 {Math.Abs(_stepMm) / _viewScale():0.#} mm)";

    public JunctionSetOffset ToOffset() => new(SetId, _axis, _stepMm);

    /// <summary>일괄 적용. 호출한 쪽이 미리보기를 한 번만 다시 그림.</summary>
    public void Apply(JunctionExpandAxis axis, double stepMm)
    {
        _axis = axis;
        _stepMm = stepMm;
        _stepText = Format(stepMm);
        HasError = false;
        foreach (var name in new[] { nameof(IsX), nameof(IsY), nameof(StepText), nameof(StepMm), nameof(DirectionText) })
            OnPropertyChanged(name);
    }

    public void RefreshScale() => OnPropertyChanged(nameof(DirectionText));

    public static bool TryParse(string? text, out double value)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        var ok = double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                 || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        return ok && double.IsFinite(value);
    }

    public static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private void SetAxis(JunctionExpandAxis axis)
    {
        if (_axis == axis) return;
        _axis = axis;
        OnPropertyChanged(nameof(IsX));
        OnPropertyChanged(nameof(IsY));
        OnPropertyChanged(nameof(DirectionText));
        _changed();
    }
}
