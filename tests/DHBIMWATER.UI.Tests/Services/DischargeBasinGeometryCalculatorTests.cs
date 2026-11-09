using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Application.Services;
using Xunit;

namespace DHBIMWATER.UI.Tests.Services;

/// <summary>
/// 수령자료 엑셀 4종(좌우측/중앙부 × 단차미적용/적용)의 예시값을 기대값으로 쓰는 회귀 테스트.
/// 원본: 토출조 input data\*.xlsx — 기대값 목록은 docs/164_토출조_제원입력_기획.md §7 참조.
///
/// ⚠ 엑셀 예시는 `토출관로 형식` 셀이 "암거 or PIPE" 라는 안내 문자열(미선택) 상태라 OT = 0, O1 = 0 이다.
///   중앙부 TL3 는 OT 에 의존하므로, 엑셀 상태를 재현하려면 overrides 로 OT·O1 을 0 으로 주입해야 한다.
///   (좌우측은 OT·O1 이 어떤 계산에도 쓰이지 않아 형식과 무관하게 일치한다)
///   → docs/164_토출조_확인필요사항.md C-2
/// </summary>
public class DischargeBasinGeometryCalculatorTests
{
    private const int Precision = 6;

    /// <summary>엑셀의 토출관로 형식 미선택 상태(OT = O1 = 0)를 재현한다.</summary>
    private static readonly Dictionary<string, double> ExcelUnselectedConduit = new()
    {
        ["OT"] = 0,
        ["O1"] = 0,
    };

    private static DischargeBasinInputDto Input(
        DischargeOutletPosition position,
        DischargeStepType step,
        double ta,
        double h7,
        OutletConduitType conduit = OutletConduitType.Culvert,
        int on = 2,
        double d = 1500,
        int n = 4,
        double pumpHwl = 5,
        double h3 = 1200,
        double b8PlusT5 = 5000)
        => new(
            Position: position,
            Step: step,
            ConduitType: conduit,
            TA: ta,
            Separation: 3,
            ON: on,
            OB: 2000,
            OH: 2000,
            OD: 2000,
            D: d,
            N: n,
            PumpHwl: pumpHwl,
            H3: h3,
            H7: h7,
            B8PlusT5: b8PlusT5);

    private static DischargeBasinInputDto LeftRight(DischargeStepType step = DischargeStepType.None)
        => Input(DischargeOutletPosition.Right, step, ta: 9.4, h7: 1000);

    private static DischargeBasinInputDto Center(DischargeStepType step = DischargeStepType.None)
        => Input(DischargeOutletPosition.Center, step, ta: 9.7, h7: 2000);

    // ── 1. 좌우측 · 단차미적용 ──────────────────────────────────────────

    [Fact]
    public void LeftRight_NoStep_MatchesExcel()
    {
        var g = DischargeBasinGeometryCalculator.Calculate(LeftRight());

        Assert.Equal(10.8, g.TB, Precision);
        Assert.Equal(6.0, g.TC, Precision);
        Assert.Equal(3.7, g.TE, Precision);
        Assert.Equal(3.2, g.TF, Precision);
        Assert.Equal(400, g.TT1, Precision);
        Assert.Equal(500, g.TT2, Precision);
        Assert.Equal(400, g.TT3, Precision);
        Assert.Equal(1000, g.TH1, Precision);
        Assert.Equal(4200, g.TH2, Precision);
        Assert.Equal(500, g.TH3, Precision);
        Assert.Equal(400, g.TCB1, Precision);
        Assert.Equal(400, g.TCH1, Precision);
        Assert.Equal(400, g.TGB1, Precision);
        Assert.Equal(300, g.TGH1, Precision);

        Assert.Equal(3800, g.TB1, Precision);
        Assert.Equal(8000, g.TB2, Precision);
        Assert.Equal(500, g.TB3, Precision);
        Assert.Equal(1500, g.TL1, Precision);
        Assert.Equal(3500, g.TL2, Precision);
        Assert.Equal(19500, g.TLT, Precision);
        Assert.Equal(2300, g.TOB1, Precision);
        Assert.Equal(2000, g.TOL1, Precision);
        Assert.Equal(3000, g.TOL2, Precision);
    }

