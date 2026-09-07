namespace DHBIMWATER.Core.Geometry;

/// <summary>드레인 피트 정의를 Void 돌출 형상으로 바꾼다. Floor·DirectShape 두 경로가 함께 쓴다.</summary>
public static class DrainPitGeometry
{
    /// <summary>면 겹침을 피하기 위해 Void 상단이 호스트 상부면을 넘는 길이(mm).
    /// DirectShape 경로(<see cref="ToVoidExtrusion"/>)에서만 쓴다 — Revit Void 인스턴스 배치 경로는
    /// 사용자 지시로 오버슛 없이 기초 상면 EL에 그대로 놓는다(docs/134).</summary>
    public const double TopOvershootMm = 10;

    /// <summary>Void 돌출 정의를 만든다. 바닥에서 위로 깊이와 여유만큼 밀어 올린다.</summary>
    public static VoidExtrusionDefinition ToVoidExtrusion(DrainPitDefinition pit)
    {
        if (pit.Shape != DrainPitShape.Rectangular)
            throw new NotSupportedException($"드레인 피트 형상 '{pit.Shape}'는 아직 지원하지 않는다.");

        var bottomZ = pit.TopCenter.Z - pit.Depth;
        var hx = pit.LengthX / 2;
        var hy = pit.WidthY / 2;
        var cx = pit.TopCenter.X;
        var cy = pit.TopCenter.Y;

        return new VoidExtrusionDefinition
        {
            Profile = new[]
            {
                new Point3D(cx - hx, cy - hy, bottomZ),
                new Point3D(cx + hx, cy - hy, bottomZ),
                new Point3D(cx + hx, cy + hy, bottomZ),
                new Point3D(cx - hx, cy + hy, bottomZ)
            },
            Normal = new Vector3D(0, 0, 1),
            Distance = pit.Depth + TopOvershootMm
        };
    }

    /// <summary>깊이가 호스트 두께 이상이면 관통이다.</summary>
    public static bool IsPenetrating(DrainPitDefinition pit, double hostThicknessMm) => pit.Depth >= hostThicknessMm;

    /// <summary>피트 상단 사각형이 주어진 축정렬 범위 안에 완전히 들어오는지 판정한다.</summary>
    public static bool IsInsideBounds(DrainPitDefinition pit, double minX, double minY, double maxX, double maxY) =>
        pit.TopCenter.X - pit.LengthX / 2 >= minX && pit.TopCenter.X + pit.LengthX / 2 <= maxX &&
        pit.TopCenter.Y - pit.WidthY / 2 >= minY && pit.TopCenter.Y + pit.WidthY / 2 <= maxY;
}
