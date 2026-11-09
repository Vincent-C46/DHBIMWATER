using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;

namespace DHBIMWATER.Application.Services
{
    /// <summary>
    /// 토출조 권고안(자동 계산) 산출기.
    ///
    /// 원본은 수령자료 엑셀 4종(좌우측/중앙부 × 단차미적용/적용)의 "● 권고안" 표이며,
    /// 수식·산식근거 전문은 docs/164_토출조_제원입력_기획.md §3 에 정리돼 있다.
    /// 엑셀 수식과 산식근거 주석이 어긋나는 항목은 **엑셀 수식을 그대로** 따르고
    /// docs/164_토출조_확인필요사항.md 에 기록해 두었다 (해당 지점마다 ⚠ 주석).
    /// </summary>
    public static class DischargeBasinGeometryCalculator
    {
        /// <summary>상부슬래브 두께 — 400mm 고정. 구조적 최적설계.</summary>
        public const double FixedTT3 = 400;

        /// <summary>거더 돌출높이 — 300mm 고정. 구조적 최적설계.</summary>
        public const double FixedTGH1 = 300;

        /// <summary>포장 여유 — 200mm 고정. 콘크리트 포장 등을 고려.</summary>
        public const double FixedTH5 = 200;

        /// <summary>절곡부 여유폭 — 1000mm 고정. 벽체 절곡부 응력집중 대비. 중앙부 전용.</summary>
        public const double FixedTL4 = 1000;

        /// <summary>벽체 최소 두께 — 400mm.</summary>
        public const double MinimumWallThickness = 400;

        /// <summary>좌우측 토출의 벌어짐 기준각. 「KDS 67 30 25」 4.3.1.4.</summary>
        public const double LeftRightBaseAngleDeg = 12;

        /// <summary>중앙부 토출의 벌어짐 기준각. 「KDS 67 30 25」 4.3.1.4.</summary>
        public const double CenterBaseAngleDeg = 20;