    [Fact]
    public void LeftRight_NoStep_AngleAndProjectionsMatchExcel()
    {
        var g = DischargeBasinGeometryCalculator.Calculate(LeftRight());

        Assert.Equal(12.154941697222231, g.Theta, Precision);
        Assert.Equal(51.077470848611114, g.Theta1, Precision);
        Assert.Equal(38.922529151388886, g.Theta2!.Value, Precision);
        Assert.Equal(403.77385261167046, g.TB4, Precision);
        Assert.Equal(619.1584679962859, g.TB5!.Value, Precision);
        Assert.Equal(323.0190820893364, g.TT4, Precision);
        Assert.Equal(495.3267743970287, g.TT5!.Value, Precision);
    }

    // ── 2. 좌우측 · 단차적용 ────────────────────────────────────────────

    [Fact]
    public void LeftRight_Step_AddsStepValues()
    {
        var g = DischargeBasinGeometryCalculator.Calculate(LeftRight(DischargeStepType.Applied));

        Assert.Equal(2200, g.TH4!.Value, Precision);
        Assert.Equal(200, g.TH5!.Value, Precision);
        Assert.Equal(3700, g.TB6!.Value, Precision);
    }

    // ── 3. 중앙부 · 단차미적용 ──────────────────────────────────────────

    [Fact]
    public void Center_NoStep_MatchesExcel()
    {
        var g = DischargeBasinGeometryCalculator.Calculate(Center(), ExcelUnselectedConduit);

        Assert.Equal(11.1, g.TB, Precision);
        Assert.Equal(6.0, g.TC, Precision);
        Assert.Equal(2.7, g.TE, Precision);
        Assert.Equal(2.2, g.TF, Precision);
        Assert.Equal(500, g.TT1, Precision);
        Assert.Equal(600, g.TT2, Precision);
        Assert.Equal(5500, g.TH2, Precision);
        Assert.Equal(500, g.TH3, Precision);

        Assert.Equal(3800, g.TB1, Precision);
        Assert.Equal(600, g.TB3, Precision);
        Assert.Equal(6750, g.TL3!.Value, Precision);
        Assert.Equal(1000, g.TL4!.Value, Precision);
        Assert.Equal(6300, g.TB2, Precision);
    }

    [Fact]
    public void Center_NoStep_AngleAndProjectionsMatchExcel()
    {
        var g = DischargeBasinGeometryCalculator.Calculate(Center(), ExcelUnselectedConduit);

        Assert.Equal(20.32313682966294, g.Theta, Precision);
        Assert.Equal(55.16156841483147, g.Theta1, Precision);
        Assert.Equal(348.0066684773322, g.TT4, Precision);
        Assert.Equal(417.6080021727986, g.TB4, Precision);
        Assert.Equal(1089.6180048887968, g.TL6!.Value, Precision);
        Assert.Equal(1197.1596107553532, g.TL8!.Value, Precision);
        Assert.Equal(7160.381995111204, g.TL5!.Value, Precision);
        Assert.Equal(7652.840389244647, g.TL7!.Value, Precision);
    }

    // ── 4. 중앙부 · 단차적용 ────────────────────────────────────────────

    [Fact]
    public void Center_Step_AddsStepValues()
    {
        var g = DischargeBasinGeometryCalculator.Calculate(
            Center(DischargeStepType.Applied), ExcelUnselectedConduit);

        Assert.Equal(3200, g.TH4!.Value, Precision);
        Assert.Equal(200, g.TH5!.Value, Precision);
        Assert.Equal(3600, g.TB6!.Value, Precision);
    }

    // ── 5. 유형별 항목 가시성 ───────────────────────────────────────────

    [Fact]
    public void LeftRight_PopulatesOnlyLeftRightFields()
    {
        var g = DischargeBasinGeometryCalculator.Calculate(LeftRight());

        Assert.NotNull(g.Theta2);
        Assert.NotNull(g.TB5);
        Assert.NotNull(g.TT5);

        Assert.Null(g.TL3);
        Assert.Null(g.TL4);
        Assert.Null(g.TL5);
        Assert.Null(g.TL6);
        Assert.Null(g.TL7);
        Assert.Null(g.TL8);
    }

    [Fact]
    public void Center_PopulatesOnlyCenterFields()
    {
        var g = DischargeBasinGeometryCalculator.Calculate(Center());

        Assert.Null(g.Theta2);
        Assert.Null(g.TB5);
        Assert.Null(g.TT5);

        Assert.NotNull(g.TL3);
        Assert.NotNull(g.TL4);
        Assert.NotNull(g.TL5);
        Assert.NotNull(g.TL6);
        Assert.NotNull(g.TL7);
        Assert.NotNull(g.TL8);
    }

