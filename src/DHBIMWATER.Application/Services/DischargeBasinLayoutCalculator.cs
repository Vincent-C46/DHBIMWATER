using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Application.Services
{
    /// <summary>
    /// 토출조 권고안(<see cref="DischargeBasinGeometryDto"/>)을 Revit 부재 정의로 바꾼다.
    /// 제원 숫자를 만드는 일은 <see cref="DischargeBasinGeometryCalculator"/>가 하고, 여기서는 평면·표고 배치만 한다.
    ///
    /// <para><b>좌표계</b> — 내공 좌측 하단(펌프측 내벽 · x=0 단부 내면)이 <b>로컬</b> 원점.
    /// +X = 길이 방향(TLT, 펌프 배열), +Y = 폭 방향(펌프측 내벽 → 토출측).
    /// 이 로컬 좌표는 <see cref="CalculateInnerOutline"/> · <see cref="PumpCenterX"/> 에서만 쓰고,
    /// <b>부재를 돌려주는 메서드는 전부 <see cref="DischargeBasinPlacement"/> 로 변환한 프로젝트 좌표</b>다
    /// (펌프장 우측에 이격 배치 + 축 교환 — 변환 근거는 그쪽 주석 참조).</para>
    ///
    /// <para><b>검증</b> — 이 배치는 엑셀이 따로 뽑아 둔 값과 맞물린다.
    /// 좌우측 토출의 (0, TB1) 코너 내각은 <c>90° + θ = 2θ1</c>, (TLT, TB2) 코너 내각은 <c>90° − θ = 2θ2</c>이고,
    /// 코너 마이터 오프셋이 각각 <c>TT4/2 = TT1/(2·tanθ1)</c>, <c>TT5/2 = TT1/(2·tanθ2)</c>가 된다.
    /// 즉 엑셀의 TT4·TT5·TB4·TB5는 여기서 쓰는 마이터 보정량과 같은 값이다(docs/164 §3.4).</para>
    /// </summary>
    public static class DischargeBasinLayoutCalculator
    {
        public const string ZoneName = "토출조";
        public const string BaseLevelName = "토출조_바닥";
        public const string TopLevelName = "토출조_상부슬래브";

        public const string BaseSlabCode = "DB-S1";
        public const string TopSlabCode = "DB-S2";
        public const string WallCode = "DB-W1";
        public const string OpeningCode = "DB-O1";
        public const string WallOpeningCode = "DB-WO1";
        public const string ColumnCode = "DB-C1";
        public const string GirderCode = "DB-G1";

        /// <summary>
        /// 지금 생성할 수 있는 제원인지 확인하고, 막히면 사유를 돌려준다(가능하면 null).
        /// <para>Transaction 을 열기 전에 불러야 한다 — 걸러내지 않으면 퇴화한 다각형이
        /// 그대로 <c>Floor.Create</c> 까지 가서 예외로 터진다.</para>
        /// </summary>
        public static string? GetUnsupportedReason(
            DischargeBasinInputDto input, DischargeBasinGeometryDto g)
        {
            if (input.Step != DischargeStepType.None) return UnsupportedReason;

            // 중앙부 토출은 토출관로가 내공 총길이를 다 먹으면 경사 구간이 사라진다.
            // TL3 ≤ 0 이면 내공 외곽선의 3·4번 점(경사 끝)이 단부 벽 바깥으로 나가 자기교차 다각형이 되고
            // TB2 < TB1 로 형상까지 뒤집힌다.
            if (input.Position == DischargeOutletPosition.Center && (g.TL3 ?? 0) <= 0)
                return CenterConduitTooWideReason(input, g);

            return null;
        }

        /// <summary>
        /// 단차 적용을 생성하지 않는 이유.
        /// 수령자료 4개 파일 중 <b>평면 도해는 단차미적용·단차적용이 바이트 단위로 동일</b>하다(MD5 일치).
        /// 즉 단차 구간(TB6)이 길이 방향으로 어디까지인지가 원본에 없다.
        /// 게다가 예시값 기준 <c>TB6 + TT1 = 4,100 &gt; TB1 = 3,800</c> 이라 폭이 좁은 단부에서는
        /// 단차 벽체가 들어갈 자리 자체가 없다 — 추측으로 지으면 틀린 모델이 나온다.
        /// </summary>
        public const string UnsupportedReason =
            "단차 적용 형상은 아직 생성할 수 없습니다.\r\n\r\n" +
            "수령자료의 평면 도해가 단차 미적용과 동일해 단차 구간(TB6)의 길이 방향 범위가 원본에 없습니다. " +
            "또한 폭이 좁은 단부에서는 TB6 + TT1 이 내공 폭 TB1 을 넘어 단차 벽체가 들어갈 자리가 없습니다.\r\n\r\n" +
            "단차 미적용으로 생성하시거나, 단차 구간의 평면 범위를 확인해 주세요.\r\n" +
            "(docs/164_토출조_확인필요사항.md C-5)";

        /// <summary>
        /// 중앙부 토출인데 토출관로가 내공 총길이를 넘어서는 경우의 안내.
        /// 숫자를 같이 보여 줘야 어느 입력을 고쳐야 하는지 알 수 있다.
        /// </summary>
        private static string CenterConduitTooWideReason(
            DischargeBasinInputDto input, DischargeBasinGeometryDto g)
        {
            var isCulvert = input.ConduitType == OutletConduitType.Culvert;
            var unit = isCulvert ? input.OH : input.OD;  // ⚠ 암거는 OB가 아니라 OH 참조 (확인필요사항 A-2)
            var gap = isCulvert ? g.OT : g.O1;
            var unitName = isCulvert ? "토출암거 높이 OH" : "토출관 직경 OD";
            var gapName = isCulvert ? "암거 사이 벽두께 OT" : "PIPE 사이 간격 O1";
            var tl4 = g.TL4 ?? 0;

            return "중앙부 토출을 이 제원으로는 생성할 수 없습니다.\r\n\r\n" +
                   $"경사 구간 길이 TL3 = {g.TL3 ?? 0:N0} mm 로 0 이하입니다.\r\n" +
                   $"내공 총길이 TLT({g.TLT:N0})가 " +
                   $"토출관로({input.ON}련 × {unitName} {unit:N0}) + " +
                   $"{gapName}({gap:N0} × {Math.Max(input.ON - 1, 0)}) + " +
                   $"양단 절곡부 여유(2 × TL4 {tl4:N0})를 감당하지 못합니다.\r\n\r\n" +
                   "펌프 대수·직경(= 내공 총길이)을 키우거나, 토출관로 련수·규모를 줄이거나, " +
                   "TL3 를 직접 입력해 주세요.";
        }

        /// <summary>내공 평면 외곽선(mm) — <b>로컬 좌표</b>, 반시계 방향.</summary>
        public static IReadOnlyList<Point2D> CalculateInnerOutline(
            DischargeBasinInputDto input, DischargeBasinGeometryDto g)
        {
            if (input.Position == DischargeOutletPosition.LeftRight)
            {
                // 한쪽으로 벌어지는 사다리꼴. x=0 단부가 TB1, x=TLT 단부가 TB2.
                return new[]
                {
                    new Point2D(0, 0),
                    new Point2D(g.TLT, 0),
                    new Point2D(g.TLT, g.TB2),
                    new Point2D(0, g.TB1),
                };
            }

            // 중앙부 토출 — 좌우 대칭 육각형. 양 단부(x=0·TLT)의 TB1 코너에서 곧바로 TL3 구간 경사로 TB2 까지 벌어지고,
            // 가운데 TL4 + 토출관로 + TL4 구간은 TB2 로 평행하다(평면 도해 하단 치수열 TL3|TL4|관로|TL4|TL3).
            // 엑셀 수식도 이 배치를 전제한다 — TT4·TB4 의 θ1 = (90°+θ)/2 는 x=0 코너 내각의 절반이고,
            // TL5·TL6 은 경사→평행 절곡점(x = TL3)의 벽 외면 위치다(docs/164_토출조_확인필요사항.md A-5).
            var tl3 = g.TL3 ?? 0;
            return new[]
            {
                new Point2D(0, 0),
                new Point2D(g.TLT, 0),
                new Point2D(g.TLT, g.TB1),
                new Point2D(g.TLT - tl3, g.TB2),
                new Point2D(tl3, g.TB2),
                new Point2D(0, g.TB1),
            };
        }

        public static IReadOnlyList<LevelDefinition> CalculateLevels(DischargeBasinGeometryDto g) => new[]
        {
            new LevelDefinition { Name = BaseLevelName, Elevation = g.TF * 1000 },
            new LevelDefinition { Name = TopLevelName, Elevation = g.TB * 1000 },
        };

        /// <summary>
        /// 내공 평면 외곽선을 <b>프로젝트 좌표</b>로 옮긴 것. 부재 계산은 전부 이쪽을 쓴다.
        /// 축 교환으로 뒤집히는 진행 방향은 <see cref="DischargeBasinPlacement.MapOutline"/> 이 되돌려 반시계를 유지한다.
        /// </summary>
        public static IReadOnlyList<Point2D> CalculateProjectOutline(
            DischargeBasinInputDto input, DischargeBasinGeometryDto g)
            => DischargeBasinPlacement.For(input, g).MapOutline(CalculateInnerOutline(input, g));

        /// <summary>바닥슬래브 · 상부슬래브. ElevationZ 는 슬래브 <b>상면</b> 표고다(Revit 바닥은 레벨에서 아래로 두께가 붙는다).</summary>
        public static IReadOnlyList<SlabDefinition> CalculateSlabs(
            DischargeBasinInputDto input, DischargeBasinGeometryDto g)
        {
            var inner = CalculateProjectOutline(input, g);

            return new[]
            {
                // 바닥슬래브 — 벽체 외면에서 TB3(= TT2) 만큼 더 내밀어 기초 저판을 만든다.
                new SlabDefinition
                {
                    Points = OffsetOutward(inner, g.TT1 + g.TB3),
                    SubPoints = Array.Empty<Point2D>(),
                    Thickness = g.TT2,
                    ElevationZ = g.TF * 1000,
                    LevelName = BaseLevelName,
                    ElementCode = BaseSlabCode,
                    Zone = ZoneName,
                    Part = "바닥슬래브",
                },
                // 상부슬래브 — 벽체 외면까지. 개구부는 SubPoints 가 1개만 지원하므로 별도 오프닝 패밀리로 뚫는다.
                new SlabDefinition
                {
                    Points = OffsetOutward(inner, g.TT1),
                    SubPoints = Array.Empty<Point2D>(),
                    Thickness = g.TT3,
                    ElevationZ = g.TB * 1000,
                    LevelName = TopLevelName,
                    ElementCode = TopSlabCode,
                    Zone = ZoneName,
                    Part = "상부슬래브",
                },
            };
        }

        /// <summary>
        /// 외벽. 내공 외곽선을 TT1/2 만큼 바깥으로 밀어 중심선을 만들고, 그 다각형의 변마다 벽 1개를 둔다.
        /// <para>Revit 벽은 양 끝 조인을 끊어 두므로(RevitWallCommandRepo) 코너를 직접 메워야 한다.
        /// 밸브실과 같은 방식으로 <b>변을 번갈아</b> 한쪽은 코너까지 늘리고 이웃은 그만큼 줄여
        /// 겹침도 틈도 없이 맞물리게 한다(직각 코너에서는 정확히, 경사 코너에서는 오차가 t² 수준).</para>
        /// </summary>
        public static IReadOnlyList<LinearWallDefinition> CalculateWalls(
            DischargeBasinInputDto input, DischargeBasinGeometryDto g)
        {
            var inner = CalculateProjectOutline(input, g);
            var centerline = OffsetOutward(inner, g.TT1 / 2);
            var height = (g.TB - g.TF) * 1000 - g.TT3;
            var n = centerline.Count;

            // 코너마다 필요한 마이터 보정량. 내각 α에서 (t/2)/tan(α/2).
            var miter = new double[n];
            for (var i = 0; i < n; i++)
            {
                var half = InteriorAngle(centerline, i) / 2;
                miter[i] = Math.Abs(Math.Tan(half)) < 1e-9 ? 0 : g.TT1 / 2 / Math.Tan(half);
            }

            var walls = new List<LinearWallDefinition>(n);
            for (var i = 0; i < n; i++)
            {
                var a = centerline[i];
                var b = centerline[(i + 1) % n];
                var dir = Direction(a, b);

                // 짝수 변은 양 끝을 코너까지 늘리고, 홀수 변은 그만큼 줄인다.
                // 변의 수가 짝수라야 코너마다 늘림 1 · 줄임 1 이 된다(현재 좌우측 4변 · 중앙부 6변).
                var sign = i % 2 == 0 ? 1 : -1;
                var start = new Point2D(a.X - dir.X * miter[i] * sign, a.Y - dir.Y * miter[i] * sign);
                var endIndex = (i + 1) % n;
                var end = new Point2D(b.X + dir.X * miter[endIndex] * sign, b.Y + dir.Y * miter[endIndex] * sign);

                walls.Add(new LinearWallDefinition
                {
                    StartPoint = new Point3D(start.X, start.Y, 0),
                    EndPoint = new Point3D(end.X, end.Y, 0),
                    Thickness = g.TT1,
                    LevelName = BaseLevelName,
                    Height = height,
                    BaseOffset = 0,
                    IsExterior = true,
                    ElementCode = WallCode,
                    Zone = ZoneName,
                    Part = "외벽",
                });
            }
            return walls;
        }

        /// <summary>
        /// 상부 개구부. 펌프 대수만큼 뚫되, 진입 동선용 1개소(첫 번째)만 TOL2 로 길게 한다.
        /// <para><b>위치</b> — 2번째부터는 펌프 중심에 맞춘다. 첫 번째(TOL2)는 개구부 평면 도해대로
        /// <b>TB1 단부 내벽면(로컬 x=0)에 한 변을 붙인다</b> — 진입 동선이 단부 벽을 따라 내려가는 자리다
        /// (2026-11-02 도해 대조로 정정. 종전에는 1번 펌프 중심 기준 가운데 정렬이었다).</para>
        /// <para><b>치수는 축 기준으로 넣는다</b> — 개구부 패밀리는 회전 없이 배치되고
        /// (<c>RevitOpeningCommandRepo</c>), <c>Width</c>→W = <b>프로젝트 X</b> · <c>Length</c>→L = <b>프로젝트 Y</b> 다
        /// (2026-09-15 Revit 실검증으로 확인. 펌프장도 같은 규약으로 쓴다 —
        /// <c>PumpingStationGeometryCalculator</c> 의 스크린 오프닝 <c>Width = B2</c>(X) · <c>Length = B8</c>(Y)).
        /// 도해상 <b>TOL(길이)은 펌프 배열 방향</b>이고 배치 변환이 그 방향을 프로젝트 Y 로 보내므로
        /// <c>Length = TOL</c> · <c>Width = TOB1</c> 이 된다.</para>
        /// </summary>
        public static IReadOnlyList<RectangularSlabOpeningDefinition> CalculateSlabOpenings(
            DischargeBasinInputDto input, DischargeBasinGeometryDto g)
        {
            var placement = DischargeBasinPlacement.For(input, g);
            var openings = new List<RectangularSlabOpeningDefinition>(input.N);
            for (var i = 0; i < input.N; i++)
            {
                var length = i == 0 ? g.TOL2 : g.TOL1;
                // 진입 동선용(첫 번째)은 단부 내벽면에 붙이고, 나머지는 펌프 중심.
                var centerX = i == 0 ? g.TOL2 / 2 : PumpCenterX(input, g, i);
                openings.Add(new RectangularSlabOpeningDefinition
                {
                    Width = g.TOB1,      // X 방향 (펌프측 → 토출측)
                    Length = length,     // Y 방향 (펌프 배열 방향) — 첫 1개소만 진입 동선용 TOL2
                    Position = placement.Map(new Point2D(centerX, g.TOB1 / 2)),
                    Name = $"토출조 상부 개구부 {i + 1}",
                    LevelName = TopLevelName,
                    HostElementCode = TopSlabCode,
                    ElementCode = OpeningCode,
                    Zone = ZoneName,
                    Part = "상부 개구부",
                });
            }
            return openings;
        }

        /// <summary>i번째 펌프 중심의 <b>로컬</b> X 좌표(mm). TLT = 2·TL1 + N·D + (N-1)·TL2 의 배열과 같은 순서다.
        /// 프로젝트 좌표에서는 <see cref="DischargeBasinPlacement"/> 를 거쳐 Y 가 된다.</summary>
        public static double PumpCenterX(DischargeBasinInputDto input, DischargeBasinGeometryDto g, int index)
            => g.TL1 + input.D / 2 + index * (input.D + g.TL2);

        /// <summary>
        /// 펌프측(서쪽) 벽의 토출관 관통구. 펌프 대수만큼, 각 펌프 중심에 뚫는다.
        ///
        /// <para><b>펌프장 W2 개구부와 같은 사양</b>(사용자 지시 2026-09-15) —
        /// <c>PumpingStationGeometryCalculator.CalculateCircularWallOpenings</c> 의 밸브실 외벽(W2) 개구부가
        /// <c>Diameter = D</c> · 펌프 중심마다 1개 · <c>밸브실레벨 + H6</c> 표고다.
        /// 그 표고를 풀면 <c>펌프장HWL×1000 + H3 − H7 − D</c> 이고 토출조의 <c>TE</c>(토출관 BOP)가 같은 값이다 —
        /// <b>두 개구부가 같은 절대 표고·같은 Y 에 놓여 관이 일직선으로 이어진다</b>(테스트로 고정).</para>
        ///
        /// <para>표고는 펌프장과 같은 방식으로 <b>레벨 + <see cref="CircularWallOpeningDefinition.OffsetZ"/></b> 로 준다.
        /// <c>OffsetZ = (TE − TF) × 1000</c> — 기본값은 TH3 와 같지만, 사용자가 TF 를 덮어써도 관 높이(TE)를 따라간다.</para>
        /// </summary>
        public static IReadOnlyList<CircularWallOpeningDefinition> CalculateWallOpenings(
            DischargeBasinInputDto input, DischargeBasinGeometryDto g)
        {
            var placement = DischargeBasinPlacement.For(input, g);
            var openings = new List<CircularWallOpeningDefinition>(input.N);
            for (var i = 0; i < input.N; i++)
            {
                // 로컬 y = −TT1/2 가 펌프측 벽의 중심선이다. Z 는 0 으로 두고 표고는 OffsetZ 가 정한다(펌프장과 같은 방식).
                openings.Add(new CircularWallOpeningDefinition
                {
                    Diameter = input.D,
                    Position = placement.Map(new Point3D(PumpCenterX(input, g, i), -g.TT1 / 2, 0)),
                    OffsetZ = (g.TE - g.TF) * 1000,
                    LevelName = BaseLevelName,
                    Name = $"토출조 토출관 관통구 {i + 1}",
                    HostElementCode = WallCode,
                    ElementCode = WallOpeningCode,
                    Zone = ZoneName,
                    Part = "토출관 관통구",
                });
            }
            return openings;
        }

        #region 기둥 · 거더 (확장 지점)

        /// <summary>
        /// 기둥. <see cref="DischargeBasinInputDto.Frame"/> 이 null 이면 빈 목록이다 — 지금은 항상 그렇다.
        ///
        /// <para>배치 규칙 — 평면 도해(좌우측·중앙부 공통)에 기둥이 <b>펌프와 펌프 사이(TL2 구간 가운데)</b>에
        /// <b>펌프 대수 − 1 개</b> 그려져 있다(2026-11-02 도해 대조로 정정. 종전 규칙은 "펌프 중심, 펌프 대수만큼").
        /// 열의 Y 는 도해상 TB1 선 부근이라 Frame 으로 받는다(docs/164_토출조_확인필요사항.md C-4).</para>
        ///
        /// <para>단면 TCB1 × TCH1 (= TT1 × TT1) 은 고른 패밀리 유형이 들고 있다 —
        /// <see cref="Interfaces.IElementTypeCommandRepo"/> 에 기둥 유형 생성 API 가 없어 만들어 줄 수 없다.</para>
        /// </summary>
        public static IReadOnlyList<ColumnDefinition> CalculateColumns(
            DischargeBasinInputDto input, DischargeBasinGeometryDto g)
        {
            if (input.Frame is not { } frame) return Array.Empty<ColumnDefinition>();

            var placement = DischargeBasinPlacement.For(input, g);
            var count = Math.Max(input.N - 1, 0);
            var columns = new List<ColumnDefinition>(count);
            for (var i = 0; i < count; i++)
            {
                // i번과 i+1번 펌프 중심의 가운데 = TL2 구간 가운데.
                var x = (PumpCenterX(input, g, i) + PumpCenterX(input, g, i + 1)) / 2;
                columns.Add(new ColumnDefinition
                {
                    Position = placement.Map(
                        new Point3D(x, frame.ColumnLineOffsetY, g.TF * 1000)),
                    TypeName = frame.ColumnTypeName,
                    BaseLevelName = BaseLevelName,
                    TopLevelName = TopLevelName,
                    BaseOffset = 0,
                    // 상단은 상부슬래브 레벨에 구속하되, 슬래브 두께와 거더 돌출높이만큼 내려 거더 밑에 붙인다.
                    TopOffset = -(g.TT3 + (frame.CreateGirder ? g.TGH1 : 0)),
                    ElementCode = ColumnCode,
                    Zone = ZoneName,
                    Part = "기둥",
                });
            }
            return columns;
        }

        /// <summary>
        /// 거더 1개 — 기둥 열을 따라 내공 전 길이를 가로지른다. Frame 이 null 이거나 거더를 끄면 빈 목록이다.
        ///
        /// <para>⚠ 실제로 켤 때 확인할 것 — <c>RevitBeamCommandRepo</c> 는 Z맞춤을
        /// <c>HAUNCH</c>·<c>VR-B</c> ElementCode 에만 적용하고 나머지는 Revit 기본값에 맡긴다.
        /// 여기서 <see cref="BeamDefinition.ZJustification"/> 을 상단(0)으로 둬 거더 상면이
        /// 상부슬래브 하면에 붙게 의도했으므로, 거더를 켜는 시점에 Repo 의 분기에 <c>DB-G1</c> 을 넣거나
        /// 조건을 넓혀야 한다.</para>
        /// </summary>
        public static IReadOnlyList<BeamDefinition> CalculateGirders(
            DischargeBasinInputDto input, DischargeBasinGeometryDto g)
        {
            if (input.Frame is not { CreateGirder: true } frame) return Array.Empty<BeamDefinition>();

            // 상부슬래브 하면. 거더는 여기서 TGH1 만큼 아래로 돌출한다.
            var soffit = g.TB * 1000 - g.TT3;
            var placement = DischargeBasinPlacement.For(input, g);

            return new[]
            {
                new BeamDefinition
                {
                    StartPoint = placement.Map(new Point3D(0, frame.ColumnLineOffsetY, soffit)),
                    EndPoint = placement.Map(new Point3D(g.TLT, frame.ColumnLineOffsetY, soffit)),
                    Width = g.TGB1,
                    Height = g.TGH1,
                    LevelName = TopLevelName,
                    ZJustification = 0,   // 상단
                    Category = "보",
                    ElementCode = GirderCode,
                    Zone = ZoneName,
                    Part = "거더",
                },
            };
        }

        #endregion

        #region 평면 보조 계산

        /// <summary>반시계 다각형을 바깥으로 d 만큼 민다(마이터 조인). 볼록·오목 코너 모두 처리한다.</summary>
        public static IReadOnlyList<Point2D> OffsetOutward(IReadOnlyList<Point2D> points, double d)
        {
            var n = points.Count;
            var result = new List<Point2D>(n);
            for (var i = 0; i < n; i++)
            {
                var prev = points[(i - 1 + n) % n];
                var cur = points[i];
                var next = points[(i + 1) % n];

                var (p1, d1) = OffsetEdge(prev, cur, d);
                var (p2, d2) = OffsetEdge(cur, next, d);
                result.Add(Intersect(p1, d1, p2, d2) ?? p2);
            }
            return result;
        }

        /// <summary>변을 바깥으로 d 만큼 민 직선. 반시계 다각형은 내부가 진행 방향 왼쪽이라 바깥 법선은 오른쪽이다.</summary>
        private static (Point2D Point, Point2D Dir) OffsetEdge(Point2D a, Point2D b, double d)
        {
            var dir = Direction(a, b);
            var normal = new Point2D(dir.Y, -dir.X);
            return (new Point2D(a.X + normal.X * d, a.Y + normal.Y * d), dir);
        }

        private static Point2D? Intersect(Point2D p1, Point2D d1, Point2D p2, Point2D d2)
        {
            var cross = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(cross) < 1e-9) return null;   // 평행 — 직선 변이 이어지는 경우
            var t = ((p2.X - p1.X) * d2.Y - (p2.Y - p1.Y) * d2.X) / cross;
            return new Point2D(p1.X + d1.X * t, p1.Y + d1.Y * t);
        }

        private static Point2D Direction(Point2D a, Point2D b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            return len < 1e-9 ? new Point2D(0, 0) : new Point2D(dx / len, dy / len);
        }

        /// <summary>
        /// 반시계 다각형 i번 꼭짓점의 내각(라디안).
        /// acos 만으로는 [0, π] 밖을 못 내므로 오목(reflex) 꼭짓점이 볼록으로 잡힌다 — 중앙부 토출 평면에 실제로 있다.
        /// 진행 방향의 <b>부호 있는</b> 회전각으로 구해 π 를 넘는 내각도 그대로 돌려준다.
        /// </summary>
        public static double InteriorAngle(IReadOnlyList<Point2D> points, int i)
        {
            var n = points.Count;
            var ePrev = Direction(points[(i - 1 + n) % n], points[i]);
            var eNext = Direction(points[i], points[(i + 1) % n]);
            var cross = ePrev.X * eNext.Y - ePrev.Y * eNext.X;
            var dot = ePrev.X * eNext.X + ePrev.Y * eNext.Y;
            var turn = Math.Atan2(cross, dot);   // 좌회전 +, 우회전 −
            return Math.PI - turn;
        }

        #endregion
    }
}
