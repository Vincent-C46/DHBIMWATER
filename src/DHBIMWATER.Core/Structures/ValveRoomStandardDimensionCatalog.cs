namespace DHBIMWATER.Core.Structures;

/// <summary>표준도 밸브실 제원 1행. 치수 mm. 길이 = 관 방향, 폭 = 직각, 높이 = 표 내부 높이(참고값).</summary>
/// <param name="RoomKind">표 이름 유형. <see cref="ValveRoomStandardDimensionCatalog"/> 상수 중 하나.</param>
/// <param name="Variant">비고 칸 형식(A형·B형·전자식·초음파). 비고 없는 표면 null.</param>
/// <param name="Group">표 "구분" 번호.</param>
/// <param name="Section">표 "계산단면(m)" 문자 그대로. 예) 1.5x1.8</param>
public sealed record ValveRoomStandardDimension(
    string RoomKind,
    string? Variant,
    int Group,
    double DiameterMm,
    double LengthMm,
    double WidthMm,
    double HeightMm,
    string Section);

/// <summary>상수도시설 밸브실 표준도 설계설명서 표 4.1~4.6 제원(docs/02 PDF, docs/304).
/// <para>타이밸브실 표 = 표준도에 없음. 이토밸브실 표 복사 + 길이·폭 맞바꿈(2026-11-11 사용자 결정).</para>
/// <para>새 유형 추가 = <see cref="Default"/>에 표 1개 추가. 조회 코드 수정 불필요.</para></summary>
public static class ValveRoomStandardDimensionCatalog
{
    public const string Sluice = "제수밸브실";
    public const string Mud = "이토밸브실";
    public const string Tie = "타이밸브실";
    public const string PressureReducing = "감압밸브실";
    public const string Air = "공기밸브실";
    public const string FlowMeter = "유량계실";
    public const string FlowMeterWithLab = "유량계실및실험실겸용";

    public const string TypeA = "A형";
    public const string TypeB = "B형";
    public const string Electromagnetic = "전자식";
    public const string Ultrasonic = "초음파";

    /// <summary>내장 표준표. 사용자 편집본은 문서 DataStorage에 따로 저장된다(docs/312) — 이 표는 안 바뀜.</summary>
    public static ValveRoomDimensionTable Default { get; } = new(
    [
        .. SluiceTable(),
        .. MudTable(),
        .. TieTable(),
        .. PressureReducingTable(),
        .. AirTable(),
        .. FlowMeterTable(),
        .. FlowMeterWithLabTable(),
    ]);

    public static IReadOnlyList<ValveRoomStandardDimension> Entries => Default.Entries;

    /// <summary>표가 있는 유형 이름. 표 순서 유지.</summary>
    public static IReadOnlyList<string> RoomKinds { get; } = Default.RoomKinds;

    /// <summary>유형의 비고 형식 목록. 표 순서라 첫 값이 기본값. 형식 없는 유형이면 빈 목록.</summary>
    public static IReadOnlyList<string> Variants(string? roomKind) => Default.Variants(roomKind);

    /// <summary>내장 표준표에서 찾는다. 규칙은 <see cref="ValveRoomDimensionTable.Find"/>.</summary>
    public static ValveRoomStandardDimension? Find(string? roomKind, double diameterMm, string? variant = null)
        => Default.Find(roomKind, diameterMm, variant);

