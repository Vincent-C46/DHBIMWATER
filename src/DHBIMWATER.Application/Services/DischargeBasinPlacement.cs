using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Application.Services
{
    /// <summary>
    /// 토출조 로컬 좌표 → 프로젝트 좌표 배치 변환.
    ///
    /// <para><b>왜 필요한가</b> — 토출조 형상은 평면 도해와 같은 로컬 좌표(+X = 펌프 배열, +Y = 펌프측→토출측)로
    /// 계산되는데, 펌프장은 <b>좌→우(+X)로 모델링되고 펌프는 +Y로 배열</b>된다
    /// (<see cref="PumpingStationGeometryCalculator"/> 의 <c>totalWidth = T4×2 + B8×N + T5×(N−1)</c>).
    /// 변환 없이 만들면 두 구조물이 원점에서 겹치고 펌프 중심도 90° 어긋난다.</para>
    ///
    /// <para><b>변환</b> — <c>(x, y) → (OriginX + y, OriginY + x)</c>.
    /// 로컬 +X(TB1→TB2 단부 방향)가 프로젝트 <b>+Y</b>, 로컬 +Y(펌프측→토출측)가 프로젝트 <b>+X</b>가 된다.
    /// 결과적으로 <b>펌프측 직선 벽이 펌프장을 향하고(−X), 넓은 단부(TB2·토출관로 출구)가 +Y</b>에 놓인다 —
    /// 평면 도해를 반시계 90° 돌린 배치와 같다(사용자 결정 2026-09-15).</para>
    ///
    /// <para>⚠ 이 변환은 회전이 아니라 <b>대칭(축 교환)</b>이다. 현재 계산기의 내공 외곽선이
    /// 평면 도해를 상하로 뒤집은 거울상이기 때문이다(도해: 펌프측이 위 / 코드: 펌프측이 아래).
    /// 축을 교환하면 도해와 같은 손방향으로 돌아온다. 대신 다각형 진행 방향이 반시계 → 시계로 뒤집히므로
    /// <see cref="MapOutline"/> 이 점 순서를 되돌려 반시계를 유지한다 — 바깥 오프셋·마이터 계산이 이 규약에 기댄다.</para>
    ///
    /// <para><b>원점</b> —
    /// <list type="bullet">
    /// <item><c>OriginX</c> : 로컬 y=0(펌프측 내벽 <b>내면</b>)의 프로젝트 X.
    /// 벽체 외면이 <c>펌프장 최우측 벽 외면 + 이격거리</c>에 오도록 벽두께 TT1 을 더한다
    /// (이격 기준면 = 토출조 <b>벽체 외면</b>, 사용자 결정 2026-09-15.
    /// 바닥슬래브는 여기서 TB3 만큼 더 내밀므로 지중 실간격은 그만큼 좁다).</item>
    /// <item><c>OriginY</c> : 로컬 x=0(TB1 단부 내면)의 프로젝트 Y.
    /// 1번 펌프 중심이 <b>펌프장 1번 펌프 중심과 일치</b>하도록 잡는다.
    /// 펌프 피치가 <c>D + TL2 = B8 + T5</c> 로 이미 같으므로 1번만 맞추면 나머지도 전부 맞는다.</item>
    /// </list></para>
    /// </summary>
    /// <param name="OriginX">로컬 y=0 평면의 프로젝트 X (mm)</param>
    /// <param name="OriginY">로컬 x=0 평면의 프로젝트 Y (mm)</param>
    public readonly record struct DischargeBasinPlacement(double OriginX, double OriginY)
    {
        /// <summary>펌프장 연동값과 이격거리로 배치 원점을 잡는다.</summary>
        public static DischargeBasinPlacement For(DischargeBasinInputDto input, DischargeBasinGeometryDto g)
            => new(
                // 벽체 외면(= OriginX − TT1)이 펌프장 우측 벽 외면에서 이격거리(m)만큼 떨어지도록.
                input.StationRightEdgeX + input.Separation * 1000 + g.TT1,
                // 1번 펌프 중심(로컬 x)이 펌프장 1번 펌프 중심(프로젝트 Y)에 오도록.
                input.FirstPumpCenterY - DischargeBasinLayoutCalculator.PumpCenterX(input, g, 0));

        public Point2D Map(Point2D p) => new(OriginX + p.Y, OriginY + p.X);

        public Point3D Map(Point3D p) => new(OriginX + p.Y, OriginY + p.X, p.Z);

        /// <summary>
        /// 다각형 변환. 축 교환으로 진행 방향이 뒤집히므로 <b>점 순서를 되돌려 반시계를 유지</b>한다
        /// (바깥 오프셋·코너 마이터가 반시계를 전제한다).
        /// </summary>
        public IReadOnlyList<Point2D> MapOutline(IReadOnlyList<Point2D> points)
        {
            var mapped = new List<Point2D>(points.Count);
            for (var i = points.Count - 1; i >= 0; i--)
                mapped.Add(Map(points[i]));
            return mapped;
        }
    }
}
