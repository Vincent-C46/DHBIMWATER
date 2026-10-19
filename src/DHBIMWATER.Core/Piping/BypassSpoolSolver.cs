namespace DHBIMWATER.Core.Piping;

/// <summary>단관 보정 입력(mm, docs/297 § B-2).</summary>
/// <param name="UserOffsetMm">사용자 단관 기준 세트 중심 → A 중심 거리.</param>
/// <param name="HalfRequiredMm">쪽별 필요 길이 R = T 레그 + 세트 안 밸브 폭 합 + A 주관 반폭. 밸브 없는 쪽도 넣는다.</param>
/// <param name="BendValveLegMm">B 코너 → 밸브쪽 커넥터.</param>
/// <param name="ValveWidthsMm">V1 + V2 폭 합.</param>
/// <param name="ToleranceMm">0으로 볼 길이. 기본 = 관 분할 허용치 — 더 크면 남은 틈이 관 분할에서 "짧은 틈"으로 걸림.</param>
public sealed record BypassSpoolInput(
    double UserOffsetMm, IReadOnlyList<double> HalfRequiredMm, double BendValveLegMm, double ValveWidthsMm,
    double MinSegmentMm = PipeSegmentPlan.MinSegmentMm, double ToleranceMm = 1e-6);

/// <param name="Adjusted">사용자 값보다 늘렸는가. false면 입력 오프셋·단관 그대로.</param>
public sealed record BypassSpoolSolution(double OffsetMm, double SpoolMm, bool Adjusted);

/// <summary>T형관 중심 밸브 2개 세트 — 본관 부품 폭에 맞춰 A 오프셋 다시 잡고 차이는 V1–V2 단관 하나가 흡수(docs/297).
/// <para>A는 T 절점 좌우 대칭이라 합계 아닌 긴 쪽 기준. 늘리기만 함. 본관 남는 길이·단관 모두 0(허용치 안) 또는 관 최소 조각 이상.</para></summary>
public static class BypassSpoolSolver
{
    /// <summary>오프셋 d의 단관 길이. S = 2d − 2·Lb − (W1 + W2).</summary>
    public static double SpoolAt(BypassSpoolInput input, double offsetMm)
        => 2 * offsetMm - 2 * input.BendValveLegMm - input.ValveWidthsMm;

    public static BypassSpoolSolution Solve(BypassSpoolInput input)
    {
        var userSpool = SpoolAt(input, input.UserOffsetMm);
        if (input.HalfRequiredMm.Count == 0) return new(input.UserOffsetMm, userSpool, false);

        var min = input.MinSegmentMm;
        var longest = input.HalfRequiredMm.Max();
        var shortest = input.HalfRequiredMm.Min();
        var baseOffset = Math.Max(input.UserOffsetMm, longest);
        // 단관 = 최소 조각 되는 오프셋.
        var spoolMinOffset = (min + 2 * input.BendValveLegMm + input.ValveWidthsMm) / 2;
        // 마지막 후보 = 모든 쪽 남는 길이 ≥ min, 단관 ≥ min — 항상 성립.
        var fallback = Math.Max(baseOffset, Math.Max(longest + min, spoolMinOffset));
        var offset = new[] { baseOffset, shortest + min, longest + min, spoolMinOffset, fallback }
            .Where(x => x >= baseOffset)
            .OrderBy(x => x)
            .First(x => IsValid(input, x));

        // 허용치 안 차이는 보정 아님 — 부동소수 흔들림 막음.
        return offset > input.UserOffsetMm + input.ToleranceMm
            ? new(offset, SpoolAt(input, offset), true)
            : new(input.UserOffsetMm, userSpool, false);
    }

    private static bool IsValid(BypassSpoolInput input, double offsetMm)
        => input.HalfRequiredMm.All(r => IsPieceLength(input, offsetMm - r)) && IsPieceLength(input, SpoolAt(input, offsetMm));

    /// <summary>관 조각 길이로 쓸 수 있나 — 겹침 아님, 0(허용치 안) 또는 최소 조각 이상.</summary>
    private static bool IsPieceLength(BypassSpoolInput input, double lengthMm)
        => lengthMm >= -input.ToleranceMm && (lengthMm <= input.ToleranceMm || lengthMm >= input.MinSegmentMm - 1e-6);
}

/// <summary>T형관 중심 세트 커넥터 사슬(docs/297 § G). 모든 값 = 소속 엣지 축(mm).
/// T 커넥터 ─ 세트 안 밸브 ─ A 안쪽 커넥터 직결이 되는 세트 중심·A 오프셋.</summary>
public static class BypassTeeChain
{
    /// <param name="ownerTeeMm">소속 엣지 쪽 T 커넥터 위치.</param>
    /// <param name="otherTeeMm">반대편 엣지 쪽 T 커넥터 위치(소속 축 투영).</param>
    /// <param name="ownerSign">소속 쪽 A 방향(세트 중심 기준 ±1). 반대편 A = −ownerSign.</param>
    /// <param name="ownerValvesMm">소속 쪽 세트 안 밸브 폭 합.</param>
    /// <param name="otherValvesMm">반대편 쪽 세트 안 밸브 폭 합.</param>
    /// <param name="teeHalfMm">A 주관 평균 반폭.</param>
    /// <param name="teeSkewMm">A 주관 구간 치우침(축 + 방향). A 구간 = [중심 − 반폭 + 치우침, 중심 + 반폭 + 치우침].</param>
    public static (double CenterMm, double OffsetMm) Solve(
        double ownerTeeMm, double otherTeeMm, int ownerSign, double ownerValvesMm, double otherValvesMm,
        double teeHalfMm, double teeSkewMm)
    {
        var ownerA = ownerTeeMm + ownerSign * (ownerValvesMm + teeHalfMm) - teeSkewMm;
        var otherA = otherTeeMm - ownerSign * (otherValvesMm + teeHalfMm) - teeSkewMm;
        return ((ownerA + otherA) / 2, ownerSign * (ownerA - otherA) / 2);
    }
}