    // 표 4.1 제수밸브실. 구분 1 = A형, 2~12 = B형.
    private static IEnumerable<ValveRoomStandardDimension> SluiceTable()
    {
        var a = new TableBuilder(Sluice, TypeA);
        a.Rows(1, "1.5x1.8", 2400, 1500, 1800, 80, 100, 125, 150, 200, 250, 300, 350);
        var b = new TableBuilder(Sluice, TypeB);
        b.Rows(2, "1.5x1.8", 3400, 1500, 1800, 80, 100, 125, 150, 200, 250, 300, 350);
        b.Rows(3, "2.3x2.3", 4100, 2300, 2000, 400, 450, 500);
        b.Row(3, "2.3x2.3", 600, 4100, 2300, 2200);
        b.Row(3, "2.3x2.3", 700, 4100, 2300, 2300);
        b.Rows(4, "3.5x2.8", 4500, 3500, 2800, 800, 900);
        b.Row(5, "4.0x3.2", 1000, 4700, 4000, 3100);
        b.Row(5, "4.0x3.2", 1100, 4700, 4000, 3200);
        b.Row(6, "4.3x3.5", 1200, 4700, 4300, 3300);
        b.Row(6, "4.3x3.5", 1350, 4700, 4300, 3500);
        b.Row(7, "5.0x4.0", 1500, 4800, 5000, 3600);
        b.Row(8, "5.0x4.0", 1600, 7000, 5000, 3700);
        b.Row(8, "5.0x4.0", 1650, 7000, 5000, 4000);
        b.Row(9, "5.3x4.3", 1800, 7000, 5300, 4100);
        b.Rows(9, "5.3x4.3", 7000, 5300, 4300, 1900, 2000);
        b.Row(10, "6.0x4.6", 2100, 7000, 6000, 4400);
        b.Row(10, "6.0x4.6", 2200, 7000, 6000, 4500);
        b.Row(10, "6.0x4.6", 2300, 7000, 6000, 4600);
        b.Row(11, "6.5x4.9", 2400, 7000, 6500, 4700);
        b.Row(11, "6.5x4.9", 2500, 7000, 6500, 4800);
        b.Row(11, "6.5x4.9", 2600, 7000, 6500, 4900);
        b.Row(12, "7.0x5.4", 2700, 7000, 7000, 5000);
        b.Row(12, "7.0x5.4", 2800, 7000, 7000, 5100);
        b.Row(12, "7.0x5.4", 2900, 7000, 7000, 5300);
        b.Row(12, "7.0x5.4", 3000, 7000, 7000, 5400);
        return [.. a.Entries, .. b.Entries];
    }

    // 표 4.2 이토밸브실. 비고 없음.
    private static IEnumerable<ValveRoomStandardDimension> MudTable()
    {
        var t = new TableBuilder(Mud, null);
        t.Rows(1, "1.5x2.0", 1500, 1500, 1800, 80, 100, 125, 150, 200, 250);
        t.Row(1, "1.5x2.0", 300, 1500, 1500, 1900);
        t.Row(2, "1.5x3.0", 350, 2000, 1500, 2000);
        t.Row(2, "1.5x3.0", 400, 2000, 1500, 2400);
        t.Row(2, "1.5x3.0", 450, 2000, 1500, 2500);
        t.Row(2, "1.5x3.0", 500, 2000, 1500, 2700);
        t.Row(2, "1.5x3.0", 600, 2000, 1500, 3000);
        t.Row(3, "2.0x3.6", 700, 2500, 2000, 3200);
        t.Row(3, "2.0x3.6", 800, 2500, 2000, 3600);
        t.Row(4, "2.5x4.5", 900, 3000, 2500, 3800);
        t.Row(4, "2.5x4.5", 1000, 3000, 2500, 4100);
        t.Row(4, "2.5x4.5", 1100, 3000, 2500, 4300);
        t.Row(4, "2.5x4.5", 1200, 3000, 2500, 4500);
        return t.Entries;
    }

    // 타이밸브실. 표준도 표 없음. 이토밸브실 표 복사, 길이·폭만 맞바꿈. 구분·계산단면·높이는 이토 그대로.
    private static IEnumerable<ValveRoomStandardDimension> TieTable()
        => MudTable().Select(x => x with { RoomKind = Tie, LengthMm = x.WidthMm, WidthMm = x.LengthMm });

    // 표 4.3 감압밸브실. 비고 없음.
    private static IEnumerable<ValveRoomStandardDimension> PressureReducingTable()
    {
        var t = new TableBuilder(PressureReducing, null);
        t.Rows(1, "1.5x1.8", 3300, 1500, 1800, 80, 100);
        t.Rows(2, "1.7x1.8", 4200, 1700, 1800, 150, 200);
        t.Rows(3, "2.2x1.8", 5600, 2200, 1800, 250, 300, 350);
        t.Row(4, "2.5x1.8", 400, 6400, 2500, 1800);
        t.Row(5, "2.7x1.8", 500, 7300, 2700, 1800);
        t.Row(6, "3.0x1.8", 600, 8000, 3000, 1800);
        return t.Entries;
    }

