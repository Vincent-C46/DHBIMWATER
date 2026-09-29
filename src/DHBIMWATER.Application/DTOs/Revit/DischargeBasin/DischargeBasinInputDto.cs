namespace DHBIMWATER.Application.DTOs.Revit.DischargeBasin
{
    /// <summary>
    /// 토출조 직접 입력 DATA.
    /// 엑셀 `평면제원 입력-*` 시트 상단 "● 직접 입력 DATA" 13개 항목에 대응한다.
    /// (원본: 토출조 input data\*.xlsx — docs/164_토출조_제원입력_기획.md §2)
    /// </summary>
    /// <param name="Position">토출 위치 — 평면제원에만 영향</param>
    /// <param name="Step">단차 — 종단제원에만 영향</param>
    /// <param name="ConduitType">토출관로 형식</param>
    /// <param name="TA">H.W.L — 토출조 계획 고수위 (EL, m)</param>
    /// <param name="Separation">
    /// 펌프장과 이격거리 (m). 펌프장 최우측 벽 외면 ↔ 토출조 <b>벽체 외면</b> 사이 거리다.
    /// ※ 엑셀의 어느 수식에도 쓰이지 않지만(확인필요사항 B-1) <b>배치 원점</b>을 정한다
    /// — <see cref="Services.DischargeBasinPlacement"/>.
    /// </param>
    /// <param name="ON">토출관로 련수</param>
    /// <param name="OB">토출암거 폭 (mm) — ConduitType = Culvert 일 때 사용</param>
    /// <param name="OH">토출암거 높이 (mm) — ConduitType = Culvert 일 때 사용</param>
    /// <param name="OD">토출관 직경 (mm) — ConduitType = Pipe 일 때 사용</param>
    /// <param name="D">[펌프장 연동] 펌프 직경 (mm)</param>
    /// <param name="N">[펌프장 연동] 펌프 대수</param>
    /// <param name="PumpHwl">[펌프장 연동] 펌프장 HWL (EL, m)</param>
    /// <param name="H3">[펌프장 연동] 펌프장 H3 (mm)</param>
    /// <param name="H7">[펌프장 연동] 펌프장 H7 — 최소 토피 (mm)</param>
    /// <param name="B8PlusT5">
    /// [펌프장 연동] 펌프장 B8 + T5 = 펌프 중심 간격 (mm).
    /// 근거: PumpingStationGeometryCalculator.cs 의 totalWidth = T4*2 + B8*N + T5*(N-1)
    /// </param>
    /// <param name="StationRightEdgeX">
    /// [펌프장 연동] 펌프장 최우측(+X) 외벽 외면의 프로젝트 X (mm) = 펌프장 전체 길이.
    /// 근거: PumpingStationGeometryCalculator.TotalLength — 상부슬래브 외곽이 x = 0 ~ 전체길이다.
    /// </param>
    /// <param name="FirstPumpCenterY">
    /// [펌프장 연동] 펌프장 1번 펌프 중심의 프로젝트 Y (mm) = B8 / 2.
    /// 근거: 펌프실 내공이 y = 0 ~ B8×N + T5×(N−1) 이고 각 펌프칸 폭이 B8 이다.
    /// </param>
    /// <param name="Frame">
    /// 기둥·거더 배치. null 이면 만들지 않는다 — 원본에 열 위치·개수가 없어 현재는 항상 null 이다.
    /// 확장 지점: 값만 채우면 기둥·거더가 함께 생성된다 (<see cref="DischargeBasinFrameSpec"/>).
    /// </param>
    public record DischargeBasinInputDto
    (
        DischargeOutletPosition Position,
        DischargeStepType Step,
        OutletConduitType ConduitType,

        // ── 토출조 신규 입력 ──
        double TA,
        double Separation,
        int ON,
        double OB,
        double OH,
        double OD,

        // ── 펌프장 연동 (읽기전용) ──
        double D,
        int N,
        double PumpHwl,
        double H3,
        double H7,
        double B8PlusT5,

        // ── 펌프장 연동 (배치 기준) ──
        // 기본값 0 = 프로젝트 원점 기준. 실제 배치는 화면이 펌프장 제원에서 채워 넣는다.
        double StationRightEdgeX = 0,
        double FirstPumpCenterY = 0,

        // ── 확장 지점 ──
        DischargeBasinFrameSpec? Frame = null
    );
}
