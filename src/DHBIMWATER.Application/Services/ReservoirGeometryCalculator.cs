using DHBIMWATER.Application.DTOs.Revit.Reservoir;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Application.Services
{
    /// <summary>
    /// 배수지(저수조) 지오메트리 계산기.
    /// 모든 좌표 단위는 mm.
    /// DTO 단위: LWL(m)·He(m) → *1000 변환, 나머지 치수는 이미 mm.
    /// 원점(0,0): 수조부 내부 좌하단.
    /// </summary>
    public static class ReservoirGeometryCalculator
    {
        #region Level Names
        public const string TankFoundLevelName = "수조부 바닥슬래브";
        public const string TankUpperLevelName = "수조부 상부슬래브";
        public const string ValveFoundLevelName = "밸브실 바닥슬래브";
        public const string ValveMidLevelName = "밸브실 중간슬래브";
        public const string LWLLevelName = "LWL";
        public const string HWLLevelName = "HWL";
        #endregion

        // 덧침콘크리트 두께(mm). 레거시 "일반 100mm" 유형과 같음(docs/289 § E-3)
        private const double ToppingThickness = 100;

        // ─────────────────────────────────────────────────────────── Public API

        public static IReadOnlyList<LevelDefinition> CalculateLevels(ReservoirCreationRequestDto dto)
        {
            var (tfE, tuE, vfE, vmE) = LevelElevations(dto);
            double lwlE = dto.DesignConditionDto.LWL * 1000;
            double hwlE = lwlE + dto.TankDto.He * 1000;
            return new List<LevelDefinition>
            {
                new LevelDefinition { Name = TankFoundLevelName,  Elevation = tfE  },
                new LevelDefinition { Name = TankUpperLevelName,  Elevation = tuE  },
                new LevelDefinition { Name = ValveFoundLevelName, Elevation = vfE  },
                new LevelDefinition { Name = ValveMidLevelName,   Elevation = vmE  },
                new LevelDefinition { Name = LWLLevelName,        Elevation = lwlE },
                new LevelDefinition { Name = HWLLevelName,        Elevation = hwlE },
            };
        }
        public static IReadOnlyList<SlabDefinition> CalculateSlabs(ReservoirCreationRequestDto dto)
        {
            var d = dto.DesignConditionDto;
            var t = dto.TankDto;
            var v = dto.ValveDto;
            var th = dto.ThicknessDto;

            int n = d.N;
            double w = t.W;       // mm
            double l = t.L;       // mm
            double ltt = t.Ltt;     // mm
            double wh = t.Wh;      // mm
            double lh = t.Lh;      // mm
            double lv = v.Lv;      // mm
            double wv = v.Wv;      // mm
            double lvt = v.Lvt;     // mm
            double we = v.We;      // mm
            double trOff = v.TrOff;   // mm

            double stuThk = th.StuThk;
            double stbThk = th.StbThk;
            double wteThk = th.WteThk;
            double wtiThk = th.WtiThk;
            double whThk = th.WhThk;
            double svuThk = th.SvuThk;
            double svmThk = th.SvmThk;
            double svbThk = th.SvbThk;
            double wveThk = th.WveThk;
            double wviThk = th.WviThk;
            double lcThk = th.LcThk;

            var (tfE, tuE, vfE, vmE) = LevelElevations(dto);

            double innerWallCX = w + wtiThk / 2;

            var slabs = new List<SlabDefinition>();

            // ──────── B1/L1: 수조부 기초 (N셀 loop)
            double s_W = w + wtiThk / 2 + wteThk + ltt;
            double s_L = l + 2 * wteThk + 2 * ltt;
            double bp1 = lv / 2 + wveThk - wh - wtiThk / 2;
            double bp2 = wh + wtiThk / 2;
            double bl1 = ltt;
            double bl2 = lh + wteThk;

            for (int i = 0; i < n; i++)
            {
                bool isFirst = (i == 0);
                bool isLast = (i == n - 1);
                double xOff = i * (w + wtiThk);

                List<Point2D> b1Pts;
                if (isFirst && n >= 2) b1Pts = LeftCellFoundShape(-(wteThk + ltt), -(wteThk + ltt), s_W, s_L, bp1, bp2, bl1, bl2);
                else if (isLast && n >= 2) b1Pts = RightCellFoundShape(xOff - wtiThk / 2, -(wteThk + ltt), s_W, s_L, bp1, bp2, bl1, bl2);
                else b1Pts = Rect(xOff - wtiThk / 2, -(wteThk + ltt), w + wtiThk, s_L);

                slabs.Add(new SlabDefinition
                {
                    Points = b1Pts,
                    SubPoints = Array.Empty<Point2D>(),
                    Thickness = stbThk,
                    ElevationZ = tfE,
                    LevelName = TankFoundLevelName,
                    ElementCode = "B1",
                    Zone = "수조부",
                    Part = "기초콘크리트",
                });

                List<Point2D> l1Pts;
                if (isFirst && n >= 2) l1Pts = LeftCellFoundShape(-(wteThk + ltt + lcThk), -(wteThk + ltt + lcThk), s_W + lcThk, s_L + 2 * lcThk, bp1 - whThk, bp2 + whThk, bl1 + lcThk + whThk, lh + wteThk);
                else if (isLast && n >= 2) l1Pts = RightCellFoundShape(xOff - wtiThk / 2, -(wteThk + ltt + lcThk), s_W + lcThk, s_L + 2 * lcThk, bp1 - whThk, bp2 + whThk, bl1 + lcThk + whThk, lh + wteThk);
                else l1Pts = Rect(xOff - wtiThk / 2, -(wteThk + ltt + lcThk), w + wtiThk, s_L + 2 * lcThk);

                slabs.Add(new SlabDefinition
                {
                    Points = l1Pts,
                    SubPoints = Array.Empty<Point2D>(),
                    Thickness = lcThk,
                    ElevationZ = tfE - stbThk,
                    LevelName = TankFoundLevelName,
                    ElementCode = "L1",
                    Zone = "수조부",
                    Part = "버림콘크리트",
                });

                double s1StartX = (n == 1 || isFirst) ? -wteThk : xOff - wtiThk / 2;
                double s1W = (n == 1) ? w + 2 * wteThk
                           : isFirst ? w + wteThk + wtiThk / 2
                           : isLast ? w + wteThk + wtiThk / 2
                           : w + wtiThk;

                slabs.Add(new SlabDefinition
                {
                    Points = Rect(s1StartX, -wteThk, s1W, l + 2 * wteThk),
                    SubPoints = Array.Empty<Point2D>(),
                    Thickness = stuThk,
                    ElevationZ = tuE,
                    LevelName = TankUpperLevelName,
                    ElementCode = "S1",
                    Zone = "수조부",
                    Part = "상부슬래브",
                });
            }

            // ──────── B2/L2: 배관실 호퍼 기초
            double b2StartX = w - wh - whThk;
            double b2StartY = whThk - wteThk;
            double b2W = 2 * wh + wtiThk + 2 * whThk;
            double b2L = lh + wteThk;

            slabs.Add(new SlabDefinition
            {
                Points = Rect(b2StartX, b2StartY, b2W, b2L),
                SubPoints = Array.Empty<Point2D>(),
                Thickness = svbThk,
                ElevationZ = vfE,
                LevelName = ValveFoundLevelName,
                ElementCode = "B2",
                Zone = "배관실",
                Part = "기초콘크리트",
            });
            slabs.Add(new SlabDefinition
            {
                Points = Rect(b2StartX, b2StartY, b2W, b2L),
                SubPoints = Array.Empty<Point2D>(),
                Thickness = lcThk,
                ElevationZ = vfE - svbThk,
                LevelName = ValveFoundLevelName,
                ElementCode = "L2",
                Zone = "배관실",
                Part = "버림콘크리트",
            });

            // ──────── B4/L4: 배관실 외측 기초
            double vCenterX = innerWallCX;
            double b4StartX = vCenterX - lv / 2 - wveThk - lvt;
            double b4StartY = -(2 * wveThk + wv + lvt);
            double b4W = 2 * lvt + lv + 2 * wveThk;
            double b4L = 2 * wveThk + wv + lvt;

            slabs.Add(new SlabDefinition
            {
                Points = Rect(b4StartX, b4StartY, b4W, b4L),
                SubPoints = Array.Empty<Point2D>(),
                Thickness = svbThk,
                ElevationZ = vfE,
                LevelName = ValveFoundLevelName,
                ElementCode = "B4",
                Zone = "배관실",
                Part = "기초콘크리트",
            });
            slabs.Add(new SlabDefinition
            {
                Points = Rect(b4StartX - lcThk, b4StartY - lcThk, b4W + 2 * lcThk, b4L + lcThk),
                SubPoints = Array.Empty<Point2D>(),
                Thickness = lcThk,
                ElevationZ = vfE - svbThk,
                LevelName = ValveFoundLevelName,
                ElementCode = "L4",
                Zone = "배관실",
                Part = "버림콘크리트",
            });

            // ──────── MS1: 배관실 중간슬래브
            double vmStartX = vCenterX - lv / 2;
            double vmStartY = -(wteThk + wv);

            slabs.Add(new SlabDefinition
            {
                Points = Rect(vmStartX, vmStartY, lv, wv),
                SubPoints = Array.Empty<Point2D>(),
                Thickness = svmThk,
                ElevationZ = vmE,
                LevelName = ValveMidLevelName,
                ElementCode = "MS1",
                Zone = "배관실",
                Part = "중간슬래브",
            });

            // ──────── S2: 배관실 상부슬래브
            slabs.Add(new SlabDefinition
            {
                Points = Rect(vmStartX - wveThk, vmStartY - wveThk, lv + 2 * wveThk, wv + wveThk),
                SubPoints = Array.Empty<Point2D>(),
                Thickness = svuThk,
                ElevationZ = tuE,
                LevelName = TankUpperLevelName,
                ElementCode = "S2",
                Zone = "배관실",
                Part = "상부슬래브",
            });

            // ──────── TC1/TC2/TC3: 덧침콘크리트
            double tcStartX = vmStartX + trOff;
            double tcStartY = vmStartY + trOff;

            slabs.Add(new SlabDefinition
            {
                Points = Rect(tcStartX, tcStartY, lv - 2 * trOff, wv - 2 * trOff),
                SubPoints = Array.Empty<Point2D>(),
                Thickness = ToppingThickness,
                ElevationZ = vfE + ToppingThickness,
                LevelName = ValveFoundLevelName,
                ElementCode = "TC1",
                Zone = "배관실",
                Part = "바닥슬래브_덧침콘크리트",
            });
            slabs.Add(new SlabDefinition
            {
                Points = Rect(tcStartX, tcStartY, lv - we - 2 * trOff - wviThk, wv - 2 * trOff),
                SubPoints = Array.Empty<Point2D>(),
                Thickness = ToppingThickness,
                ElevationZ = vmE + ToppingThickness,
                LevelName = ValveMidLevelName,
                ElementCode = "TC2",
                Zone = "배관실",
                Part = "중간슬래브_덧침콘크리트",
            });
            double tc3StartX = vmStartX + (lv - we + trOff);
            slabs.Add(new SlabDefinition
            {
                Points = Rect(tc3StartX, tcStartY, we - 2 * trOff, wv - 2 * trOff),
                SubPoints = Array.Empty<Point2D>(),
                Thickness = ToppingThickness,
                ElevationZ = vmE + ToppingThickness,
                LevelName = ValveMidLevelName,
                ElementCode = "TC3",
                Zone = "배관실",
                Part = "전기실_덧침콘크리트",
            });

            return slabs;
        }
        public static IReadOnlyList<LinearWallDefinition> CalculateLinearWalls(ReservoirCreationRequestDto dto)
        {
            var d = dto.DesignConditionDto;
            var t = dto.TankDto;
            var v = dto.ValveDto;
            var th = dto.ThicknessDto;

            int n = d.N;
            double w = t.W;   // mm
            double l = t.L;   // mm
            double wh = t.Wh;  // mm
            double lh = t.Lh;  // mm
            double lv = v.Lv;  // mm
            double wv = v.Wv;  // mm
            double we = v.We;  // mm

            double wteThk = th.WteThk;
            double wtiThk = th.WtiThk;
            double whThk = th.WhThk;
            double wveThk = th.WveThk;
            double wviThk = th.WviThk;

            var (tfE, tuE, vfE, vmE) = LevelElevations(dto);

            double totalIW = n * w + (n - 1) * wtiThk;
            double innerWCX = w + wtiThk / 2;
            double vCenterX = innerWCX;

            // 상부 구속·높이는 레거시 RH:2156-2310과 같게 맞춤(docs/289 § B).
            // 높이 = 상부레벨 + 상부간격띄우기 - 기준레벨. 상부 구속은 RevitWallCommandRepo가 설정
            double tankWallH = tuE - tfE - th.StuThk;
            double hopperH = tfE - vfE - th.StbThk;
            double tankToValveFoundH = tuE - vfE - th.StuThk;   // W5_2·W6_2: Hopper 바닥부터
            double valveFullH = tuE - vfE - th.SvuThk;          // 밸브실 외벽: 밸브실 상부슬래브 하단까지
            double elecWallH = tuE - vmE - th.SvuThk;
            var walls = new List<LinearWallDefinition>();

            // 수조부 상부슬래브 하단까지 구속
            LinearWallDefinition TankTop(LinearWallDefinition wall) => wall with { TopLevelName = TankUpperLevelName, TopOffset = -th.StuThk };
            // 밸브실 상부슬래브 하단까지 구속
            LinearWallDefinition ValveTop(LinearWallDefinition wall) => wall with { TopLevelName = TankUpperLevelName, TopOffset = -th.SvuThk };

            walls.Add(TankTop(Wall("W1", wteThk, TankFoundLevelName, tankWallH,
                new Point3D(-wteThk / 2, 0, 0), new Point3D(-wteThk / 2, l, 0), "수조부", "외벽_좌측")));

            double w2X = totalIW + wteThk / 2;
            walls.Add(TankTop(Wall("W2", wteThk, TankFoundLevelName, tankWallH,
                new Point3D(w2X, 0, 0), new Point3D(w2X, l, 0), "수조부", "외벽_우측")));

            walls.Add(TankTop(Wall("W3", wteThk, TankFoundLevelName, tankWallH,
                new Point3D(-wteThk, l + wteThk / 2, 0), new Point3D(totalIW + wteThk, l + wteThk / 2, 0), "수조부", "외벽_후면")));

            double w4_1EndX = vCenterX - lv / 2 - wveThk;
            double w4_2StaX = vCenterX + lv / 2 + wveThk;
            walls.Add(TankTop(Wall("W4", wteThk, TankFoundLevelName, tankWallH,
                new Point3D(-wteThk, -wteThk / 2, 0), new Point3D(w4_1EndX, -wteThk / 2, 0), "수조부", "외벽_전면")));
            walls.Add(TankTop(Wall("W4", wteThk, TankFoundLevelName, tankWallH,
                new Point3D(w4_2StaX, -wteThk / 2, 0), new Point3D(totalIW + wteThk, -wteThk / 2, 0), "수조부", "외벽_전면")));

            double w5_1EndX = w - wh;
            double w5_2EndX = w + wh + wtiThk;
            walls.Add(TankTop(Wall("W5", wteThk, TankFoundLevelName, tankWallH,
                new Point3D(w4_1EndX, -wteThk / 2, 0), new Point3D(w5_1EndX, -wteThk / 2, 0), "배관실", "경계부_벽체")));
            // W5_2: Hopper 위 경계부. 레거시는 수조 외벽 유형·밸브실 바닥 기준(RH:2252-2273)
            walls.Add(TankTop(Wall("W5", wteThk, ValveFoundLevelName, tankToValveFoundH,
                new Point3D(w5_1EndX, -wteThk / 2, 0), new Point3D(w5_2EndX, -wteThk / 2, 0), "배관실", "경계부_벽체")));
            walls.Add(TankTop(Wall("W5", wteThk, TankFoundLevelName, tankWallH,
                new Point3D(w5_2EndX, -wteThk / 2, 0), new Point3D(w4_2StaX, -wteThk / 2, 0), "배관실", "경계부_벽체")));

            for (int i = 0; i < n - 1; i++)
            {
                double wxCenter = (i + 1) * (w + wtiThk) - wtiThk / 2;
                if (i == 0)
                {
                    // W6_1: L→Lh, W6_2: Lh→0 (레거시 방향 RH:822-832). W6_2는 Hopper 바닥부터(RH:2297-2310)
                    walls.Add(TankTop(Wall("W6", wtiThk, TankFoundLevelName, tankWallH,
                        new Point3D(wxCenter, l, 0), new Point3D(wxCenter, lh, 0), "수조부", "내벽")));
                    walls.Add(TankTop(Wall("W6", wtiThk, ValveFoundLevelName, tankToValveFoundH,
                        new Point3D(wxCenter, lh, 0), new Point3D(wxCenter, 0, 0), "수조부", "내벽")));
                }
                else
                {
                    walls.Add(TankTop(Wall("W6", wtiThk, TankFoundLevelName, tankWallH,
                        new Point3D(wxCenter, 0, 0), new Point3D(wxCenter, l, 0), "수조부", "내벽")));
                }
            }

            // Hopper 벽: 수조부 바닥슬래브 하단까지 구속
            LinearWallDefinition HopperTop(LinearWallDefinition wall) => wall with { TopLevelName = TankFoundLevelName, TopOffset = -th.StbThk };
            double b3_1Sy = -wteThk + whThk / 2;
            walls.Add(HopperTop(Wall("B3", whThk, ValveFoundLevelName, hopperH,
                new Point3D(w4_1EndX, b3_1Sy, 0), new Point3D(w5_1EndX, b3_1Sy, 0), "배관실", "Hopper_벽체")));
            double b3_2X = w5_1EndX - whThk / 2;
            double b3_2Sy = -wteThk + whThk;
            walls.Add(HopperTop(Wall("B3", whThk, ValveFoundLevelName, hopperH,
                new Point3D(b3_2X, b3_2Sy, 0), new Point3D(b3_2X, lh, 0), "배관실", "Hopper_벽체")));
            double b3_4X = w5_2EndX + whThk / 2;
            walls.Add(HopperTop(Wall("B3", whThk, ValveFoundLevelName, hopperH,
                new Point3D(b3_4X, b3_2Sy, 0), new Point3D(b3_4X, lh, 0), "배관실", "Hopper_벽체")));
            walls.Add(HopperTop(Wall("B3", whThk, ValveFoundLevelName, hopperH,
                new Point3D(b3_2X - whThk / 2, lh + whThk / 2, 0), new Point3D(b3_4X + whThk / 2, lh + whThk / 2, 0), "배관실", "Hopper_벽체")));
            walls.Add(HopperTop(Wall("B3", whThk, ValveFoundLevelName, hopperH,
                new Point3D(w5_2EndX, b3_1Sy, 0), new Point3D(w4_2StaX, b3_1Sy, 0), "배관실", "Hopper_벽체")));

            double w7X = vCenterX - lv / 2 - wveThk / 2;
            double w79TopY = -wteThk;
            double w79BotY = -(wteThk + wv);
            walls.Add(ValveTop(Wall("W7", wveThk, ValveFoundLevelName, valveFullH,
                new Point3D(w7X, w79TopY, 0), new Point3D(w7X, w79BotY, 0), "배관실", "외벽_좌측")));
            double w8X = vCenterX + lv / 2 + wveThk / 2;
            walls.Add(ValveTop(Wall("W8", wveThk, ValveFoundLevelName, valveFullH,
                new Point3D(w8X, w79TopY, 0), new Point3D(w8X, w79BotY, 0), "배관실", "외벽_우측")));
            walls.Add(ValveTop(Wall("W9", wveThk, ValveFoundLevelName, valveFullH,
                new Point3D(w7X - wveThk / 2, w79BotY - wveThk / 2, 0),
                new Point3D(w8X + wveThk / 2, w79BotY - wveThk / 2, 0), "배관실", "외벽_전면")));

            double w10X = w8X - wveThk / 2 - we - wviThk / 2;
            walls.Add(ValveTop(Wall("W10", wviThk, ValveMidLevelName, elecWallH,
                new Point3D(w10X, w79TopY, 0), new Point3D(w10X, w79BotY, 0), "배관실", "전기실_벽체")));

            return walls;
        }
        public static IReadOnlyList<ColumnDefinition> CalculateColumns(ReservoirCreationRequestDto dto)
        {
            var d = dto.DesignConditionDto;
            var t = dto.TankDto;
            var th = dto.ThicknessDto;

            int n = d.N;
            double w = t.W;   // mm
            double l = t.L;   // mm
            double m1 = t.M1;  // mm
            double m2 = t.M2;  // mm
            double m3 = t.M3;  // mm
            double m4 = t.M4;  // mm
            double wtiThk = th.WtiThk;

            var (tfE, tuE, _, _) = LevelElevations(dto);
            double colH = tuE - tfE - th.StuThk;

            const double maxCTC = 5000;
            double colSpan = w - m3 - m4;
            double rowSpan = l - m1 - m2;
            int colNum = (colSpan % maxCTC == 0) ? (int)(colSpan / maxCTC) + 1 : (int)(colSpan / maxCTC) + 2;
            int rowNum = (rowSpan % maxCTC == 0) ? (int)(rowSpan / maxCTC) + 1 : (int)(rowSpan / maxCTC) + 2;
            double colOff = Math.Round(colSpan / (colNum - 1), 0);
            double rowOff = Math.Round(rowSpan / (rowNum - 1), 0);

            var columns = new List<ColumnDefinition>();
            for (int ci = 0; ci < n; ci++)
            {
                for (int r = 0; r < rowNum; r++)
                    for (int c = 0; c < colNum; c++)
                        columns.Add(new ColumnDefinition
                        {
                            Position = new Point3D(MapX(ci, w, wtiThk, m3 + c * colOff), m2 + r * rowOff, 0),
                            TypeName = th.ColumnTypeName,
                            BaseLevelName = TankFoundLevelName,
                            TopLevelName = TankUpperLevelName,
                            BaseOffset = 0,
                            TopOffset = -th.StuThk,
                            ElementCode = "C1",
                            Zone = "수조부",
                            Part = "기둥",
                        });
            }
            return columns;
        }
        /// <param name="sizes">기둥 b·h, 보 b(mm). 보를 기둥 면에서 끊고 헌치를 보 폭만큼 끊는 데 씀(레거시 RH:1087-1490). null이면 공제 없음.</param>
        public static IReadOnlyList<BeamDefinition> CalculateBeams(ReservoirCreationRequestDto dto, ReservoirMemberSizes? sizes = null)
        {
            var d = dto.DesignConditionDto;
            var t = dto.TankDto;
            var v = dto.ValveDto;
            var th = dto.ThicknessDto;

            int n = d.N;
            double w = t.W;   // mm
            double l = t.L;   // mm
            double m1 = t.M1;  // mm
            double m2 = t.M2;  // mm
            double m3 = t.M3;  // mm
            double m4 = t.M4;  // mm
            double lh = t.Lh;  // mm
            double wh = t.Wh;  // mm
            double wtiThk = th.WtiThk;
            double stuThk = th.StuThk;

            double colB = sizes?.ColumnB ?? 0;   // 기둥 X 폭
            double colH = sizes?.ColumnH ?? 0;   // 기둥 Y 깊이
            double hb = (sizes?.BeamB ?? 0) / 2; // 보 반폭

            var (tfE, tuE, _, vmE) = LevelElevations(dto);
            double beamZ = tuE;

            const double maxCTC = 5000;
            double colSpan = w - m3 - m4;
            double rowSpan = l - m1 - m2;
            int colNum = (colSpan % maxCTC == 0) ? (int)(colSpan / maxCTC) + 1 : (int)(colSpan / maxCTC) + 2;
            int rowNum = (rowSpan % maxCTC == 0) ? (int)(rowSpan / maxCTC) + 1 : (int)(rowSpan / maxCTC) + 2;
            double colOff = Math.Round(colSpan / (colNum - 1), 0);
            double rowOff = Math.Round(rowSpan / (rowNum - 1), 0);

            // 1번째 지 로컬 좌표(x 0..w)로 만들고 지마다 MapX로 옮김. 홀수 지는 미러(레거시 MirrorElements 결과)
            var local = new List<BeamDefinition>();
            string beamType = th.BeamTypeName;
            var pts = ColumnGrid(m3, m2, rowOff, colOff, rowNum, colNum, beamZ);

            // G1: 기둥 면에서 시작·끝 (RH:1091-1170)
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                if ((i + 1) % colNum != 0)
                    local.Add(Beam("G1", beamType, TankUpperLevelName,
                        new Point3D(p.X + colB / 2, p.Y, beamZ),
                        new Point3D(pts[i + 1].X - colB / 2, pts[i + 1].Y, beamZ), "수조부", "보"));

                if (i + colNum < pts.Count)
                    local.Add(Beam("G1", beamType, TankUpperLevelName,
                        new Point3D(p.X, p.Y + colH / 2, beamZ),
                        new Point3D(pts[i + colNum].X, pts[i + colNum].Y - colH / 2, beamZ), "수조부", "보"));

                if (i >= colNum * (rowNum - 1))
                    local.Add(Beam("G1", beamType, TankUpperLevelName,
                        new Point3D(p.X, p.Y + colH / 2, beamZ),
                        new Point3D(p.X, p.Y + m1, beamZ), "수조부", "보"));

                if (i < colNum)
                    local.Add(Beam("G1", beamType, TankUpperLevelName,
                        new Point3D(p.X, p.Y - colH / 2, beamZ),
                        new Point3D(p.X, p.Y - m2, beamZ), "수조부", "보"));

                if (i % colNum == 0)
                    local.Add(Beam("G1", beamType, TankUpperLevelName,
                        new Point3D(p.X - colB / 2, p.Y, beamZ),
                        new Point3D(p.X - m3, p.Y, beamZ), "수조부", "보"));

                if (i % colNum == colNum - 1)
                    local.Add(Beam("G1", beamType, TankUpperLevelName,
                        new Point3D(p.X + colB / 2, p.Y, beamZ),
                        new Point3D(p.X + m4, p.Y, beamZ), "수조부", "보"));
            }

            // H2: 기초 헌치
            var fndPts = new List<Point3D>
            {
                new Point3D(w,      lh, tfE),
                new Point3D(w,      l,  tfE),
                new Point3D(0,      l,  tfE),
                new Point3D(0,      0,  tfE),
                new Point3D(w - wh, 0,  tfE),
            };
            for (int i = 0; i < fndPts.Count - 1; i++)
                local.Add(Haunch("H2", TankFoundLevelName, fndPts[i], fndPts[i + 1], "수조부"));

            // H1: 상부 헌치. 닫힌 루프 + 보 폭 공제(RH:1229-1490). 높이·방향은 기존 신규 방식(슬래브 하단, 상단 맞춤) — Revit 확인
            var upperPts = BuildUpperHaunchPoints(0, w, l, m1, m2, m3, m4, rowOff, colOff, rowNum, colNum, beamZ - stuThk);
            foreach (var (start, end) in TrimUpperHaunchSegments(upperPts, rowNum, colNum, hb))
                local.Add(Haunch("H1", TankUpperLevelName, start, end, "수조부"));

            var beams = new List<BeamDefinition>();
            for (int ci = 0; ci < n; ci++)
                foreach (var b in local)
                    beams.Add(MapBeam(b, ci, w, wtiThk));

            // H3/H4: 밸브실·전기실 헌치. 밸브실은 1개라 미러 대상 아님(레거시도 첫 밸브실만, RH:1493-1550, 2681-2720)
            double vmStartX = w + wtiThk / 2 - v.Lv / 2;
            double vmStartY = -(th.WteThk + v.Wv);
            AddHaunchLoop(beams, "H3", ValveMidLevelName,
                Square(vmStartX, vmStartY, v.Lv, v.Wv, vmE - th.SvmThk));
            AddHaunchLoop(beams, "H4", TankUpperLevelName,
                Square(vmStartX, vmStartY, v.Lv - v.We - th.WviThk, v.Wv, tuE - th.SvuThk));
            AddHaunchLoop(beams, "H4", TankUpperLevelName,
                Square(vmStartX + v.Lv - v.We, vmStartY, v.We, v.Wv, tuE - th.SvuThk));

            return beams;
        }

        public static IReadOnlyList<GenericModelPlacementDefinition> CalculateGenericModels(ReservoirCreationRequestDto dto)
        {
            var t = dto.TankDto;
            var v = dto.ValveDto;
            var th = dto.ThicknessDto;

            double w = t.W;
            double wh = t.Wh;
            double lh = t.Lh;
            double hh = t.Hh;
            double lv = v.Lv;
            double wv = v.Wv;
            double wp = v.Wp;

            double stbThk = th.StbThk;
            double svbThk = th.SvbThk;
            double wteThk = th.WteThk;
            double wtiThk = th.WtiThk;
            double whThk = th.WhThk;
            double wveThk = th.WveThk;
            double lcThk = th.LcThk;

            var (tfE, _, vfE, _) = LevelElevations(dto);

            double innerWallCX = w + wtiThk / 2;
            double vCenterX = innerWallCX;
            double vmStartX = vCenterX - lv / 2;
            double vmStartY = -(wteThk + wv);
            double b2StartY = whThk - wteThk;
            double faultingHeight = hh + svbThk - stbThk;

            return new List<GenericModelPlacementDefinition>
            {
                new GenericModelPlacementDefinition
                {
                    SymbolName = "DH_단차버림콘크리트",
                    // 레거시처럼 Z=0 비구조 배치 후 레벨로부터 높이로 내림(RH:2087, 2115-2116)
                    Origin = new Point3D(innerWallCX, b2StartY, 0),
                    LevelName = TankFoundLevelName,
                    IsNonStructural = true,
                    ElevationFromLevel = -(stbThk + lcThk),
                    Rotation = 0,
                    Category = "슬래브",
                    ElementCode = "L3",
                    Class = "Body",
                    Zone = "배관실",
                    Part = "단차_버림콘크리트",
                    Parameters = new Dictionary<string, object>
                    {
                        { "L1", (lv + 2 * wveThk - (wtiThk + 2 * wh + 2 * whThk)) / 2 },
                        { "L2", lh + whThk },
                        { "L3", wtiThk + 2 * wh + 2 * whThk },
                        { "W1", lcThk + faultingHeight * v.SLv },
                        { "W2", lcThk },
                        { "H", faultingHeight },
                        { "단차구간", lv + 2 * wveThk },
                        { "사면깊이", faultingHeight },
                        { "Hopper길이", lh },
                        { "Hopper벽체두께", whThk },
                        { "Hopper폭", wh },
                        { "버림Con'c두께", lcThk },
                        { "수조부내벽두께", wtiThk },
                        { "수조부외벽두께", wteThk },
                        { "사면비", new Dimensionless(v.SLv) },
                    },
                },
                new GenericModelPlacementDefinition
                {
                    SymbolName = "DH_배수지피트",
                    Origin = new Point3D(vmStartX + wp / 2, vmStartY + wp / 2, 0),
                    LevelName = ValveFoundLevelName,
                    IsNonStructural = true, // 레거시 RH:2446
                    Rotation = 0,
                    Category = "PIT",
                    ElementCode = "P1",
                    Class = "Body",
                    Zone = "배관실",
                    Part = "PIT",
                    Parameters = new Dictionary<string, object>
                    {
                        { "드레인피트_내부_가로폭", v.Wp },
                        { "드레인피트_내부_세로폭", v.Wp },
                        { "드레인피트_내부_깊이", v.Hp },
                        { "드레인피트_벽체두께", v.WpThk },
                        { "드레인피트_바닥두께", v.SpThk },
                        { "드레인피트_버림콘크리트_두께", th.LcThk },
                        { "밸브실_바닥슬라브두께", th.SvbThk },
                    },
                },
            };
        }
        /// <summary>단면뷰 A~D. 레거시 상자(RH:2774-2796)를 절단선·폭·표고·깊이로 옮김. 여유 1m.
        /// 레거시 근사식(B의 W-Lv/2, A의 (W+Wte)·N 등)은 그대로 둠(docs/289 § F).</summary>
        public static IReadOnlyList<SectionCutDefinition> CalculateSectionViews(ReservoirCreationRequestDto dto)
        {
            var d = dto.DesignConditionDto;
            var t = dto.TankDto;
            var v = dto.ValveDto;
            var th = dto.ThicknessDto;
            const double margin = 1000;

            var (tfE, tuE, vfE, _) = LevelElevations(dto);
            double wte = th.WteThk, wve = th.WveThk, wti = th.WtiThk;

            SectionCutDefinition Cut(string name, double cutX, double cutY, Vector3D right, double min, double max,
                double zMin, double depth)
            {
                // 가로 범위 중앙을 절단점으로. right 축 방향 좌표만 중앙으로 옮김
                double center = (min + max) / 2;
                var origin = right.X != 0 ? new Point3D(center, cutY, 0) : new Point3D(cutX, center, 0);
                return new SectionCutDefinition
                {
                    Name = name, CutOrigin = origin, Right = right,
                    HalfWidth = (max - min) / 2, ZMin = zMin, ZMax = tuE + margin, FarClipDepth = depth,
                };
            }

            // A·B: 화면 오른쪽 +X → +Y 바라봄. C·D: 화면 오른쪽 -Y → +X 바라봄
            var plusX = new Vector3D(1, 0, 0);
            var minusY = new Vector3D(0, -1, 0);

            double aMin = -(wte + margin + t.Ltt);
            double aMax = (t.W + wte) * d.N + margin * 2 + t.Ltt * 2;
            double bMin = t.W - v.Lv / 2 - (v.Lvt + wve + margin);
            double bMax = bMin + v.Lv + v.Lvt * 2 + wve * 2 + margin * 2;
            double cMin = -(wte + margin + t.Ltt);
            double cMax = t.L + wte + margin + t.Ltt;
            double dMin = -(wte + margin + v.Lvt + v.Wv + wve);
            double dMax = t.L + wte + margin + v.Lvt;

            return new List<SectionCutDefinition>
            {
                Cut("A", 0, t.Lh / 2, plusX, aMin, aMax, vfE - margin, t.L / 2),
                Cut("B", 0, wte - v.Wv, plusX, bMin, bMax, vfE - margin, v.Wv / 2),
                Cut("C", t.M3 / 2, 0, minusY, cMin, cMax, tfE - margin, t.M3),
                Cut("D", t.W + t.Wh / 2 + wti, 0, minusY, dMin, dMax, vfE - margin, t.M4),
            };
        }

        // ─────────────────────────────────────────────── Private Helpers

        /// <summary>레벨 절대 표고 계산 (mm). LWL·He는 m 단위 DTO이므로 *1000 변환.</summary>
        private static (double tf, double tu, double vf, double vm) LevelElevations(ReservoirCreationRequestDto dto)
        {
            var d = dto.DesignConditionDto;
            var t = dto.TankDto;
            var v = dto.ValveDto;
            var th = dto.ThicknessDto;

            double lwl = d.LWL * 1000;  // m → mm
            double he = t.He * 1000;  // m → mm
            double hm = t.Hm;          // 이미 mm
            double hf = t.Hf;          // 이미 mm
            double hh = t.Hh;          // 이미 mm
            double h1f = v.H1F;         // 이미 mm

            double tf = lwl - hm;
            double tu = tf + hm + he + hf + th.StuThk;
            double vf = tf - hh;
            double vm = vf + h1f + th.SvmThk;
            return (tf, tu, vf, vm);
        }

        private static List<Point2D> LeftCellFoundShape(
            double sx, double sy, double W, double L, double w1, double w2, double l1, double l2)
        {
            if (l1 > 0)
                return new List<Point2D>
                {
                    new Point2D(sx,               sy),
                    new Point2D(sx + W - (w1+w2), sy),
                    new Point2D(sx + W - (w1+w2), sy + l1),
                    new Point2D(sx + W - w2,      sy + l1),
                    new Point2D(sx + W - w2,      sy + l1 + l2),
                    new Point2D(sx + W,            sy + l1 + l2),
                    new Point2D(sx + W,            sy + L),
                    new Point2D(sx,                sy + L),
                };
            return new List<Point2D>
            {
                new Point2D(sx,          sy),
                new Point2D(sx + W - w2, sy),
                new Point2D(sx + W - w2, sy + l2),
                new Point2D(sx + W,      sy + l2),
                new Point2D(sx + W,      sy + L),
                new Point2D(sx,          sy + L),
            };
        }

        private static List<Point2D> RightCellFoundShape(
            double sx, double sy, double W, double L, double w1, double w2, double l1, double l2)
        {
            if (l1 > 0)
                return new List<Point2D>
                {
                    new Point2D(sx + w1 + w2, sy),
                    new Point2D(sx + W,        sy),
                    new Point2D(sx + W,        sy + L),
                    new Point2D(sx,            sy + L),
                    new Point2D(sx,            sy + l1 + l2),
                    new Point2D(sx + w2,       sy + l1 + l2),
                    new Point2D(sx + w2,       sy + l1),
                    new Point2D(sx + w1 + w2,  sy + l1),
                };
            return new List<Point2D>
            {
                new Point2D(sx + w2, sy),
                new Point2D(sx + W,  sy),
                new Point2D(sx + W,  sy + L),
                new Point2D(sx,      sy + L),
                new Point2D(sx,      sy + l2),
                new Point2D(sx + w2, sy + l2),
            };
        }

        private static List<Point2D> Rect(double sx, double sy, double W, double L)
            => new List<Point2D>
            {
                new Point2D(sx,     sy),
                new Point2D(sx + W, sy),
                new Point2D(sx + W, sy + L),
                new Point2D(sx,     sy + L),
            };

        private static List<Point3D> ColumnGrid(
            double startX, double startY, double rowOffset, double colOffset,
            int rowNum, int colNum, double z = 0)
        {
            var pts = new List<Point3D>();
            for (int r = 0; r < rowNum; r++)
                for (int c = 0; c < colNum; c++)
                    pts.Add(new Point3D(startX + c * colOffset, startY + r * rowOffset, z));
            return pts;
        }

        private static LinearWallDefinition Wall(
            string code, double thickness, string levelName, double height,
            Point3D start, Point3D end, string zone, string part)
            => new LinearWallDefinition
            {
                ElementCode = code,
                Thickness = thickness,
                LevelName = levelName,
                Height = height,
                StartPoint = start,
                EndPoint = end,
                Zone = zone,
                Part = part,
            };

        private static BeamDefinition Beam(
            string code, string typeName, string levelName,
            Point3D start, Point3D end, string zone, string part)
            => new BeamDefinition
            {
                ElementCode = code,
                TypeName = typeName,
                LevelName = levelName,
                StartPoint = start,
                EndPoint = end,
                Zone = zone,
                Part = part,
                ZJustification = 0,
            };

        /// <summary>헌치 정의. 커브 z = 슬래브 하단, Z맞춤 상단(0), 회전 없음.
        /// 레거시(원점 + 슬래브 두께 + 180°)가 아니라 기존 신규 방식이 맞다고 사용자 확인(2026-10-13, docs/289 § C-3).</summary>
        private static BeamDefinition Haunch(string code, string levelName, Point3D start, Point3D end, string zone)
            => new BeamDefinition
            {
                ElementCode = code,
                LevelName = levelName,
                StartPoint = start,
                EndPoint = end,
                Zone = zone,
                Part = "HAUNCH",
                ZJustification = 0,
            };

        /// <summary>레거시 CreateSquare 후 Reverse와 같은 점 순서(시계방향).</summary>
        private static List<Point3D> Square(double x, double y, double width, double length, double z)
            => new List<Point3D>
            {
                new Point3D(x,         y + length, z),
                new Point3D(x + width, y + length, z),
                new Point3D(x + width, y,          z),
                new Point3D(x,         y,          z),
            };

        /// <summary>밸브실 헌치 닫힌 루프. pts z는 슬래브 하단.</summary>
        private static void AddHaunchLoop(List<BeamDefinition> beams, string code, string levelName, List<Point3D> pts)
        {
            for (int i = 0; i < pts.Count; i++)
                beams.Add(Haunch(code, levelName, pts[i], pts[(i + 1) % pts.Count], "배관실"));
        }

        /// <summary>지 인덱스별 X 좌표. 짝수 지 = 평행이동, 홀수 지 = 직전 내벽 중심면 기준 미러.
        /// 레거시는 1번째 지를 내벽 평면마다 MirrorElements 함(RH:1669-1674, 2854-2859).</summary>
        private static double MapX(int ci, double w, double wtiThk, double xLocal)
        {
            double xOff = ci * (w + wtiThk);
            return ci % 2 == 0 ? xOff + xLocal : xOff + w - xLocal;
        }

        /// <summary>홀수 지는 시작·끝을 바꿔 진행 방향을 뒤집음. 헌치 단면(삼각형)이 미러 후에도 수조 안쪽을 향하게 하려는 것.</summary>
        private static BeamDefinition MapBeam(BeamDefinition b, int ci, double w, double wtiThk)
        {
            bool mirrored = ci % 2 == 1;
            var start = mirrored ? b.EndPoint : b.StartPoint;
            var end = mirrored ? b.StartPoint : b.EndPoint;
            return new BeamDefinition
            {
                StartPoint = new Point3D(MapX(ci, w, wtiThk, start.X), start.Y, start.Z),
                EndPoint = new Point3D(MapX(ci, w, wtiThk, end.X), end.Y, end.Z),
                TypeName = b.TypeName,
                Width = b.Width,
                Height = b.Height,
                LevelName = b.LevelName,
                ZJustification = b.ZJustification,
                CrossSectionRotationDeg = b.CrossSectionRotationDeg,
                Category = b.Category,
                ElementCode = b.ElementCode,
                Zone = b.Zone,
                Part = b.Part,
            };
        }

        /// <summary>상부 헌치 구간. 마지막 점→첫 점까지 닫고, 보와 만나는 끝은 보 반폭(hb)만큼 끊음.
        /// 분기 경계는 레거시 RH:1278-1490 if/else 그대로. 점 순서는 BuildUpperHaunchPoints(좌변↑ 상변→ 우변↓ 하변←).</summary>
        private static IEnumerable<(Point3D Start, Point3D End)> TrimUpperHaunchSegments(
            List<Point3D> pts, int rowNum, int colNum, double hb)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                var s = pts[i];
                var e = pts[(i + 1) % pts.Count];
                double sx = 0, sy = 0, ex = 0, ey = 0;

                if (i == 0) ey = -hb;                                            // 좌하단 구석→첫 보
                else if (i < rowNum) { sy = hb; ey = -hb; }                      // 좌변 보 사이
                else if (i == rowNum) sy = hb;                                   // 마지막 보→좌상단 구석
                else if (i == rowNum + 1) ex = -hb;                              // 좌상단 구석→첫 보
                else if (i < rowNum + colNum + 1) { sx = hb; ex = -hb; }         // 상변 보 사이
                else if (i == rowNum + colNum + 1) sx = hb;                      // 마지막 보→우상단 구석
                else if (i == rowNum + colNum + 2) ey = hb;                      // 우상단 구석→첫 보
                else if (i < 2 * rowNum + colNum + 2) { sy = -hb; ey = hb; }     // 우변 보 사이
                else if (i == 2 * rowNum + colNum + 2) sy = -hb;                 // 마지막 보→우하단 구석
                else if (i == 2 * rowNum + colNum + 3) ex = hb;                  // 우하단 구석→첫 보
                else if (i < 2 * rowNum + 2 * colNum + 3) { sx = -hb; ex = hb; } // 하변 보 사이
                else sx = -hb;                                                   // 마지막 보→좌하단 구석(닫힘)

                yield return (new Point3D(s.X + sx, s.Y + sy, s.Z), new Point3D(e.X + ex, e.Y + ey, e.Z));
            }
        }

        private static List<Point3D> BuildUpperHaunchPoints(
            double xOff, double w, double l,
            double m1, double m2, double m3, double m4,
            double rowOff, double colOff, int rowNum, int colNum,
            double z)
        {
            var pts = new List<Point3D>();
            pts.Add(new Point3D(xOff, 0, z));
            for (int r = 0; r < rowNum; r++) pts.Add(new Point3D(xOff, m2 + r * rowOff, z));
            pts.Add(new Point3D(xOff, l, z));
            for (int c = 0; c < colNum; c++) pts.Add(new Point3D(xOff + m3 + c * colOff, l, z));
            pts.Add(new Point3D(xOff + w, l, z));
            for (int r = 0; r < rowNum; r++) pts.Add(new Point3D(xOff + w, l - m1 - r * rowOff, z));
            pts.Add(new Point3D(xOff + w, 0, z));
            for (int c = 0; c < colNum; c++) pts.Add(new Point3D(xOff + w - m4 - c * colOff, 0, z));
            return pts;
        }
    }

    /// <summary>배수지 부재 단면 크기(mm). 기둥 b = X 폭, h = Y 깊이, 보 b = 폭.</summary>
    public sealed record ReservoirMemberSizes(double ColumnB, double ColumnH, double BeamB);
}
 

