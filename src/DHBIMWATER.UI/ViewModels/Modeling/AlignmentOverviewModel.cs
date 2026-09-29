using System.Windows;

namespace DHBIMWATER.UI.ViewModels.Modeling;

/// <summary>선형 전체 개요도의 선형 1개(docs/220 § C). <see cref="Points"/>는 진행 방향(반전 반영) 순서의 평면 좌표(m)다.</summary>
public sealed record AlignmentOverviewLine(AlignmentDirectionRow Row, IReadOnlyList<Point> Points, bool IsSelected, string StartStationText);

/// <summary>개요도 위 배치 제외 구간 띠. <see cref="Points"/>는 구간이 걸친 선형 일부의 평면 좌표다.</summary>
public sealed record AlignmentOverviewZone(AlignmentExclusionRow Row, AlignmentDirectionRow Line, IReadOnlyList<Point> Points, string Label, bool IsFocus);

public sealed record AlignmentOverviewModel(IReadOnlyList<AlignmentOverviewLine> Lines, IReadOnlyList<AlignmentOverviewZone> Zones)
{
    /// <summary>선형 구성(개수·좌표 범위)이 같으면 줌·이동을 유지하고, 바뀌면 전체 보기로 다시 맞춘다.</summary>
    public string LayoutKey => Lines.Count == 0 ? string.Empty
        : $"{Lines.Count}|{Lines.Min(l => l.Points.Min(p => p.X)):0.###}|{Lines.Min(l => l.Points.Min(p => p.Y)):0.###}|{Lines.Max(l => l.Points.Max(p => p.X)):0.###}|{Lines.Max(l => l.Points.Max(p => p.Y)):0.###}";
}

/// <summary>개요도에서 선형을 클릭했을 때 VM에 넘기는 값. <see cref="Additive"/>는 Ctrl 클릭(추가·해제).</summary>
public sealed record AlignmentOverviewSelection(AlignmentDirectionRow Row, bool Additive);
