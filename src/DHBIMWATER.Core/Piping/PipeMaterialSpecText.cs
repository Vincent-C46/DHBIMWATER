using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 격점상세도 배관재료표의 부재 명칭·규격 문자열(docs/131 § B-3).
/// <para>신규 배치 경로(<c>RevitPipeSegmentCommandRepo</c>)와 기존 부재 재기록 경로
/// (<c>RevitPipeMaterialBackfillRepo</c>, docs/136)가 <b>글자 하나까지 같은 문자열</b>을 만들어야 한다.
/// 다르면 같은 부재가 일람표에서 두 행으로 갈린다. 그래서 문자열 생성을 여기 한곳에 둔다.</para>
/// </summary>
public static class PipeMaterialSpecText
{
    public const string StraightName = "직관";
    public const string ShortName = "단관";
    public const string BendName = "곡관";
    public const string TeeName = "T형";
    public const string CrossName = "십자";
    public const string WyeName = "Y형";

    /// <summary>직선/꺾임 판정 허용오차(도). 이 값 이하면 관, 넘으면 곡관으로 본다.</summary>
    public const double StraightDeflectionToleranceDeg = 1.0;

    /// <summary>정척 판정 허용오차(mm). 커넥터 실측값이 정척보다 이만큼 짧아도 직관으로 본다.</summary>
    public const double StraightLengthToleranceMm = 1.0;

    /// <summary>직관·단관 규격: `D160x6.00m`. 길이는 m로 환산해 소수 두 자리로 고정한다(docs/203 § A).
    /// <para>두 자리 고정이므로 표기 분해능은 10mm다 — 커넥터 실측 오차로 몇 mm 어긋난 같은 관은
    /// 같은 규격으로 묶인다. 소수를 떨구지 않는 것은 `4.00m`·`6.00m`처럼 자릿수가 맞아야
    /// 재료표에서 읽기 쉽기 때문이다(2026-09-21 사용자 결정).</para></summary>
    public static string Segment(double diameterMm, double lengthMm)
        => $"{ValvePipingParameters.DiameterText(diameterMm)}x{lengthMm / 1000:0.00}m";

    /// <summary>곡관 규격: `D160x45°`. 편각을 못 구한 절점은 직경만 적는다(docs/203 § B).
    /// 각도는 정수면 정수로 적는다 — `90°`·`22.5°`.</summary>
    public static string Bend(double diameterMm, double? deflectionDeg)
        => deflectionDeg is null
            ? ValvePipingParameters.DiameterText(diameterMm)
            : $"{ValvePipingParameters.DiameterText(diameterMm)}x{deflectionDeg.Value:0.##}°";

    /// <summary>T형 규격: `D1650xD600`(주관 x 분기). 분기경에도 `D`를 붙인다(docs/203 § C).
    /// <para>십자·Y형은 <b>따라오지 않는다</b> — 2026-09-21 사용자 결정으로 T형만 먼저 바꾸고
    /// 나머지는 뒤에 다듬기로 했다. 그래서 <see cref="MainBranch"/>와 식을 나눠 둔다.</para></summary>
    public static string Tee(double mainDiameterMm, double branchDiameterMm)
        => $"{ValvePipingParameters.DiameterText(mainDiameterMm)}x{ValvePipingParameters.DiameterText(branchDiameterMm)}";

    /// <summary>밸브부관 A·B 규격: `D1650xD300`(주관 x 분기, docs/227 § 0-2). T형과 같은 형식이다.
    /// <para>B는 자기 관경이 분기(D300)이고 주관은 이어진 A의 주관이다. 식을 따로 두는 이유는 T형 표기가
    /// 바뀌어도(docs/203) 밸브부관이 끌려가지 않게 하기 위해서다.</para></summary>
    public static string ValveBranch(double mainDiameterMm, double branchDiameterMm)
        => $"{ValvePipingParameters.DiameterText(mainDiameterMm)}x{ValvePipingParameters.DiameterText(branchDiameterMm)}";

    /// <summary>십자 규격: `D160x160`(주관 x 분기). docs/203 § C 이전의 T형 표기를 그대로 쓴다(docs/175 § B-3).</summary>
    public static string Cross(double mainDiameterMm, double branchDiameterMm)
        => MainBranch(mainDiameterMm, branchDiameterMm);

    /// <summary>Y형 규격: `D160x160 45°`(주관 x 분기, 분기각)(docs/187 § 2).
    /// <para>각도를 붙이는 이유: Y형은 같은 구경이라도 분기각이 다르면 다른 부재다. 곡관이 편각을 붙이는 것과 같다.</para></summary>
    public static string Wye(double mainDiameterMm, double branchDiameterMm, double branchAngleDeg)
        => $"{MainBranch(mainDiameterMm, branchDiameterMm)} {branchAngleDeg:0.##}°";

    // 십자·Y형이 쓰는 구 표기(`D160x160`). T형이 docs/203 § C로 갈라져 나가면서 남은 공통식이다.
    // TODO(docs/203): 십자·Y형 표기가 확정되면 이 헬퍼를 지우고 Tee로 다시 합친다.
    private static string MainBranch(double mainDiameterMm, double branchDiameterMm)
        => $"{ValvePipingParameters.DiameterText(mainDiameterMm)}x{branchDiameterMm:0.##}";

    /// <summary>정척 이상이면 직관, 아니면 단관.
    /// <para>신규 배치 경로는 <see cref="PipeSegmentPlan"/>이 정한 구분을 그대로 쓰고,
    /// 재기록 경로만 이 판정을 쓴다 — 배치 당시의 정척 설정이 모델에 남아 있지 않기 때문이다(docs/136 § 2).</para></summary>
    public static string SegmentName(double lengthMm, double straightLengthMm = PipeSegmentPlan.StraightLengthMm)
        => lengthMm >= straightLengthMm - StraightLengthToleranceMm ? StraightName : ShortName;