    // 표 4.4 공기밸브실. 비고 없음. 구분 2~4 계산단면 병합(2.0x1.9).
    private static IEnumerable<ValveRoomStandardDimension> AirTable()
    {
        var t = new TableBuilder(Air, null);
        t.Rows(1, "1.5x1.8", 1500, 1500, 1800, 80, 100, 125, 150, 200, 250, 300, 350, 400, 450, 500, 600, 700);
        t.Rows(2, "2.0x1.9", 2000, 2000, 1800, 800, 900, 1000, 1100, 1200, 1350);
        t.Rows(3, "2.0x1.9", 2000, 2000, 1800, 1500, 1600);
        t.Rows(4, "2.0x1.9", 2000, 2200, 1900, 1650, 1800, 1900, 2000, 2100, 2200, 2300, 2400, 2500, 2600, 2700, 2800, 2900, 3000);
        return t.Entries;
    }

    // 표 4.5 유량계실. 구분 1~3 전자식, 4~15 초음파. 계산단면은 병합 셀이 구분 경계와 어긋나 행마다 PDF 그대로 적음.
    private static IEnumerable<ValveRoomStandardDimension> FlowMeterTable()
    {
        var e = new TableBuilder(FlowMeter, Electromagnetic);
        e.Rows(1, "2.0x1.8", 3800, 2000, 1800, 80, 100, 125, 150, 200);
        e.Row(2, "2.5x1.9", 250, 5000, 2500, 1800);
        e.Row(2, "2.5x1.9", 300, 5000, 2500, 1900);
        e.Rows(3, "3.2x1.8", 5600, 3200, 1800, 350, 400);
        var u = new TableBuilder(FlowMeter, Ultrasonic);
        u.Rows(4, "1.5x1.8", 1500, 1500, 1800, 80, 100, 125, 150, 200, 250, 300, 350);
        u.Row(4, "2.3x2.3", 400, 1500, 1500, 1800);
        u.Rows(5, "2.3x2.3", 1500, 1500, 1800, 450, 500, 600, 700);
        u.Row(6, "3.5x2.8", 800, 2000, 2000, 2000);
        u.Row(7, "3.5x2.8", 900, 2000, 2500, 2100);
        u.Row(7, "4.0x3.2", 1000, 2000, 2500, 2200);
        u.Row(8, "4.0x3.2", 1100, 2500, 2500, 2300);
        u.Row(8, "4.3x3.5", 1200, 2500, 2500, 2400);
        u.Row(9, "4.3x3.5", 1350, 2500, 3000, 2600);
        u.Row(9, "5.0x4.0", 1500, 2500, 3000, 2700);
        u.Row(10, "5.0x4.0", 1600, 3000, 3000, 2800);
        u.Row(10, "5.0x4.0", 1650, 3000, 3000, 3100);
        u.Row(10, "5.3x4.3", 1800, 3000, 3000, 3200);
        u.Row(11, "5.3x4.3", 1900, 3000, 3500, 3300);
        u.Row(11, "5.3x4.3", 2000, 3000, 3500, 3400);
        u.Row(12, "6.0x4.6", 2100, 3500, 3500, 3500);
        u.Row(12, "6.0x4.6", 2200, 3500, 3500, 3600);
        u.Row(12, "6.0x4.6", 2300, 3500, 3500, 3700);
        u.Row(13, "6.5x4.9", 2400, 3500, 4000, 3800);
        u.Row(13, "6.5x4.9", 2500, 3500, 4000, 3900);
        u.Row(14, "6.5x4.9", 2600, 4000, 4000, 4000);
        u.Row(14, "7.0x5.4", 2700, 4000, 4000, 4100);
        u.Row(14, "7.0x5.4", 2800, 4000, 4000, 4200);
        u.Row(15, "7.0x5.4", 2900, 4000, 4200, 4300);
        u.Row(15, "7.0x5.4", 3000, 4000, 4200, 4400);
        return [.. e.Entries, .. u.Entries];
    }

