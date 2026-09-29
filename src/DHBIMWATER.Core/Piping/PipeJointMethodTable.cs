using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 밸브실 부재 → 접합 슬롯 종류(docs/210).
/// <para><b>산출 관행은 "부재당 1개소"다.</b> 산출서(도수1-1) 제수밸브실 3개 실을 관재료 집계표와 대조하면
/// 접합 개소 합이 관부재 수와 같다 — No.61+0은 부재 4 = 용접 1 + 플랜지 3, No.5+17.3은 부재 10 = 1 + 9다.
/// D600·D300은 부설수와 플랜지접합수가 아예 같은 값이다. 그래서 여기서는 <b>개수를 세지 않고</b>
/// 어느 매개변수에 1을 적을지만 정한다.</para>
/// <para><b>배분 자체는 계획관로 표에 위임한다(docs/170 § D-1).</b> 예전에는 부재명의 `S`/`F` 표기를
/// 여기서 직접 읽어 용접/플랜지로 갈랐는데, 그 판정은 두 가지가 틀렸다 —
/// ① `S`=소켓이 용접인 것은 <b>강관일 때</b>이고 주철관 소켓은 KP 메커니컬 또는 이탈방지압륜이다,
/// ② 애드인이 만드는 부재명에는 애초에 표기가 붙지 않아 <b>전량 플랜지로 떨어졌다</b>.
/// 표를 둘 두고 테스트로 일치를 확인하는 것보다 <b>표를 하나로 만드는 쪽이 구조적으로 안전하다</b> —
/// 계획관로와 밸브실이 같은 입력에 다른 값을 찍으면 수량 집계가 갈린다.
/// 접합 방식(소켓/플랜지)을 정하는 일은 <see cref="PipeJointResolution"/>이 맡는다.</para>
/// </summary>
public static class PipeJointMethodTable
{
    /// <param name="material">인스턴스의 <c>DH_관종</c>. 소켓인데 관종을 모르면 호출부가 강관으로 간주한다(docs/170 § F).</param>
    /// <param name="connection"><see cref="PipeJointResolution.Resolve"/>가 정한 접합 방식.</param>
    /// <param name="useRestraintGland">주철관 소켓에 이탈방지압륜을 쓰는지. 강관·플랜지에는 영향이 없다.</param>
    /// <param name="isAccessory">배관 밸브류(<c>OST_PipeAccessory</c>) 여부.</param>
    /// <returns>접합 슬롯 종류. 밸브류는 null이다.</returns>
    public static string? JointKindFor(PipeMaterial? material, BendConnection connection, bool useRestraintGland, bool isAccessory)
    {
        // 밸브는 `제수밸브 접합 및 부설`(CGI…)로 1대 = 1nr 계상되고, 그 품에 접합이 포함된다.
        // 여기서 플랜지 접합을 또 찍으면 산출서가 같은 접합을 두 번 센다(docs/168 § 5-4).
        if (isAccessory) return null;
        return PipeAlignmentParameters.JointKindFor(material, connection, useRestraintGland);
    }
}
