namespace DHBIMWATER.Core.Piping;

// 수직분기 세트 심볼 방향 맞춤(docs/306). 심볼 위쪽 = 패밀리 평면 +Y(BasisY) → 펼침 방향으로 수직축 회전.
// Revit 회전·미리보기 회전 같은 함수 씀 → 캔버스 = 실행 결과
public static class JunctionSymbolRotation
{
    // BasisY 평면 투영 길이 이 미만 = 로컬 Y 거의 수직 → 방향 못 정함
    public const double MinPlanLength = 0.1;
    private const double ToleranceRadians = 0.01 * Math.PI / 180;

    /// <summary>회전각(라디안, 뷰 기준 반시계 +, −π~π). 0 = 이미 맞음 또는 안 펼침(간격 0). null = 로컬 Y 수직이라 건너뜀.
    /// 매번 현재 방향에서 목표까지 → 재실행해도 누적 없음.</summary>
    public static double? AngleRadians(SymbolVector basisY, SymbolVector viewRight, SymbolVector viewUp, JunctionSetOffset set)
    {
        if (set.StepMm == 0) return 0;
        var right = IsZero(viewRight) ? new SymbolVector(1, 0, 0) : viewRight;
        var up = IsZero(viewUp) ? new SymbolVector(0, 1, 0) : viewUp;
        var ux = Dot(basisY, right);
        var uy = Dot(basisY, up);
        if (Math.Sqrt(ux * ux + uy * uy) < MinPlanLength) return null;
        var sign = Math.Sign(set.StepMm);
        var (dx, dy) = set.Axis == JunctionExpandAxis.X ? (sign, 0) : (0, sign);
        var angle = Math.Atan2(ux * dy - uy * dx, ux * dx + uy * dy);
        return Math.Abs(angle) < ToleranceRadians ? 0 : angle;
    }

    // 기호 모양 점 전부 원점(인스턴스 원점) 기준 회전
    public static JunctionSymbolShape Rotate(JunctionSymbolShape shape, double radians)
    {
        var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
        return JunctionSymbolShape.FromPolylines(shape.Polylines
            .Select(line => (IReadOnlyList<ShapePoint>)line
                .Select(p => new ShapePoint(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos)).ToList())
            .ToList());
    }

    private static double Dot(SymbolVector a, SymbolVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static bool IsZero(SymbolVector v) => v.X == 0 && v.Y == 0 && v.Z == 0;
}
