namespace DHBIMWATER.UI.ViewModels.Modeling
{
    /// <summary>
    /// 펌프장 창 입력값 저장본(문서 ExtensibleStorage에 JSON). 창 다시 열면 그대로 복원.
    /// 계산값(L2·H4·T4 등)도 사용자가 고쳤을 수 있어 화면 값 전부 담음.
    /// 칸 추가 시 여기 속성 추가 — 옛 저장본엔 없으니 null이면 현재 값(기본값) 유지.
    /// 엑셀 제작사 목록은 저장 안 함(파일 의존). 엑셀에서 받은 밸브받침 치수만 담음.
    /// </summary>
    internal sealed class PumpingStationInputSnapshot
    {
        public int Version { get; set; } = 1;

        // 설계조건
        public string? PumpingStationType { get; set; }
        public string? EntranceType { get; set; }
        public double? D { get; set; }
        public double? HD { get; set; }
        public int? N { get; set; }
        public double? LWL { get; set; }
        public double? HWL { get; set; }
        public bool? HasCheckValve { get; set; }
        public double? SupportBlockWidth { get; set; }
        public double? SupportBlockHeight { get; set; }
        public bool? IsRectangularOpening { get; set; }
        public double? ValveBaseWidth { get; set; }
        public double? ValveBaseLength { get; set; }
        public double? ValveBaseHeight { get; set; }
        public double? ValveBasePlacement { get; set; }

        // 종단제원
        public double? B1 { get; set; }
        public double? B2 { get; set; }
        public double? B3 { get; set; }
        public double? B4 { get; set; }
        public double? B5 { get; set; }
        public double? B6 { get; set; }
        public double? B7 { get; set; }
        public double? B7Base { get; set; }
        public double? H1 { get; set; }
        public double? H3 { get; set; }
        public double? H4 { get; set; }
        public double? H5 { get; set; }
        public double? H6 { get; set; }
        public double? H7 { get; set; }
        public string? Theta { get; set; }
        public double? L1 { get; set; }
        public double? L2 { get; set; }
        public double? L3 { get; set; }
        public double? L4 { get; set; }
        public double? OB1 { get; set; }
        public double? OH1 { get; set; }
        public int? NS { get; set; }
        public double? HS { get; set; }
        public int? NS1 { get; set; }
        public double? HS1 { get; set; }

        // 평면제원
        public double? B8 { get; set; }
        public double? B9 { get; set; }
        public double? B10 { get; set; }
        public double? L5 { get; set; }

        // 부재 두께·보
        public double? T1 { get; set; }
        public double? T2 { get; set; }
        public double? T3 { get; set; }
        public double? T4 { get; set; }
        public double? T5 { get; set; }
        public double? T5Prime { get; set; }
        public double? T6 { get; set; }
        public double? GB1 { get; set; }
        public double? GH1 { get; set; }
        public double? HB1 { get; set; }
        public double? HH1 { get; set; }

        // 토출조(같은 창). 옛 저장본엔 없음 → null이면 토출조 기본값 유지
        public DischargeBasinInputSnapshot? DischargeBasin { get; set; }
    }
}
