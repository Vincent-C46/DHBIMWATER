using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DHBIMWATER.UI.Controls;

/// <summary>
/// 도해 매개변수 글자 확대 강조. 글자 영역(Data 경로의 도형마다)을 Source 그림에서 잘라 Scale배로 키워
/// 제자리(중심 기준) 위에 덮음. 상자 = 흰 바탕(밑 원래 글자 가림) + 빨간 테두리.
/// <para>도해 Grid 안에서 Source(Image) 뒤에 둠. 좌표 = 도해 Grid 좌표 = 경로 좌표(PNG 픽셀). 칸 밖으로 안 나가게 밀어 넣음.</para>
/// <para>Data 비면 아무것도 안 그림. 입력 막지 않음(IsHitTestVisible=false).</para>
/// </summary>
public sealed class DiagramLabelMagnifier : Canvas
{
    // 글자 상자 여유(도해 픽셀). 경로가 글자에 딱 붙어 테두리가 글자 덮지 않게
    private const double Padding = 4;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(string), typeof(DiagramLabelMagnifier),
        new PropertyMetadata(null, (d, _) => ((DiagramLabelMagnifier)d).Rebuild()));

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(Visual), typeof(DiagramLabelMagnifier),
        new PropertyMetadata(null, (d, _) => ((DiagramLabelMagnifier)d).Rebuild()));

    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(double), typeof(DiagramLabelMagnifier),
        new PropertyMetadata(1.5, (d, _) => ((DiagramLabelMagnifier)d).Rebuild()));

    public static readonly DependencyProperty FrameBrushProperty = DependencyProperty.Register(
        nameof(FrameBrush), typeof(Brush), typeof(DiagramLabelMagnifier),
        new PropertyMetadata(Brushes.Red, (d, _) => ((DiagramLabelMagnifier)d).Rebuild()));

    public static readonly DependencyProperty FrameThicknessProperty = DependencyProperty.Register(
        nameof(FrameThickness), typeof(double), typeof(DiagramLabelMagnifier),
        new PropertyMetadata(4.0, (d, _) => ((DiagramLabelMagnifier)d).Rebuild()));

    /// <summary>강조 경로 문자열(DiagramLabels 표 값). 도형 하나 = 글자 하나.</summary>
    public string? Data { get => (string?)GetValue(DataProperty); set => SetValue(DataProperty, value); }

    /// <summary>잘라 올 도해 그림 요소(보통 같은 Grid의 Image).</summary>
    public Visual? Source { get => (Visual?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }

    public Brush FrameBrush { get => (Brush)GetValue(FrameBrushProperty); set => SetValue(FrameBrushProperty, value); }

    public double FrameThickness { get => (double)GetValue(FrameThicknessProperty); set => SetValue(FrameThicknessProperty, value); }

    public DiagramLabelMagnifier()
    {
        IsHitTestVisible = false;
        // 칸 크기 바뀌면 밀어 넣기 범위 달라짐
        SizeChanged += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        if (string.IsNullOrWhiteSpace(Data) || Source == null) return;

        Geometry geometry;
        try { geometry = Geometry.Parse(Data); }
        catch (FormatException) { return; }

        foreach (var figure in PathGeometry.CreateFromGeometry(geometry).Figures)
        {
            var label = new PathGeometry(new[] { figure }).Bounds;
            if (label.IsEmpty || label.Width <= 0 || label.Height <= 0) continue;
            label.Inflate(Padding, Padding);
            Children.Add(CreateBox(label));
        }
    }

    /// <summary>글자 영역 하나 → 확대 상자. 테두리 두께 포함해 중심 맞춤.</summary>
    private Border CreateBox(Rect label)
    {
        var width = label.Width * Scale;
        var height = label.Height * Scale;
        var frame = FrameThickness;

        var left = label.X + label.Width / 2 - width / 2 - frame;
        var top = label.Y + label.Height / 2 - height / 2 - frame;
        var outerWidth = width + frame * 2;
        var outerHeight = height + frame * 2;

        // 도해 가장자리 글자는 상자가 칸 밖으로 나감 → 안쪽으로 밀기
        if (ActualWidth > 0) left = Math.Max(0, Math.Min(left, ActualWidth - outerWidth));
        if (ActualHeight > 0) top = Math.Max(0, Math.Min(top, ActualHeight - outerHeight));

        var box = new Border
        {
            Width = outerWidth,
            Height = outerHeight,
            Background = Brushes.White,
            BorderBrush = FrameBrush,
            BorderThickness = new Thickness(frame),
            Child = new Rectangle
            {
                // Source 그림의 글자 영역만 잘라 상자에 꽉 채움(가로세로 같은 배율)
                Fill = new VisualBrush(Source)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = label,
                    Stretch = Stretch.Fill,
                },
            },
        };
        SetLeft(box, left);
        SetTop(box, top);
        return box;
    }
}