        /// <summary>
        /// 토출조 권고안을 계산한다.
        /// </summary>
        /// <param name="input">직접 입력 + 펌프장 연동값</param>
        /// <param name="overrides">
        /// 사용자가 권고안을 덮어쓴 항목. 키는 엑셀 기호(예 "TT1", "TB1"), 각도만 "Theta"/"Theta1"/"Theta2".
        /// 각 단계에서 오버라이드가 있으면 그 값을 채택하므로 하위 항목으로 자동 전파된다
        /// (엑셀 셀 참조와 동일한 동작 — docs/164_토출조_확인필요사항.md C-1).
        /// </param>
        public static DischargeBasinGeometryDto Calculate(
            DischargeBasinInputDto input,
            IReadOnlyDictionary<string, double>? overrides = null)
        {
            // 오버라이드가 있으면 계산값 대신 그 값을 채택한다.
            double V(string key, double computed)
                => overrides != null && overrides.TryGetValue(key, out var o) ? o : computed;

            // 각도 전용 — DTO에는 도(°)로 담고 이후 계산은 라디안으로 이어간다.
            // 오버라이드가 없으면 계산된 라디안을 그대로 써서 불필요한 도↔라디안 왕복 오차를 피한다.
            (double Rad, double Deg) Angle(string key, double computedRad)
                => overrides != null && overrides.TryGetValue(key, out var o)
                    ? (Deg2Rad(o), o)
                    : (computedRad, Rad2Deg(computedRad));

            var d = input.D;
            var n = input.N;

            // ══════════════════════════════════════════════════════════
            // [1단계] 종단제원
            // ══════════════════════════════════════════════════════════

            // 토출관 하부 두께. 기전설계업무편람(2001), 농업생산기반정비사업계획설계기준-배수편(2005),
            // 첨단기술 스마트핸드북(기계, 2020)의 최대값 적용.
            var tt3 = V("TT3", FixedTT3);
            var th3 = V("TH3", d <= 1350 ? 400 : (d <= 1650 ? 500 : 600));

            // 토출관 BOP. (펌프장 상단고 - H7 - D)의 m 표기.
            var te = V("TE", input.PumpHwl + (input.H3 - input.H7 - d) / 1000.0);

            // 토출조 바닥고.
            var tf = V("TF", te - th3 / 1000.0);

            var th2 = V("TH2", (input.TA - te) * 1000.0 - d);

            // 여유고 1m 적용 + 전체 높이를 정치수화 하기 위한 치수 추가.
            var th1 = V("TH1", 1000 + RoundUp((th2 + d + th3) / 100.0) * 100 - (th2 + d + th3));

            var tb = V("TB", input.TA + (th1 + tt3) / 1000.0);

            // 지반고. (펌프장 상단고 - 200mm)의 m 표기.
            var tc = V("TC", input.PumpHwl + (input.H3 - 200) / 1000.0);

            // 벽체 두께. 토압 높이의 10%의 정치수 반영(ROUNDUP) 및 돌출높이 5m 마다 100mm 추가.
            // 또한 최소 400mm 적용. 구조적 최적설계.
            var tt1 = V("TT1", Math.Max(
                RoundUp(tc - tf) * 100 + RoundDown((tb - tc) / 5.0) * 100,
                MinimumWallThickness));

            // 바닥슬래브 두께 = 벽체 두께 + 100mm. 구조적 최적설계.
            var tt2 = V("TT2", tt1 + 100);

            var tcb1 = V("TCB1", tt1);  // 기둥 가로
            var tch1 = V("TCH1", tt1);  // 기둥 세로
            var tgb1 = V("TGB1", tt1);  // 거더 폭 — 기둥폭과 동일폭
            var tgh1 = V("TGH1", FixedTGH1);

            var hasStep = input.Step == DischargeStepType.Applied;

            double? th5 = null;
            double? th4 = null;
            if (hasStep)
            {
                th5 = V("TH5", FixedTH5);
                th4 = V("TH4", (tc - tf) * 1000.0 - th5.Value - tt3);
            }

            // ══════════════════════════════════════════════════════════
            // [2단계] 평면제원 — 공통
            // ══════════════════════════════════════════════════════════

            // 「KDS 67 30 25 양배수장 구조 설계, P43, 4.3.1.4 배출수조의 수리설계」에 따라 2.5D 이상.
            var tb1 = V("TB1", RoundUp(2.5 * d / 100.0) * 100);

            // 바닥슬래브의 두께와 동일 폭 적용.
            var tb3 = V("TB3", tt2);

            // ⚠ 엑셀 원본 수식 그대로(1.0D). 산식근거 주석은 "토출관 중심에서 1.5D 이상"이라 불일치.
            //   → docs/164_토출조_확인필요사항.md A-1 참조
            var tl1 = V("TL1", RoundUp(1.0 * d / 100.0) * 100);

            // 펌프장의 펌프 순간격.
            var tl2 = V("TL2", input.B8PlusT5 - d);

            // 내공 총길이.
            var tlt = V("TLT", 2 * tl1 + n * d + (n - 1) * tl2);

            // 암거일 경우 적용. 토출관로 규모는 대부분 크지 않으므로 300으로 고정.
            var ot = V("OT", input.ConduitType == OutletConduitType.Culvert ? 300 : 0);

            // 여러 본의 PIPE를 설치할 경우 PIPE 사이 작업공간 1m 확보.
            var o1 = V("O1", input.ConduitType == OutletConduitType.Pipe && input.ON >= 2 ? 1000 : 0);

            // 토출 밸브 유지관리용 상부 개구부. 제작사별 상이.
            var tob1 = V("TOB1", RoundUp(1.5 * d / 100.0) * 100);
            var tol1 = V("TOL1", RoundUp((d + 500) / 100.0) * 100);
            var tol2 = V("TOL2", tol1 + 1000);

            double tb2, tb4, tt4;
            double thetaDeg, theta1Deg;
            double? theta2Deg = null, tb5 = null, tt5 = null;
            double? tl3 = null, tl4 = null, tl5 = null, tl6 = null, tl7 = null, tl8 = null;

            if (input.Position != DischargeOutletPosition.Center)
            {
                // ── [2-L] 좌우측 토출 ──
                tb2 = V("TB2", RoundUp((tb1 + tlt * Math.Tan(Deg2Rad(LeftRightBaseAngleDeg))) / 100.0) * 100);

                // TB2 정치수 적용에 의한 각도 보정.
                var (theta, tDeg) = Angle("Theta", SafeAtan(tb2 - tb1, tlt));
                thetaDeg = tDeg;

                var (theta1, t1Deg) = Angle("Theta1", (Math.PI / 2 + theta) / 2);
                theta1Deg = t1Deg;

                // ⚠ 엑셀 계산식 표기는 "90°-θ" 이지만 실제 수식은 90-θ1 이다.
                //   → docs/164_토출조_확인필요사항.md A-3 (표기 오류로 판단, 실제 수식 채택)
                var (theta2, t2Deg) = Angle("Theta2", Math.PI / 2 - theta1);
                theta2Deg = t2Deg;

                tb4 = V("TB4", SafeDivByTan(tb3, theta1));
                tb5 = V("TB5", SafeDivByTan(tb3, theta2));
                tt4 = V("TT4", SafeDivByTan(tt1, theta1));
                tt5 = V("TT5", SafeDivByTan(tt1, theta2));
            }
            else
            {
                // ── [2-C] 중앙부 토출 ──
                tl4 = V("TL4", FixedTL4);

                // ⚠ 암거 분기가 OB(폭)가 아닌 OH(높이)를 참조한다. 엑셀 원본 수식 그대로.
                //   → docs/164_토출조_확인필요사항.md A-2 참조
                var tl3Computed = input.ConduitType == OutletConduitType.Culvert
                    ? (tlt - input.ON * input.OH - (input.ON - 1) * ot - 2 * tl4.Value) / 2
                    : (tlt - input.ON * input.OD - (input.ON - 1) * o1 - 2 * tl4.Value) / 2;
                var tl3Value = V("TL3", tl3Computed);
                tl3 = tl3Value;

                tb2 = V("TB2", RoundUp((tb1 + tl3Value * Math.Tan(Deg2Rad(CenterBaseAngleDeg))) / 100.0) * 100);

                var (theta, tDeg) = Angle("Theta", SafeAtan(tb2 - tb1, tl3Value));
                thetaDeg = tDeg;

                var (theta1, t1Deg) = Angle("Theta1", (Math.PI / 2 + theta) / 2);
                theta1Deg = t1Deg;

                tt4 = V("TT4", SafeDivByTan(tt1, theta1));
                tb4 = V("TB4", SafeDivByTan(tb3, theta1));

                var tl6Value = V("TL6", tl4.Value + tt1 * Math.Tan(theta / 2));
                var tl8Value = V("TL8", tl4.Value + (tt1 + tb3) * Math.Tan(theta / 2));
                tl6 = tl6Value;
                tl8 = tl8Value;
                tl5 = V("TL5", tt1 + tl3Value + tl4.Value - tl6Value);
                tl7 = V("TL7", tt1 + tb3 + tl3Value + tl4.Value - tl8Value);
            }

            // ══════════════════════════════════════════════════════════
            // [3단계] 단차 상부 폭 — 종단 항목이지만 평면 TB1에 의존하므로 마지막에 계산한다.
            // ══════════════════════════════════════════════════════════

            double? tb6 = null;
            if (hasStep)
            {
                // ⚠ 좌우측 -100 / 중앙부 -200 으로 파일마다 다르다. 엑셀 원본 그대로.
                //   → docs/164_토출조_확인필요사항.md A-4 참조
                var delta = input.Position != DischargeOutletPosition.Center ? 100 : 200;
                tb6 = V("TB6", tb1 - delta);
            }

            return new DischargeBasinGeometryDto(
                TB: tb, TC: tc, TE: te, TF: tf,
                TT1: tt1, TT2: tt2, TT3: tt3,
                TH1: th1, TH2: th2, TH3: th3,
                TCB1: tcb1, TCH1: tch1, TGB1: tgb1, TGH1: tgh1,
                TH4: th4, TH5: th5, TB6: tb6,
                TB1: tb1, TB2: tb2, TB3: tb3, TB4: tb4, TT4: tt4,
                TL1: tl1, TL2: tl2, TLT: tlt,
                OT: ot, O1: o1,
                TOB1: tob1, TOL1: tol1, TOL2: tol2,
                Theta: thetaDeg, Theta1: theta1Deg,
                Theta2: theta2Deg, TB5: tb5, TT5: tt5,
                TL3: tl3, TL4: tl4, TL5: tl5, TL6: tl6, TL7: tl7, TL8: tl8);
        }