    /// <summary>비고 자동값: 직관·단관만 관 재료 코드(`DCIP` 등)를 적는다(docs/141 § B-2).
    /// <para>곡관·T형·밸브·신축관은 기본값이 없다 — 사용자가 도면·속성창에서 직접 채우는 칸이다.
    /// 규칙이 늘어나도 이 함수 한곳만 고치면 배치·재기록 두 경로가 함께 따라온다(docs/136과 같은 이유).</para>
    /// <para><b>표기를 떼고 비교한다</b> — 부재명에 접합 표기가 붙은 뒤로(docs/170 § C) 이름이 `2F 단관`이라
    /// 동등 비교로는 직관·단관이 잡히지 않는다. 그대로 두면 `DH_비고` 자동값이 통째로 사라진다.</para></summary>
    public static string Remark(string partName, PipeMaterial? material)
        => material is not null && StripJointMark(partName) is var baseName && (baseName == StraightName || baseName == ShortName)
            ? PipeAlignmentParameters.Code(material.Value)
            : string.Empty;

    /// <summary>부재의 단부(접합) 개수. 부재명 표기 앞의 숫자가 이 값이다.
    /// <para>T형·Y형 3구(docs/187), 십자 4구(docs/175), 나머지(직관·단관·곡관·이음관)는 2구다. 이경티는 현재 배치 범위 밖이다(docs/130 § A).</para></summary>
    public static int EndCountFor(string? partName) => StripJointMark(partName) switch { TeeName => 3, WyeName => 3, CrossName => 4, _ => 2 };

    /// <summary>부재명 앞에 접합 표기를 붙인다 — 접합 구성(<c>DH_접합구성</c>, `2S1F`)이 있으면 <b>그 표기 그대로</b>
    /// (`2S1F T형`·`1S1F 단관`), 없으면 접합 방식 단일 표기(`3F T형`)로 폴백한다.
    /// <para>2026-09-15 사용자 결정: 부재명(합친 이름)과 접합구성을 <b>둘 다 유지</b>하되 접두는 접합구성에서 유도한다 —
    /// 종전(docs/170 § C)의 부재 단위 단일 표기는 `2S1F` T형을 `3F T형`으로 적어 실제 형상과 어긋났다.</para>
    /// <para>구형 `KP:2+플랜지:1` 값도 <see cref="PipeJointComposition.Decode"/>가 읽으므로 `2S1F`로 정규화된다.</para></summary>
    public static string WithJointMark(string partName, string? composition, BendConnection connection, int endCount)
    {
        var ends = PipeJointComposition.Decode(composition);
        return ends.IsEmpty
            ? WithJointMark(partName, connection, endCount)
            : $"{PipeJointComposition.Encode(ends)} {StripJointMark(partName)}";
    }

    /// <summary>접합 방식 단일 표기 — `2F 단관`·`2S 곡관`·`3F T형`(docs/170 § C). 접합 구성을 모를 때의 폴백이며,
    /// 원장은 `S`가 하나라도 있으면 소켓 1개소로 세므로(docs/168 § 5-3) 개소 합은 혼합 표기와 같다.</summary>
    public static string WithJointMark(string partName, BendConnection connection, int endCount)
        // 이미 표기가 붙은 이름을 다시 받아도 `2F 2F 단관`이 되지 않게 한 번 떼고 붙인다(재기록 반복 실행).
        => $"{endCount}{(connection == BendConnection.Socket ? "S" : "F")} {StripJointMark(partName)}";

    /// <inheritdoc cref="WithJointMark(string, BendConnection, int)"/>
    public static string WithJointMark(string partName, BendConnection connection)
        => WithJointMark(partName, connection, EndCountFor(partName));

    /// <summary><see cref="WithJointMark(string, BendConnection, int)"/>의 역함수.
    /// 앞머리가 접합 표기가 아니면 <b>원본을 그대로</b> 돌려준다 — `1650 강관`처럼 숫자로 시작하는
    /// 사용자 입력 이름을 잘라먹지 않기 위해서다.</summary>
    public static string StripJointMark(string? partName)
    {
        if (string.IsNullOrWhiteSpace(partName)) return partName ?? string.Empty;
        var index = 0;
        var pairs = 0;
        while (index < partName.Length && char.IsDigit(partName[index]))
        {
            while (index < partName.Length && char.IsDigit(partName[index])) index++;
            if (index >= partName.Length) return partName;
            var mark = char.ToUpperInvariant(partName[index]);
            if (mark != 'S' && mark != 'F') return partName;
            index++;
            pairs++;
        }
        // 표기 뒤에 공백이 없으면 표기가 아니다(`2F단관`은 사용자가 지은 이름일 수 있다).
        if (pairs == 0 || index >= partName.Length || !char.IsWhiteSpace(partName[index])) return partName;
        return partName[index..].TrimStart();
    }

    /// <summary>절점 꺾임각(도). 인자는 절점에서 <b>바깥을 향하는</b> 두 단위벡터의 내적이다.
    /// 일직선이면 0, 직각이면 90 — <see cref="PipeNodeAngle.Deflection"/>과 같은 규약이다.
    /// <para>Revit <c>XYZ</c>는 Core에 들어올 수 없으므로 내적만 받는다.</para></summary>
    public static double DeflectionDeg(double dotOfUnitDirections)
        => 180 - Math.Acos(Math.Clamp(dotOfUnitDirections, -1, 1)) * 180 / Math.PI;
}
