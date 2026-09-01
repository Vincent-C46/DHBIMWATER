namespace DHBIMWATER.Core.Geometry;

/// <summary>드레인 피트 형상. 확장 여지를 위해 형상 종류를 값으로 들고 있다.</summary>
public enum DrainPitShape
{
    /// <summary>사각 피트. 현재 유일한 구현.</summary>
    Rectangular,
    /// <summary>원형 피트. 미구현 — 형상 계산기에서 예외를 던진다.</summary>
    Circular
}

/// <summary>드레인 피트 한 개의 정의. 모든 길이는 mm, 좌표는 프로젝트 내부좌표다.</summary>
public record DrainPitDefinition
{
    /// <summary>피트 상단면 중심. Z는 피트가 시작되는 바닥 상부면 표고다.</summary>
    public Point3D TopCenter { get; init; }
    /// <summary>X 방향 치수(mm). 밸브실 규약상 X축이 길이 방향이다.</summary>
    public double LengthX { get; init; }
    /// <summary>Y 방향 치수(mm). 밸브실 규약상 Y축이 폭 방향이다.</summary>
    public double WidthY { get; init; }
    /// <summary>상단면에서 아래로 파는 깊이(mm).</summary>
    public double Depth { get; init; }
    public DrainPitShape Shape { get; init; } = DrainPitShape.Rectangular;
    public string Name { get; init; } = "드레인피트";
}
