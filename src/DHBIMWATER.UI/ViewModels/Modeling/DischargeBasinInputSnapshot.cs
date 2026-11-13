namespace DHBIMWATER.UI.ViewModels.Modeling
{
    /// <summary>
    /// 토출조 입력값 저장본. 같은 창(펌프장)이라 펌프장 저장본 안에 함께 담음(<see cref="PumpingStationInputSnapshot.DischargeBasin"/>).
    /// 펌프장 연동값(D·N 등)은 펌프장 쪽 값에서 다시 밀어 넣으니 안 담음.
    /// Overrides = 사용자가 덮어쓴 권고안(키 = 행 Key). 칸 추가 시 null이면 현재 값 유지.
    /// </summary>
    internal sealed class DischargeBasinInputSnapshot
    {
        public string? Position { get; set; }
        public string? Step { get; set; }
        public string? Conduit { get; set; }
        public double? TA { get; set; }
        public double? Separation { get; set; }
        public int? ON { get; set; }
        public double? OB { get; set; }
        public double? OH { get; set; }
        public double? OD { get; set; }
        public Dictionary<string, double>? Overrides { get; set; }
    }
}
