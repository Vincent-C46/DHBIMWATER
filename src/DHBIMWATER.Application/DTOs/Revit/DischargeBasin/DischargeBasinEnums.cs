namespace DHBIMWATER.Application.DTOs.Revit.DischargeBasin
{
    /// <summary>
    /// 토출 위치. 평면제원에만 영향을 준다 (경사 기준각 12° vs 20°, 중앙부는 TL3~TL8 추가).
    /// </summary>
    public enum DischargeOutletPosition
    {
        /// <summary>좌측 토출 — 평면 도해 왼쪽이 TB2. 우측의 미러. 제원 수식은 우측과 같음.</summary>
        Left,

        /// <summary>우측 토출 — 평면 도해 오른쪽이 TB2. 한 방향으로 벌어지는 사다리꼴. 기준각 12°.</summary>
        Right,

        /// <summary>중앙부 토출 — 좌우 대칭 사다리꼴. 기준각 20°, 절곡부 여유폭 TL4 적용.</summary>
        Center,
    }

    /// <summary>
    /// 단차 적용 여부. 종단제원에만 영향을 준다 (적용 시 TH4·TH5·TB6 추가).
    /// </summary>
    public enum DischargeStepType
    {
        /// <summary>단차 미적용 — 토출조 전체가 EL."TB"까지 한 높이로 올라간다.</summary>
        None,

        /// <summary>단차 적용 — TB6 폭 구간만 EL."TB"까지 올라가고 나머지는 지반고 부근에서 잘린다.</summary>
        Applied,
    }

    /// <summary>토출관로 형식.</summary>
    public enum OutletConduitType
    {
        /// <summary>암거 — OB(폭)·OH(높이) 사용. 암거 사이 벽두께 OT = 300.</summary>
        Culvert,

        /// <summary>PIPE — OD(직경) 사용. 2련 이상이면 PIPE 사이 간격 O1 = 1000.</summary>
        Pipe,
    }
}
