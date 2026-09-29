using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DHBIMWATER.UI.ViewModels.Modeling;

namespace DHBIMWATER.UI.Controls;

/// <summary>
/// 선형 전체 개요도(docs/220 § C, 목업 docs/220). 모든 선형을 평면(북쪽 위)으로 그리고 진행 방향 화살표·시점·종점,
/// 배치 제외 구간 띠를 올린다.
/// <para>조작 — 휠: 커서 기준 줌 · 드래그(좌/가운데): 이동 · 더블클릭: 전체 보기 · 클릭: 선형 선택(Ctrl 추가) · 띠 클릭: 구간 선택.</para>
/// <para>칸 크기는 XAML에서 고정한다. 선형 구성이 바뀌면 전체가 여백 안에 들어오게 다시 맞추고, 그 외 갱신(선택·반전·구간)은 줌·이동을 유지한다.</para>
/// </summary>
public sealed class AlignmentOverviewControl : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(AlignmentOverviewModel), typeof(AlignmentOverviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((AlignmentOverviewControl)d).OnModelChanged()));
    public static readonly DependencyProperty SelectLineCommandProperty = DependencyProperty.Register(nameof(SelectLineCommand), typeof(ICommand), typeof(AlignmentOverviewControl));
    public static readonly DependencyProperty SelectZoneCommandProperty = DependencyProperty.Register(nameof(SelectZoneCommand), typeof(ICommand), typeof(AlignmentOverviewControl));

    public AlignmentOverviewModel? Model { get => (AlignmentOverviewModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    /// <summary>매개변수 <see cref="AlignmentOverviewSelection"/>.</summary>
    public ICommand? SelectLineCommand { get => (ICommand?)GetValue(SelectLineCommandProperty); set => SetValue(SelectLineCommandProperty, value); }
    /// <summary>매개변수 <see cref="AlignmentExclusionRow"/>.</summary>
    public ICommand? SelectZoneCommand { get => (ICommand?)GetValue(SelectZoneCommandProperty); set => SetValue(SelectZoneCommandProperty, value); }

    private static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFB, 0xFC, 0xFE)));
    private static readonly Brush Muted = Frozen(new SolidColorBrush(Color.FromRgb(0x8C, 0x99, 0xA5)));
    private static readonly Brush Accent = Frozen(new SolidColorBrush(Color.FromRgb(0x4A, 0x6F, 0xA5)));
    private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromRgb(0x9D, 0xAF, 0xC0)));
    private static readonly Brush ZoneBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)));
    private static readonly Pen GridPen = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF6))), 1);
    private static readonly Pen LinePen = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xB7, 0xC4, 0xD0))), 2.2);
    private static readonly Pen SelectedLinePen = FrozenPen(Accent, 3.2);
    private static readonly Pen ZonePen = FrozenPen(Frozen(new SolidColorBrush(Color.FromArgb(0x73, 0xC0, 0x39, 0x2B))), 11, PenLineCap.Flat);
    private static readonly Pen ZoneFocusPen = FrozenPen(Frozen(new SolidColorBrush(Color.FromArgb(0xD9, 0xC0, 0x39, 0x2B))), 12, PenLineCap.Flat);
    private static readonly Pen ScalePen = FrozenPen(Muted, 2);
    private static readonly Pen WhitePen = FrozenPen(Brushes.White, 1.5);
    private static readonly Typeface Face = new(new FontFamily("Segoe UI, Malgun Gothic"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldFace = new(new FontFamily("Segoe UI, Malgun Gothic"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private const double Pad = 36;          // 전체 보기 여백 — 라벨·시종점 기호가 칸 밖으로 나가지 않게
    private const double ArrowSpacing = 70; // 화면 px
    private const double HitTolerance = 7;
    private const double DragThreshold = 3;
    private const double MaxZoomFactor = 400; // 전체 보기 배율 대비

    private double _scale = 1, _fitScale = 1, _centerX, _centerY;
    private bool _hasView;
    private string _layoutKey = string.Empty;
    private Point? _pressPoint;
    private Point _lastPoint;
    private bool _dragging;

    public AlignmentOverviewControl()
    {
        ClipToBounds = true;
        Focusable = false;
        SizeChanged += (_, e) => { if (!_hasView || e.PreviousSize.Width <= 0 || e.PreviousSize.Height <= 0) FitAll(); };
    }

    private void OnModelChanged()
    {
        var key = Model?.LayoutKey ?? string.Empty;
        if (key == _layoutKey && _hasView) return;
        _layoutKey = key;
        FitAll();
    }

    /// <summary>모든 선형이 여백 안에 들어오게 맞춘다(더블클릭·선형 구성 변경).</summary>
    public void FitAll()
    {
        ComputeFit();
        InvalidateVisual();
    }

    private void ComputeFit()
    {
        _hasView = false;
        if (Model is not { Lines.Count: > 0 } model || ActualWidth <= 0 || ActualHeight <= 0) return;
        var points = model.Lines.SelectMany(x => x.Points).ToList();
        var (minX, maxX, minY, maxY) = (points.Min(p => p.X), points.Max(p => p.X), points.Min(p => p.Y), points.Max(p => p.Y));
        var width = Math.Max(maxX - minX, 1e-3); var height = Math.Max(maxY - minY, 1e-3);
        _fitScale = Math.Min(Math.Max(ActualWidth - 2 * Pad, 10) / width, Math.Max(ActualHeight - 2 * Pad, 10) / height);
        _scale = _fitScale;
        _centerX = (minX + maxX) / 2; _centerY = (minY + maxY) / 2;
        _hasView = true;
    }

    private Point ToScreen(Point world) => new(ActualWidth / 2 + (world.X - _centerX) * _scale, ActualHeight / 2 - (world.Y - _centerY) * _scale);
    private Point ToWorld(Point screen) => new(_centerX + (screen.X - ActualWidth / 2) / _scale, _centerY - (screen.Y - ActualHeight / 2) / _scale);

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        // 배경을 칠해야 빈 곳에서도 휠·드래그가 적중한다.
        dc.DrawRectangle(BackgroundBrush, null, bounds);
        for (var x = 0d; x <= bounds.Width; x += 40) dc.DrawLine(GridPen, new Point(x, 0), new Point(x, bounds.Height));
        for (var y = 0d; y <= bounds.Height; y += 40) dc.DrawLine(GridPen, new Point(0, y), new Point(bounds.Width, y));
        if (Model is not { Lines.Count: > 0 } model)
        {
            DrawText(dc, "선형이 없습니다 — [입력·기준점] 탭에서 파일을 추가하세요.", new Point(bounds.Width / 2, bounds.Height / 2 - 8), 12, Muted, false, TextAlignment.Center);
            return;
        }
        if (!_hasView) ComputeFit();

        // 선택 안 된 선형 먼저, 선택 선형을 위에.
        foreach (var line in model.Lines.OrderBy(x => x.IsSelected))
        {
            var screen = line.Points.Select(ToScreen).ToList();
            dc.DrawGeometry(null, line.IsSelected ? SelectedLinePen : LinePen, Polyline(screen));
            DrawDirection(dc, screen, line.IsSelected ? Accent : Dim);
        }
        foreach (var zone in model.Zones.Where(x => x.Points.Count >= 2))
            dc.DrawGeometry(null, zone.IsFocus ? ZoneFocusPen : ZonePen, Polyline(zone.Points.Select(ToScreen).ToList()));

        // 라벨은 맨 위 — 선택 선형의 시점 측점, 구간명.
        foreach (var line in model.Lines.Where(x => x.IsSelected))
            DrawClampedText(dc, line.StartStationText, ToScreen(line.Points[0]) + new Vector(8, 6), 10.5, Accent, true);
        foreach (var zone in model.Zones.Where(x => x.Points.Count >= 2 && (x.IsFocus || model.Lines.Any(l => l.IsSelected && ReferenceEquals(l.Row, x.Line)))))
            DrawClampedText(dc, zone.Label, ToScreen(zone.Points[zone.Points.Count / 2]) + new Vector(8, -20), 11, ZoneBrush, true);

        DrawNorth(dc, bounds);
        DrawScaleBar(dc, bounds);
    }

    /// <summary>진행 방향 화살표(화면 간격 일정) + 시점 ● + 종점 ▶.</summary>
    private void DrawDirection(DrawingContext dc, IReadOnlyList<Point> screen, Brush brush)
    {
        if (screen.Count < 2) return;
        var next = ArrowSpacing / 2;
        var walked = 0d;
        for (var i = 1; i < screen.Count; i++)
        {
            var segment = screen[i] - screen[i - 1];
            var length = segment.Length;
            if (length <= 1e-6) continue;
            while (next <= walked + length)
            {
                var t = (next - walked) / length;
                var p = screen[i - 1] + segment * t;
                if (IsInside(p)) dc.DrawGeometry(brush, null, Triangle(p, segment, 5, 4.5));
                next += ArrowSpacing;
            }
            walked += length;
        }
        dc.DrawEllipse(brush, WhitePen, screen[0], 4.5, 4.5);
        var last = LastDirection(screen);
        if (last.Length > 1e-6) dc.DrawGeometry(brush, WhitePen, Triangle(screen[^1], last, 7, 6));
    }

    private static Vector LastDirection(IReadOnlyList<Point> screen)
    {
        for (var i = screen.Count - 1; i > 0; i--)
        {
            var v = screen[i] - screen[i - 1];
            if (v.Length > 1e-6) return v;
        }
        return new Vector();
    }

    private static Geometry Triangle(Point tip, Vector direction, double half, double width)
    {
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(tip + direction * half, true, true);
            ctx.LineTo(tip - direction * half + normal * width, true, false);
            ctx.LineTo(tip - direction * half - normal * width, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry Polyline(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    private void DrawNorth(DrawingContext dc, Rect bounds)
    {
        var c = new Point(bounds.Width - 20, 22);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(c + new Vector(0, -11), true, true);
            ctx.LineTo(c + new Vector(6, 6), true, false);
            ctx.LineTo(c + new Vector(0, 2), true, false);
            ctx.LineTo(c + new Vector(-6, 6), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(Muted, null, geometry);
        DrawText(dc, "N", c + new Vector(0, 8), 10, Muted, false, TextAlignment.Center);
    }

    /// <summary>현재 배율에 맞는 축척 막대(60px 이상이 되는 가장 작은 1·2·5 단위).</summary>
    private void DrawScaleBar(DrawingContext dc, Rect bounds)
    {
        var meters = 1d;
        foreach (var candidate in Enumerable.Range(-1, 7).SelectMany(e => new[] { 1d, 2d, 5d }.Select(m => m * Math.Pow(10, e))))
        {
            meters = candidate;
            if (candidate * _scale >= 60) break;
        }
        var start = new Point(14, bounds.Height - 14);
        dc.DrawLine(ScalePen, start, start + new Vector(meters * _scale, 0));
        DrawText(dc, meters >= 1 ? $"{meters:0.##} m" : $"{meters * 100:0} cm", start + new Vector(meters * _scale + 5, -7), 10, Muted, false, TextAlignment.Left);
    }

    private bool IsInside(Point p) => p.X >= -10 && p.Y >= -10 && p.X <= ActualWidth + 10 && p.Y <= ActualHeight + 10;

    private void DrawText(DrawingContext dc, string text, Point origin, double size, Brush brush, bool bold, TextAlignment alignment)
        => dc.DrawText(Format(text, size, brush, bold, alignment), origin);

    /// <summary>라벨이 칸 밖으로 나가지 않게 당긴다(고정 크기 칸, docs/220 § C).</summary>
    private void DrawClampedText(DrawingContext dc, string text, Point origin, double size, Brush brush, bool bold)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsInside(origin)) return;
        var formatted = Format(text, size, brush, bold, TextAlignment.Left);
        var x = Math.Clamp(origin.X, 4, Math.Max(4, ActualWidth - formatted.Width - 4));
        var y = Math.Clamp(origin.Y, 4, Math.Max(4, ActualHeight - formatted.Height - 4));
        dc.DrawText(formatted, new Point(x, y));
    }

    private FormattedText Format(string text, double size, Brush brush, bool bold, TextAlignment alignment)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? BoldFace : Face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip) { TextAlignment = alignment };

    // ---------- 마우스 ----------

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Model is not { Lines.Count: > 0 }) return;
        if (!_hasView) FitAll();
        var anchor = e.GetPosition(this);
        var world = ToWorld(anchor);
        var factor = Math.Pow(1.2, e.Delta / 120d);
        _scale = Math.Clamp(_scale * factor, _fitScale * 0.5, _fitScale * MaxZoomFactor);
        // 커서 아래 지점이 그대로 커서 아래에 남도록 중심을 옮긴다.
        _centerX = world.X - (anchor.X - ActualWidth / 2) / _scale;
        _centerY = world.Y + (anchor.Y - ActualHeight / 2) / _scale;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2) { FitAll(); e.Handled = true; return; }
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle)) return;
        _pressPoint = _lastPoint = e.GetPosition(this);
        _dragging = e.ChangedButton == MouseButton.Middle;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pressPoint is not { } press || !IsMouseCaptured) { Cursor = HitZone(e.GetPosition(this)) is not null || HitLine(e.GetPosition(this)) is not null ? Cursors.Hand : null; return; }
        var position = e.GetPosition(this);
        if (!_dragging && (position - press).Length < DragThreshold) return;
        _dragging = true;
        Cursor = Cursors.SizeAll;
        var delta = position - _lastPoint;
        _centerX -= delta.X / _scale;
        _centerY += delta.Y / _scale;
        _lastPoint = position;
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pressPoint is null) return;
        var wasDragging = _dragging;
        _pressPoint = null; _dragging = false;
        ReleaseMouseCapture();
        Cursor = null;
        if (wasDragging || e.ChangedButton != MouseButton.Left) return;
        var position = e.GetPosition(this);
        if (HitZone(position) is { } zone) { Execute(SelectZoneCommand, zone.Row); return; }
        if (HitLine(position) is { } line)
            Execute(SelectLineCommand, new AlignmentOverviewSelection(line.Row, (Keyboard.Modifiers & ModifierKeys.Control) != 0));
    }

    private static void Execute(ICommand? command, object parameter)
    {
        if (command?.CanExecute(parameter) == true) command.Execute(parameter);
    }

    private AlignmentOverviewZone? HitZone(Point position)
        => Model?.Zones.Where(z => z.Points.Count >= 2)
            .Select(z => (Zone: z, Distance: DistanceTo(z.Points, position)))
            .Where(x => x.Distance <= HitTolerance).OrderBy(x => x.Distance).Select(x => x.Zone).FirstOrDefault();

    private AlignmentOverviewLine? HitLine(Point position)
        => Model?.Lines.Select(l => (Line: l, Distance: DistanceTo(l.Points, position)))
            .Where(x => x.Distance <= HitTolerance).OrderBy(x => x.Distance).Select(x => x.Line).FirstOrDefault();

    private double DistanceTo(IReadOnlyList<Point> world, Point screen)
    {
        var best = double.MaxValue;
        var previous = ToScreen(world[0]);
        for (var i = 1; i < world.Count; i++)
        {
            var current = ToScreen(world[i]);
            best = Math.Min(best, SegmentDistance(screen, previous, current));
            previous = current;
        }
        return best;
    }

    private static double SegmentDistance(Point p, Point a, Point b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared;
        if (lengthSquared <= 1e-12) return (p - a).Length;
        var t = Math.Clamp(Vector.Multiply(p - a, ab) / lengthSquared, 0d, 1d);
        return (p - (a + ab * t)).Length;
    }

    private static Brush Frozen(Brush brush) { brush.Freeze(); return brush; }
    private static Pen FrozenPen(Brush brush, double thickness, PenLineCap cap = PenLineCap.Round)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }
}