    // 표 4.6 유량계실 및 실험실 겸용. 구분 1~3 전자식, 4~17 초음파.
    private static IEnumerable<ValveRoomStandardDimension> FlowMeterWithLabTable()
    {
        var e = new TableBuilder(FlowMeterWithLab, Electromagnetic);
        e.Rows(1, "2.0x1.8", 4200, 2000, 1800, 80, 100, 125);
        e.Rows(1, "2.0x1.8", 4800, 2000, 1800, 150, 200);
        e.Rows(2, "2.5x1.8", 5000, 2500, 1800, 250, 300);
        e.Row(3, "3.2x2.4", 350, 6800, 3200, 2000);
        e.Row(3, "3.2x2.4", 400, 6800, 3200, 2400);
        var u = new TableBuilder(FlowMeterWithLab, Ultrasonic);
        u.Rows(4, "1.5x1.8", 1500, 1500, 1800, 80, 100, 125, 150, 200, 250, 300);
        u.Rows(5, "1.5x1.8", 2000, 1500, 1800, 350, 400);
        u.Rows(6, "1.5x1.8", 2500, 1500, 1800, 450, 500, 600);
        u.Row(7, "1.5x1.8", 700, 3000, 1500, 1800);
        u.Row(8, "2.0x2.0", 800, 3500, 2000, 2000);
        u.Row(9, "2.5x2.4", 900, 3500, 2500, 2100);
        u.Row(9, "2.5x2.4", 1000, 3500, 2500, 2200);
        u.Row(10, "2.5x2.4", 1100, 4000, 2500, 2300);
        u.Row(10, "2.5x2.4", 1200, 4000, 2500, 2400);
        u.Row(11, "3.0x2.7", 1350, 4500, 3000, 2600);
        u.Row(11, "3.0x2.7", 1500, 4500, 3000, 2700);
        u.Row(12, "3.0x3.2", 1600, 5000, 3000, 2800);
        u.Row(12, "3.0x3.2", 1650, 5000, 3000, 3100);
        u.Row(12, "3.0x3.2", 1800, 5500, 3000, 3200);
        u.Row(13, "3.5x3.7", 1900, 5500, 3500, 3300);
        u.Row(13, "3.5x3.7", 2000, 5500, 3500, 3400);
        u.Row(14, "3.5x3.7", 2100, 6000, 3500, 3500);
        u.Row(14, "3.5x3.7", 2200, 6000, 3500, 3600);
        u.Row(14, "3.5x3.7", 2300, 6500, 3500, 3700);
        u.Row(15, "4.0x4.2", 2400, 6500, 4000, 3800);
        u.Row(15, "4.0x4.2", 2500, 6500, 4000, 3900);
        u.Row(16, "4.0x4.2", 2600, 7000, 4000, 4000);
        u.Row(16, "4.0x4.2", 2700, 7000, 4000, 4100);
        u.Row(16, "4.0x4.2", 2800, 7500, 4000, 4200);
        u.Row(17, "4.2x4.4", 2900, 7500, 4200, 4300);
        u.Row(17, "4.2x4.4", 3000, 7500, 4200, 4400);
        return [.. e.Entries, .. u.Entries];
    }

    /// <summary>표 1개(유형 + 형식) 적재 도우미. 같은 치수 여러 관경은 Rows, 한 관경은 Row.</summary>
    private sealed class TableBuilder(string roomKind, string? variant)
    {
        public List<ValveRoomStandardDimension> Entries { get; } = [];

        public void Row(int group, string section, double diameterMm, double lengthMm, double widthMm, double heightMm)
            => Entries.Add(new ValveRoomStandardDimension(roomKind, variant, group, diameterMm, lengthMm, widthMm, heightMm, section));

        public void Rows(int group, string section, double lengthMm, double widthMm, double heightMm, params double[] diametersMm)
        {
            foreach (var diameter in diametersMm) Row(group, section, diameter, lengthMm, widthMm, heightMm);
        }
    }
}
