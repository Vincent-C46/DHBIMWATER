using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.UI.ViewModels.Modeling;

namespace DHBIMWATER.UI.Controls;

/// <summary>
/// 배치 제외 구간 미리보기(docs/219 § D, 목업 docs/218). 측점 축 하나에 그린다.
/// <para>전체(<see cref="IsDetail"/>=false): 선형 전체 + 구간 음영 + 곡관. 구간을 누르면 <see cref="SelectZoneCommand"/>로 행을 고른다.</para>
/// <para>확대(<see cref="IsDetail"/>=true): 선택 구간 ± 여유만 보여 직관 분절·경계 절단관·재시작 위치를 확인한다.</para>
/// </summary>
public sealed class AlignmentExclusionPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(ExclusionPreviewModel), typeof(AlignmentExclusionPreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsDetailProperty = DependencyProperty.Register(nameof(IsDetail), typeof(bool), typeof(AlignmentExclusionPreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectZoneCommandProperty = DependencyProperty.Register(nameof(SelectZoneCommand), typeof(ICommand), typeof(AlignmentExclusionPreviewControl));

    public ExclusionPreviewModel? Model { get => (ExclusionPreviewModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public bool IsDetail { get => (bool)GetValue(IsDetailProperty); set => SetValue(IsDetailProperty, value); }
    public ICommand? SelectZoneCommand { get => (ICommand?)GetValue(SelectZoneCommandProperty); set => SetValue(SelectZoneCommandProperty, value); }

    private static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x33)));
    private static readonly Brush Muted = Frozen(new SolidColorBrush(Color.FromRgb(0x52, 0x60, 0x6A)));
    private static readonly Brush Accent = Frozen(new SolidColorBrush(Color.FromRgb(0x4A, 0x6F, 0xA5)));
    private static readonly Brush PipeA = Frozen(new SolidColorBrush(Color.FromRgb(0x7F, 0xA3, 0xC9)));
    private static readonly Brush PipeB = Frozen(new SolidColorBrush(Color.FromRgb(0x5F, 0x88, 0xB6)));
    private static readonly Brush Cut = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)));
    private static readonly Brush Zone = Frozen(new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)));
    private static readonly Brush ZoneFill = Frozen(new SolidColorBrush(Color.FromArgb(0x22, 0xC0, 0x39, 0x2B)));
    private static readonly Brush ErrorFill = Frozen(new SolidColorBrush(Color.FromRgb(0xFD, 0xEC, 0xEA)));
    private static readonly Pen ZonePen = FrozenPen(Zone, 1);
    private static readonly Pen ZoneFocusPen = FrozenPen(Zone, 2.2);
    private static readonly Pen ZoneDashPen = FrozenPen(Zone, 1, DashStyles.Dash);
    private static readonly Pen HatchPen = FrozenPen(Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0xC0, 0x39, 0x2B))), 1);
    private static readonly Pen MutedPen = FrozenPen(Muted, 1);
    private static readonly Pen AxisPen = FrozenPen(PipeB, 5);
    private static readonly Pen WhitePen = FrozenPen(Brushes.White, 1);
    private static readonly Pen SkippedBendPen = FrozenPen(Muted, 1, DashStyles.Dot);
    private static readonly Typeface Face = new(new FontFamily("Segoe UI, Malgun Gothic"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldFace = new(new FontFamily("Segoe UI, Malgun Gothic"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private const double SidePad = 28;

    protected override void OnRender(DrawingContext dc)
    {
        // 배경을 칠하지 않으면 빈 영역 클릭이 적중하지 않는다(구간 선택 클릭).
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Model is not { } model || model.MaxStation - model.MinStation <= 1e-9) { Message(dc, "대상 선형이 없습니다 — 입력 파일을 추가하고 구간을 만드세요."); return; }
        if (IsDetail) RenderDetail(dc, model); else RenderOverview(dc, model);
    }

    private void RenderOverview(DrawingContext dc, ExclusionPreviewModel model)
    {
        var y = RenderSize.Height * 0.5;
        var x = Scale(model.MinStation, model.MaxStation);
        dc.DrawLine(AxisPen, new Point(x(model.MinStation), y), new Point(x(model.MaxStation), y));
        foreach (var zone in model.Zones)
        {
            var a = x(Math.Max(zone.StartStation, model.MinStation));
            var b = x(Math.Min(zone.EndStation, model.MaxStation));
            var width = Math.Max(b - a, 3);
            dc.DrawRectangle(zone.IsError ? ErrorFill : Brushes.White, zone.IsFocus ? ZoneFocusPen : zone.IsError ? ZoneDashPen : ZonePen, new Rect(a - 2, y - 14, width + 4, 28));
            dc.DrawRectangle(Zone, null, new Rect(a, y - 3, width, 6));
            Text(dc, zone.Label, new Point(a + width / 2, y - 30), Ink, 11, zone.IsFocus, TextAlignment.Center);
        }
        foreach (var bend in model.Bends) Diamond(dc, x(bend.Station), y, 6, bend.IsSkipped ? Brushes.White : Accent, bend.IsSkipped ? SkippedBendPen : null);
        var step = NiceStep(model.MaxStation - model.MinStation, RenderSize.Width);
        for (var s = Math.Ceiling(model.MinStation / step) * step; s <= model.MaxStation + 1e-6; s += step)
        {
            dc.DrawLine(MutedPen, new Point(x(s), y + 17), new Point(x(s), y + 21));
            Text(dc, StationTextFormatter.Format(s).Replace(".000", string.Empty, StringComparison.Ordinal), new Point(x(s), y + 22), Muted, 10, false, TextAlignment.Center);
        }
    }

    private void RenderDetail(DrawingContext dc, ExclusionPreviewModel model)
    {
        if (model.Focus is not { } focus) { Message(dc, "표에서 구간을 선택하세요."); return; }
        if (focus.IsError) { Message(dc, $"구간 오류로 미리보기 없음: {focus.Row.FirstMessage}", Zone); return; }

        var pad = Math.Max(model.IntervalM * 3.5, (focus.EndStation - focus.StartStation) * 0.8);
        var w0 = Math.Max(model.MinStation, focus.StartStation - pad);
        var w1 = Math.Min(model.MaxStation, focus.EndStation + pad);
        var x = Scale(w0, w1);
        var y = RenderSize.Height * 0.55;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.DrawLine(FrozenPen(Muted, 0.6, DashStyles.Dash), new Point(x(w0), y), new Point(x(w1), y));

        foreach (var zone in model.Zones.Where(z => !z.IsError && z.EndStation > w0 && z.StartStation < w1))
        {
            var rect = new Rect(new Point(x(Math.Max(zone.StartStation, w0)), y - 30), new Point(x(Math.Min(zone.EndStation, w1)), y + 30));
            dc.DrawRectangle(ZoneFill, zone.IsFocus ? ZoneFocusPen : ZonePen, rect);
            dc.PushClip(new RectangleGeometry(rect));
            for (var hx = rect.Left - rect.Height; hx < rect.Right; hx += 7) dc.DrawLine(HatchPen, new Point(hx, rect.Bottom), new Point(hx + rect.Height, rect.Top));
            dc.Pop();
            // 가운데(축)는 생략 곡관 표시 자리라 라벨은 위·아래로 비킨다.
            Text(dc, zone.Label, new Point(rect.Left + rect.Width / 2, y - 28), Zone, 12, true, TextAlignment.Center);
            Text(dc, "구체 · 내부 밸브 별도 프로젝트", new Point(rect.Left + rect.Width / 2, y + 14), Zone, 10, false, TextAlignment.Center);
        }

        foreach (var pipe in model.Pipes)
        {
            var low = Math.Min(pipe.StartStation, pipe.EndStation);
            var high = Math.Max(pipe.StartStation, pipe.EndStation);
            if (high < w0 || low > w1) continue;
            var a = x(Math.Max(low, w0)); var b = x(Math.Min(high, w1));
            var index = (int)Math.Round((low - model.MinStation) / Math.Max(model.IntervalM, 1e-6));
            dc.DrawRoundedRectangle(pipe.IsCut ? Cut : index % 2 == 0 ? PipeB : PipeA, null, new Rect(a + 0.6, y - 7, Math.Max(b - a - 1.2, 1), 14), 1.5, 1.5);
            if (pipe.IsCut && b - a > 34) Text(dc, $"{pipe.Length:0.000} m", new Point((a + b) / 2, y + 12), Cut, 10, false, TextAlignment.Center);
        }

        foreach (var bend in model.Bends.Where(b => b.Station >= w0 && b.Station <= w1))
        {
            Diamond(dc, x(bend.Station), y, 9, bend.IsSkipped ? Brushes.White : Accent, bend.IsSkipped ? SkippedBendPen : WhitePen);
            // 생략 곡관(◇)은 구간 안이라 라벨이 구체 라벨과 겹친다 — 범례로 대신한다.
            if (!bend.IsSkipped) Text(dc, $"곡관 {StationTextFormatter.Format(bend.Station)}", new Point(x(bend.Station), y + 26), Accent, 10, false, TextAlignment.Center);
        }

        // 진행 방향 앞쪽 경계에서 자르고 뒤쪽 경계에서 다시 시작한다. 역방향 선형이면 반대다.
        var (cutStation, restartStation) = model.IsDescending ? (focus.EndStation, focus.StartStation) : (focus.StartStation, focus.EndStation);
        Boundary(dc, x(cutStation), y, $"✂ 절단 {StationTextFormatter.Format(cutStation)}", cutStation < restartStation);
        Boundary(dc, x(restartStation), y, $"▶ 재시작 {StationTextFormatter.Format(restartStation)}", restartStation < cutStation);
        Text(dc, StationTextFormatter.Format(w0), new Point(x(w0), RenderSize.Height - 14), Muted, 10, false, TextAlignment.Left);
        Text(dc, StationTextFormatter.Format(w1), new Point(x(w1), RenderSize.Height - 14), Muted, 10, false, TextAlignment.Right);
        dc.Pop();
    }

    private void Boundary(DrawingContext dc, double x, double y, string label, bool alignRight)
    {
        dc.DrawLine(ZoneDashPen, new Point(x, y - 46), new Point(x, y + 38));
        Text(dc, label, new Point(alignRight ? x - 5 : x + 5, y - 60), Ink, 11, true, alignRight ? TextAlignment.Right : TextAlignment.Left);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsDetail || Model is not { } model || SelectZoneCommand is not { } command) return;
        var x = Scale(model.MinStation, model.MaxStation);
        var px = e.GetPosition(this).X;
        var hit = model.Zones.FirstOrDefault(z => px >= x(z.StartStation) - 4 && px <= x(z.EndStation) + 4);
        if (hit is not null && command.CanExecute(hit.Row)) { command.Execute(hit.Row); e.Handled = true; }
    }

    private Func<double, double> Scale(double from, double to)
    {
        var width = Math.Max(RenderSize.Width - SidePad * 2, 1);
        var span = Math.Max(to - from, 1e-9);
        return station => SidePad + (station - from) / span * width;
    }

    private static double NiceStep(double span, double width)
    {
        var target = span / Math.Max(width / 90d, 1d);
        foreach (var step in new[] { 10d, 20d, 50d, 100d, 200d, 500d, 1000d, 2000d, 5000d })
            if (step >= target) return step;
        return 10000d;
    }

    private void Message(DrawingContext dc, string text, Brush? brush = null)
        => Text(dc, text, new Point(RenderSize.Width / 2, RenderSize.Height / 2 - 8), brush ?? Muted, 12, false, TextAlignment.Center);

    private void Text(DrawingContext dc, string text, Point origin, Brush brush, double size, bool bold, TextAlignment alignment)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? BoldFace : Face, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip) { TextAlignment = alignment };
        dc.DrawText(formatted, origin);
    }

    private static void Diamond(DrawingContext dc, double x, double y, double r, Brush fill, Pen? pen)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(x, y - r), true, true);
            g.PolyLineTo(new[] { new Point(x + r, y), new Point(x, y + r), new Point(x - r, y) }, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(fill, pen, geometry);
    }

    private static Brush Frozen(SolidColorBrush brush) { brush.Freeze(); return brush; }
    private static Pen FrozenPen(Brush brush, double thickness, DashStyle? dash = null)
    {
        var pen = new Pen(brush, thickness) { DashStyle = dash ?? DashStyles.Solid };
        pen.Freeze();
        return pen;
    }
}
