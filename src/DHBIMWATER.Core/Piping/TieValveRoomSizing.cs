namespace DHBIMWATER.Core.Piping;

/// <summary>타이밸브실 최소 폭 계산 입력 — 프로젝트에 로드된 패밀리 실측(mm, docs/323). 바이패스 배치와 같은 프로브 값이다.</summary>
/// <param name="MainDiameterMm">본관 호칭지름.</param>
/// <param name="TeeBranchLegMm">T형관 중심 → 분기 커넥터. 연결관 양 끝을 이만큼 먹는다.</param>
/// <param name="SetOffsetMm">바이패스 세트(밸브 2개, 단관 300) 중심 → 밸브부관 A 중심 d.</param>
/// <param name="ValveTeeMainHalfMm">밸브부관 A 중심 → 주관 커넥터.</param>
/// <param name="BypassOffsetMm">본관 중심선 → 바이패스 관 중심선(A 분기 커넥터 + B 짧은 레그).</param>
/// <param name="BypassDiameterMm">바이패스 관경(핸드북 지관).</param>
/// <param name="ButterflyWidthMm">본관 버터플라이 밸브 커넥터 간 폭.</param>
public sealed record TieValveRoomProbe(
    double MainDiameterMm, double TeeBranchLegMm, double SetOffsetMm, double ValveTeeMainHalfMm,
    double BypassOffsetMm, double BypassDiameterMm, double ButterflyWidthMm);

/// <param name="TeeToTeeMm">연결관 양 끝 T 중심 사이 최소 거리.</param>
/// <param name="WallClearanceMm">내벽 → 위·아래 본관 중심 거리 c. IN·OUT(위)·IN2·OUT2(아래) 화살표 위치.</param>
/// <param name="MinWidthMm">내부 폭(Y) 최소값 = T–T + 2c, 100mm 단위 올림. c에 작업 여유 300 포함.</param>
public sealed record TieValveRoomLayout(double TeeToTeeMm, double WallClearanceMm, double MinWidthMm);

/// <summary>타이밸브실 관경별 최소 폭(docs/323). 표준 제원표가 없어 실측으로 정한다(2026-11-10 사용자 결정).
/// <para>폭(Y) = 위·아래 본관 사이 연결관 + 위·아래 바이패스가 벽 쪽으로 빠지는 여유. 길이(X)는 보지 않는다 —
/// 연결관 바이패스는 X로 빠지지만 이 규칙 범위 밖이다.</para></summary>
public static class TieValveRoomSizing
{
    /// <summary>바이패스 관 바깥 ~ 내벽 작업 여유(mm, 사용자 결정).</summary>
    public const double WorkClearanceMm = 300;

    /// <summary>최소 폭 올림 단위(mm). 50 → 100(2026-11-11 사용자 결정).</summary>
    public const double RoundStepMm = 100;

    public static TieValveRoomLayout Compute(TieValveRoomProbe probe)
    {
        // 연결관 세트 안 버터플라이 2개 — A 사이 틈 2(d − h)에 들어가야 함 → d ≥ h + 밸브 폭
        var offset = Math.Max(probe.SetOffsetMm, probe.ValveTeeMainHalfMm + probe.ButterflyWidthMm);
        var teeToTee = 2 * probe.TeeBranchLegMm + 2 * (offset + probe.ValveTeeMainHalfMm);
        // 벽 쪽 = 바이패스 관 바깥 + 작업 여유. 본관 반경 + 여유보다 작지 않게
        var clearance = Math.Max(probe.BypassOffsetMm + probe.BypassDiameterMm / 2, probe.MainDiameterMm / 2) + WorkClearanceMm;
        var width = Math.Ceiling((teeToTee + 2 * clearance) / RoundStepMm - 1e-9) * RoundStepMm;
        return new TieValveRoomLayout(Math.Round(teeToTee, 1), Math.Round(clearance, 1), width);
    }
}
