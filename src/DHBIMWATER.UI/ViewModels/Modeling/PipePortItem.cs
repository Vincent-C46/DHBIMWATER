using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.Base;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>IN/OUT 포트 1개의 편집 항목(docs/172 § C-1). Core <see cref="PipePort"/>를 감싸며 이격만 바뀐다.</summary>
public sealed class PipePortItem(PipePort port) : ViewModelBase
{
    private double _offsetMm = port.OffsetMm;
    private bool _canRemove;

    public Guid Id { get; } = port.Id;
    public PortFlow Flow { get; } = port.Flow;
    public OutlineSide Side { get; } = port.Side;
    public string Label { get; } = port.Label;

    /// <summary>사용자가 직접 입력했는지. true면 외곽 재피킹 시 자동값으로 덮지 않는다(종전 _inOffsetUserEdited 규칙).</summary>
    public bool UserEdited { get; set; }

    /// <summary>기본 포트(IN·OUT)는 지울 수 없고 추가된 포트만 지울 수 있다(사용자 확정 2026-09-15). VM이 갱신한다.</summary>
    public bool CanRemove { get => _canRemove; set => SetProperty(ref _canRemove, value); }

    /// <summary>OutlineArrowItem.Color와 같은 값 — 입력칸 라벨과 화살표 색을 맞춘다.</summary>
    public string Color => Flow == PortFlow.In ? "#1E8449" : "#B03A2E";

    /// <summary>세터의 예외는 XAML ValidatesOnExceptions가 의존한다(DiameterMm 세터와 같은 규약). 없애지 말 것.</summary>
    public double OffsetMm
    {
        get => _offsetMm;
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "이격은 유효한 숫자여야 합니다.");
            if (SetProperty(ref _offsetMm, value)) UserEdited = true;
        }
    }

    /// <summary>자동값 대입. UserEdited를 세우지 않는다(ApplyOutline 전용).</summary>
    public void SetOffsetSilently(double value)
    {
        _offsetMm = value;
        OnPropertyChanged(nameof(OffsetMm));
    }

    public PipePort ToPort() => new(Id, Flow, Side, _offsetMm, Label);
}