        /// <summary>
        /// Excel ROUNDUP(x, 0) — 0에서 먼 쪽으로 올림.
        /// Math.Ceiling 은 음수에서 0 쪽으로 붙으므로 그대로 쓰면 안 된다.
        /// </summary>
        private static double RoundUp(double x) => x < 0 ? -Math.Ceiling(-x) : Math.Ceiling(x);

        /// <summary>
        /// Excel ROUNDDOWN(x, 0) — 0에 가까운 쪽으로 버림.
        /// Math.Floor 는 음수에서 0에서 멀어지므로 그대로 쓰면 안 된다.
        /// </summary>
        private static double RoundDown(double x) => x < 0 ? -Math.Floor(-x) : Math.Floor(x);

        private static double Deg2Rad(double deg) => deg * Math.PI / 180.0;

        private static double Rad2Deg(double rad) => rad * 180.0 / Math.PI;

        /// <summary>분모가 0이면 0을 돌려준다. 입력 검증은 UI가 담당하므로 예외를 던지지 않는다.</summary>
        private static double SafeAtan(double numerator, double denominator)
            => Math.Abs(denominator) < 1e-9 ? 0 : Math.Atan(numerator / denominator);

        /// <summary>tan(각도)가 0에 가까우면 0을 돌려준다. 예외를 던지지 않는다.</summary>
        private static double SafeDivByTan(double value, double angleRad)
        {
            var t = Math.Tan(angleRad);
            return Math.Abs(t) < 1e-9 ? 0 : value / t;
        }
    }
}
