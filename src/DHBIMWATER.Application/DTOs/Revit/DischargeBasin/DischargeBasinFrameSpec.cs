namespace DHBIMWATER.Application.DTOs.Revit.DischargeBasin
{
    /// <summary>
    /// 토출조 기둥·거더 배치.
    ///
    /// <para><b>지금은 항상 null 이다.</b> 엑셀 표는 단면(TCB1·TCH1 = TT1, TGB1 = TT1, TGH1 = 300)만 주고,
    /// 기둥 패밀리 유형을 고를 화면이 아직 없다(docs/164_토출조_확인필요사항.md C-4).
    /// 이 record 를 채워 <see cref="DischargeBasinInputDto.Frame"/> 에 넣기만 하면
    /// 계산기와 UseCase 가 그대로 기둥·거더를 만든다 — 형상 규칙은 이미 구현돼 있다.</para>
    ///
    /// <para>기둥 개수·X 위치는 평면 도해대로 <b>펌프 사이(TL2 가운데), 펌프 대수 − 1 개</b>다.
    /// 따라서 받을 값은 열의 Y 위치와 유형뿐이다.</para>
    /// </summary>
    /// <param name="ColumnLineOffsetY">
    /// 펌프측 내벽면(Y=0)에서 기둥 열 중심까지의 거리 (mm).
    /// 평면·종단 도해상 기둥은 TB1 선(단부 코너 높이) 부근에 선다.
    /// </param>
    /// <param name="ColumnTypeName">
    /// 기둥 패밀리 유형 — "패밀리명 : 타입명".
    /// <see cref="Interfaces.IElementTypeCommandRepo"/> 에 기둥 유형 생성 API 가 없어
    /// 기존 문서에 이미 있는 유형을 골라 써야 한다(밸브실과 같은 방식).
    /// 단면은 TCB1 × TCH1 (= TT1 × TT1) 에 맞는 유형이어야 한다.
    /// </param>
    /// <param name="CreateGirder">기둥 열을 따라 거더를 함께 만들지 여부.</param>
    public record DischargeBasinFrameSpec
    (
        double ColumnLineOffsetY,
        string ColumnTypeName,
        bool CreateGirder = true
    );
}