    [Fact]
    public void NoStep_LeavesStepFieldsNull()
    {
        var g = DischargeBasinGeometryCalculator.Calculate(LeftRight());

        Assert.Null(g.TH4);
        Assert.Null(g.TH5);
        Assert.Null(g.TB6);
    }

    // ── 6. 권고안 덮어쓰기 전파 ─────────────────────────────────────────

    [Fact]
    public void Override_PropagatesToDependents()
    {
        var overrides = new Dictionary<string, double> { ["TT1"] = 600 };

        var g = DischargeBasinGeometryCalculator.Calculate(LeftRight(), overrides);

        Assert.Equal(600, g.TT1, Precision);
        Assert.Equal(700, g.TT2, Precision);   // TT2 = TT1 + 100
        Assert.Equal(700, g.TB3, Precision);   // TB3 = TT2
        Assert.Equal(600, g.TCB1, Precision);  // TCB1 = TT1
        Assert.Equal(600, g.TCH1, Precision);
        Assert.Equal(600, g.TGB1, Precision);

        // TT4 = TT1 / tan(θ1) — 덮어쓴 TT1 기준으로 재계산된다.
        var baseline = DischargeBasinGeometryCalculator.Calculate(LeftRight());
        Assert.Equal(baseline.TT4 * 600 / 400, g.TT4, Precision);
    }

    [Fact]
    public void Override_OnItselfIsHonoredAndFlowsIntoStepWidth()
    {
        var overrides = new Dictionary<string, double> { ["TB1"] = 4000 };

        var g = DischargeBasinGeometryCalculator.Calculate(
            LeftRight(DischargeStepType.Applied), overrides);

        Assert.Equal(4000, g.TB1, Precision);
        Assert.Equal(3900, g.TB6!.Value, Precision);  // TB6 = TB1 - 100 (좌우측)
    }

    // ── 7. 분기 조건 ────────────────────────────────────────────────────

    [Theory]
    [InlineData(1350, 400)]
    [InlineData(1351, 500)]
    [InlineData(1650, 500)]
    [InlineData(1651, 600)]
    public void Th3_SwitchesOnPumpDiameter(double d, double expected)
    {
        var input = Input(DischargeOutletPosition.Right, DischargeStepType.None,
                          ta: 9.4, h7: 1000, d: d);

        var g = DischargeBasinGeometryCalculator.Calculate(input);

        Assert.Equal(expected, g.TH3, Precision);
    }

    [Fact]
    public void Culvert_SetsOtAndClearsO1()
    {
        var input = Input(DischargeOutletPosition.Right, DischargeStepType.None,
                          ta: 9.4, h7: 1000, conduit: OutletConduitType.Culvert);

        var g = DischargeBasinGeometryCalculator.Calculate(input);

        Assert.Equal(300, g.OT, Precision);
        Assert.Equal(0, g.O1, Precision);
    }

    [Fact]
    public void Pipe_SingleRun_ClearsBothSpacings()
    {
        var input = Input(DischargeOutletPosition.Right, DischargeStepType.None,
                          ta: 9.4, h7: 1000, conduit: OutletConduitType.Pipe, on: 1);

        var g = DischargeBasinGeometryCalculator.Calculate(input);

        Assert.Equal(0, g.OT, Precision);
        Assert.Equal(0, g.O1, Precision);
    }

    [Fact]
    public void Pipe_MultipleRuns_SetsO1()
    {
        var input = Input(DischargeOutletPosition.Right, DischargeStepType.None,
                          ta: 9.4, h7: 1000, conduit: OutletConduitType.Pipe, on: 2);

        var g = DischargeBasinGeometryCalculator.Calculate(input);

        Assert.Equal(0, g.OT, Precision);
        Assert.Equal(1000, g.O1, Precision);
    }

    /// <summary>
    /// 중앙부 TL3 의 암거 분기는 OB(폭)가 아닌 OH(높이)를 참조한다 — 엑셀 원본 그대로.
    /// OB ≠ OH 로 두면 OH 쪽이 쓰인다는 것이 드러난다.
    /// → docs/164_토출조_확인필요사항.md A-2
    /// </summary>
    [Fact]
    public void Center_Culvert_Tl3_UsesOhNotOb()
    {
        var input = Center() with { OB = 1000, OH = 3000 };

        var g = DischargeBasinGeometryCalculator.Calculate(input);

        // TL3 = (TLT - ON*OH - (ON-1)*OT - 2*TL4) / 2
        //     = (19500 - 2*3000 - 1*300 - 2*1000) / 2 = 5600
        Assert.Equal(5600, g.TL3!.Value, Precision);
    }
}
