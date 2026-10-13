using System.Globalization;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.Base;

namespace DHBIMWATER.UI.ViewModels.Documentation;

/// <summary>수직분기 세트 하나의 펼침 축·간격. 간격은 도면 mm, 음수 = 반대 방향, 0 = 펼치지 않음, 상한 없음.</summary>
public sealed class JunctionOffsetSetViewModel : ViewModelBase
{
    private readonly Action _changed;
    private readonly Func<int> _viewScale;
    private JunctionExpandAxis _axis;
    private double _stepPaperMm;
    private string _stepText;
    private bool _hasError;

    public JunctionOffsetSetViewModel(long setId, int number, string members, JunctionExpandAxis axis,
        double stepPaperMm, Func<int> viewScale, Action changed)
    {
        SetId = setId;
        Number = number;
        Members = members;
        _axis = axis;
        _stepPaperMm = stepPaperMm;
        _stepText = Format(stepPaperMm);
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
    public double StepPaperMm => _stepPaperMm;
    public bool IsX { get => _axis == JunctionExpandAxis.X; set { if (value) SetAxis(JunctionExpandAxis.X); } }
    public bool IsY { get => _axis == JunctionExpandAxis.Y; set { if (value) SetAxis(JunctionExpandAxis.Y); } }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }

    // 입력 중 "-"·빈칸은 숫자 아님 → 이전 값 유지, 빨간 표시만
    public string StepText
    {
        get => _stepText;
        set
        {
            if (!SetProperty(ref _stepText, value)) return;
            if (!TryParse(value, out var step)) { HasError = true; return; }
            HasError = false;
            if (step == _stepPaperMm) return;
            _stepPaperMm = step;
            OnPropertyChanged(nameof(StepPaperMm));
            OnPropertyChanged(nameof(DirectionText));
            _changed();
        }
    }

    public string DirectionText => _stepPaperMm == 0 ? "0 — 펼치지 않음"
        : $"{(_axis == JunctionExpandAxis.X ? "X" : "Y")}{(_stepPaperMm > 0 ? "+" : "−")} 방향 · 실제 {Math.Abs(_stepPaperMm) * _viewScale():0.#} mm씩";

    public JunctionSetOffset ToOffset() => new(SetId, _axis, _stepPaperMm);

    /// <summary>일괄 적용. 호출한 쪽이 미리보기를 한 번만 다시 그림.</summary>
    public void Apply(JunctionExpandAxis axis, double stepPaperMm)
    {
        _axis = axis;
        _stepPaperMm = stepPaperMm;
        _stepText = Format(stepPaperMm);
        HasError = false;
        foreach (var name in new[] { nameof(IsX), nameof(IsY), nameof(StepText), nameof(StepPaperMm), nameof(DirectionText) })
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
