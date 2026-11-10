namespace DHBIMWATER.Core.Piping;

/// <summary>바이패스 세트 한 개의 입력(docs/228 § A). 본관 위 인라인 부속(<see cref="InlineFitting"/>)에 실린다.
/// 본관 밸브는 세트가 아니다(docs/230) — 세트 부속의 <see cref="InlineFitting.FamilyTypeName"/>은 빈 문자열이고,
/// A–A 사이 밸브는 사용자가 일반 밸브로 놓는다.</summary>
/// <param name="BypassValveFamilyTypeName">B–B 사이 바이패스 밸브. 새 세트면 패밀리명만, 되가져온 세트면 "패밀리명 : 유형명"이다.
/// 유형은 모델 생성 때 바이패스 관경과 DN이 같은 것으로 다시 고른다(docs/244 § A-4).</param>
/// <param name="BypassDiameterMm">바이패스 관경(mm). 0 = 모델 생성 때 결정(밸브부관 B에 본관 구경을 넣어 커넥터 지름으로 정한다, docs/244).
/// 되가져온 세트는 모델 실제값이다 — 표시용이며 배치는 이 값을 쓰지 않는다.</param>
/// <param name="Side">+1 = 엣지 진행 방향(시작→끝)의 왼쪽(반시계 90°), -1 = 오른쪽. 바이패스가 이쪽으로 빠진다.</param>
/// <param name="SecondValveFamilyTypeName">B–B 사이 두 번째 밸브 V2(엣지 끝 쪽, docs/255). null·빈 값이면 밸브 1개 세트다.
/// 첫 밸브(<paramref name="BypassValveFamilyTypeName"/>)가 V1(엣지 시작 쪽)이다. 형식·유형 선택 규칙은 V1과 같다.</param>
/// <param name="SpoolLengthMm">V1–V2 사이 단관 길이(mm, docs/255). 0 = 두 밸브 직결. 밸브 1개 세트에서는 쓰지 않는다.</param>
public sealed record BypassSetSpec(string BypassValveFamilyTypeName, double BypassDiameterMm, int Side,
    string? SecondValveFamilyTypeName = null, double SpoolLengthMm = 0)
{
    /// <summary>0이나 이상값은 왼쪽으로 본다. 부호만 뜻이 있다.</summary>
    public int NormalizedSide => Side < 0 ? -1 : 1;

    /// <summary>B–B 사이 밸브가 2개인가(docs/255). V2가 비면 1개 세트다.</summary>
    public bool HasTwoValves => !string.IsNullOrWhiteSpace(SecondValveFamilyTypeName);
}

/// <summary>바이패스 세트 규칙 모음(docs/228).
/// <para>세트 = 밸브부관 A 2개 + 밸브부관 B 2개 + 바이패스 밸브(B–B 사이) + 바이패스 쪽 틈 채움 단관(docs/230).
/// 바이패스 밸브는 1개 또는 2개이고, 2개면 V1–V2 사이에 단관이 들어간다(docs/255).
/// A–A 사이 본관은 세트가 아니다 — 관으로 잇고, 밸브는 사용자가 일반 밸브로 놓는다.
/// 치수는 패밀리가 호칭지름으로 정하므로 캔버스는 개략 모양만 그리고, 실제 자리는 Revit 배치가 커넥터를 재서 정한다.</para></summary>
public static class BypassSet
{
    /// <summary>캔버스 인라인 부속의 TypeKey. 밸브("배관 밸브류")·이음관과 가르는 표식이다.</summary>
    public const string TypeKey = "바이패스 세트";

    /// <summary>밸브 2개 세트의 V1–V2 사이 단관 기본 길이(mm, docs/255). 세트마다 바꿀 수 있다.</summary>
    public const double DefaultSpoolLengthMm = 300;

    /// <summary>단관 길이가 쓸 수 있는 값인가(docs/255 § D-6). 0(직결) 또는 관 최소 조각 이상이어야 한다.</summary>
    public static bool IsValidSpoolLength(double spoolLengthMm)
        => double.IsFinite(spoolLengthMm) && (spoolLengthMm == 0 || spoolLengthMm >= PipeSegmentPlan.MinSegmentMm);

    /// <summary>캔버스 개략 그림 전용 바이패스 관경 추정(mm) — 본관의 20%를 50 단위로 반올림, 최소 50.
    /// 핸드북 규격표(<see cref="ValveBypassSpecTable"/>)에 없는 주관일 때만 쓰는 폴백이다(docs/256). 배치·재료표에 쓰지 않는다.</summary>
    public static double PreviewBypassDiameterMm(double mainDiameterMm)
        => Math.Max(50, Math.Round(mainDiameterMm * 0.2 / 50, MidpointRounding.AwayFromZero) * 50);

    /// <summary>캔버스 개략 표시용 — 세트 중심에서 A 중심까지(mm). 실제 값이 아니다(Revit 실측이 정한다).</summary>
    public static double EstimatedHalfSpanMm(double mainDiameterMm, double bypassDiameterMm)
        => Math.Max(500, mainDiameterMm * 0.9 + bypassDiameterMm);

    /// <summary>세트가 놓일 배관이 짧을 때 건너뛰기 사유(docs/322). 짧지 않으면 null.
    /// <paramref name="requiredMm"/> = 세트 본관 점유 길이(A 바깥 커넥터 사이 = 2·(A 중심 거리 + A 주관 반폭)).
    /// <paramref name="edgeLengthMm"/> = 양 끝 절점 부속 커넥터 사이 배관 길이. <paramref name="nodeLegsMm"/> = 양 끝 절점 중심 → 커넥터 거리 합.
    /// 사용자는 캔버스 절점 중심 거리로 고치므로 그 값(+ 레그)도 같이 알려 준다.</summary>
    public static string? EdgeTooShortReason(double edgeLengthMm, double requiredMm, double nodeLegsMm, double toleranceMm = 1e-6)
    {
        if (edgeLengthMm + toleranceMm >= requiredMm) return null;
        var legs = Math.Max(0, nodeLegsMm);
        return FormattableString.Invariant(
            $"배관이 짧아 바이패스 세트를 놓을 수 없습니다 — 세트 본관 {requiredMm:N0}mm 필요, 배관 {edgeLengthMm:N0}mm. ") +
            FormattableString.Invariant(
                $"양 끝 절점 중심 사이를 {requiredMm + legs:N0}mm 이상으로 늘리세요(현재 {edgeLengthMm + legs:N0}mm).");
    }

    /// <summary>캔버스 개략 표시용 — 본관 중심선에서 바이패스 관 중심선까지(mm).</summary>
    public static double EstimatedOffsetMm(double mainDiameterMm, double bypassDiameterMm)
        => Math.Max(400, mainDiameterMm * 0.5 + bypassDiameterMm * 2);
}
