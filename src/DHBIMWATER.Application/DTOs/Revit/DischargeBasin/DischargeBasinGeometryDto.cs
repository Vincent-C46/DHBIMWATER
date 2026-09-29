namespace DHBIMWATER.Application.DTOs.Revit.DischargeBasin
{
    /// <summary>
    /// 토출조 권고안(자동 계산) 결과.
    /// 단위 — EL 항목(TB/TC/TE/TF)은 m, 각도(Theta*)는 도(°), 나머지는 mm.
    /// 유형에 따라 산출되지 않는 항목은 null 이다.
    /// (수식 전문: docs/164_토출조_제원입력_기획.md §3)
    /// </summary>
    public record DischargeBasinGeometryDto
    (
        // ── 종단 · 공통 ──
        double TB,          // 토출조 상단고 (EL, m)
        double TC,          // 지반고 (EL, m)
        double TE,          // 토출관 BOP (EL, m)
        double TF,          // 토출조 바닥고 (EL, m)
        double TT1,         // 벽체 두께
        double TT2,         // 바닥슬래브 두께
        double TT3,         // 상부슬래브 두께 (400 고정)
        double TH1,         // 여유고
        double TH2,         // 수면 ~ 토출관 상단
        double TH3,         // 토출관 하부 두께
        double TCB1,        // 기둥 가로
        double TCH1,        // 기둥 세로
        double TGB1,        // 거더 폭
        double TGH1,        // 거더 돌출높이 (300 고정)

        // ── 종단 · 단차 적용 시만 ──
        double? TH4,        // 단차 하부 높이
        double? TH5,        // 포장 여유 (200 고정)
        double? TB6,        // 단차 상부 폭

        // ── 평면 · 공통 ──
        double TB1,         // 내공 폭 (펌프측)
        double TB2,         // 내공 폭 (토출측)
        double TB3,         // 바닥슬래브 폭 (= TT2)
        double TB4,         // 바닥 경사 투영 (θ1 기준)
        double TT4,         // 벽체 경사 투영 (θ1 기준)
        double TL1,         // 단부 펌프 중심 ~ 내벽
        double TL2,         // 펌프 순간격
        double TLT,         // 내공 총길이
        double OT,          // 암거 사이 벽두께
        double O1,          // PIPE 사이 간격
        double TOB1,        // 상부 개구부 폭
        double TOL1,        // 상부 개구부 길이
        double TOL2,        // 진입 개구부 길이
        double Theta,       // 벌어짐 각 θ (°)
        double Theta1,      // θ1 = (90° + θ) / 2 (°)

        // ── 평면 · 좌우측 전용 ──
        double? Theta2,     // θ2 = 90° − θ1 (°)
        double? TB5,        // 바닥 경사 투영 (θ2 기준)
        double? TT5,        // 벽체 경사 투영 (θ2 기준)

        // ── 평면 · 중앙부 전용 ──
        double? TL3,        // 경사 구간 길이
        double? TL4,        // 절곡부 여유폭 (1000 고정)
        double? TL5,
        double? TL6,
        double? TL7,
        double? TL8
    );
}
