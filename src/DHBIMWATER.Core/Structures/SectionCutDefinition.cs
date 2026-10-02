using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Structures;

/// <summary>절단점·화면 오른쪽 방향으로 정의하는 수직 단면(docs/246). 모든 치수는 mm다.
/// <para>위 방향은 항상 +Z이고, 바라보는 방향 = Up × Right 이다(Revit 단면 규약: right·up·view 왼손 좌표계).
/// 예) Right +X → +Y를 바라봄, Right -Y → +X를 바라봄. ZMin/ZMax는 절대 표고다.</para></summary>
public sealed record SectionCutDefinition
{
    public required string Name { get; init; }

    /// <summary>절단선 위의 기준점(평면 X, Y만 사용). 크롭 가로 중앙이 된다.</summary>
    public required Point3D CutOrigin { get; init; }

    /// <summary>화면 오른쪽이 될 수평 방향(Z 성분은 무시).</summary>
    public required Vector3D Right { get; init; }

    /// <summary>크롭 가로 반폭.</summary>
    public required double HalfWidth { get; init; }

    /// <summary>크롭 하단 표고.</summary>
    public required double ZMin { get; init; }

    /// <summary>크롭 상단 표고.</summary>
    public required double ZMax { get; init; }

    /// <summary>절단면에서 바라보는 쪽으로의 깊이(far clip, &gt; 0).</summary>
    public required double FarClipDepth { get; init; }
}
