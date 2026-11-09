using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Services;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DHBIMWATER.UI.ViewModels.Modeling
{
    /// <summary>
    /// 토출조 제원 입력 화면의 ViewModel.
    /// <see cref="PumpingStationViewModel"/> 안에 얹혀 같은 창에서 모드 토글로 전환된다.
    ///
    /// 펌프장 연동 6개 값(D·N·펌프장HWL·H3·H7·B8+T5)은 읽기전용이며
    /// <see cref="UpdatePumpReference"/> 로 펌프장 쪽에서 밀어 넣는다.
    /// 배치 기준값(펌프장 우측 끝 X · 1번 펌프 중심 Y)은 밀어 넣지 않고
    /// <see cref="StationPlacementProvider"/> 로 필요할 때 당겨 온다.
    ///
    /// 권고안은 편집 가능하고, 덮어쓴 값은 하위 항목으로 전파된다(엑셀과 동일).
    /// 기획: docs/164_토출조_제원입력_기획.md · 열린 항목: docs/164_토출조_확인필요사항.md
    /// </summary>
    public class DischargeBasinViewModel : ViewModelBase
    {
        #region Constants
        public const string PositionLeft = "좌측 토출";
        public const string PositionRight = "우측 토출";
        public const string PositionCenter = "중앙부 토출";
        public const string StepNone = "단차 미적용";
        public const string StepApplied = "단차 적용";
        public const string ConduitCulvert = "암거";
        public const string ConduitPipe = "PIPE";

        private static readonly HashSet<string> PrimaryProfileKeys = new() { "TB", "TF", "TT1", "TT2", "TT3" };
        private static readonly HashSet<string> PrimaryPlanKeys = new() { "TB1", "TB2", "TLT" };

        // θ로 자동 결정되는 값. 계산만 쓰고 화면에는 안 보임.
        private static readonly HashSet<string> HiddenPlanKeys = new()
        {
            "Theta1", "Theta2", "TB4", "TB5", "TT4", "TT5", "TL5", "TL6", "TL7", "TL8",
        };

        private const string ImageRoot = "pack://application:,,,/DHBIMWATER.UI;component/Resources/PumpStationImages/";
        #endregion

        #region Fields
        private readonly IDialogService _dialogService;
        private readonly ICreateDischargeBasinUseCase? _createUseCase;

        /// <summary>사용자가 덮어쓴 권고안. 계산기에 그대로 넘겨 하위 항목까지 전파시킨다.</summary>
        private readonly Dictionary<string, double> _overrides = new();

        private readonly Dictionary<string, DischargeBasinValueRow> _rowsByKey = new();

        /// <summary>초기화 중에는 재계산을 미룬다.</summary>
        private bool _suspendRecalculate;

        // 유형
        private string _selectedPosition = PositionRight;
        private string? _focusedParameter;
        private bool _isDiagramExpanded = true;
        private string _selectedStep = StepNone;
        private string _selectedConduit = ConduitCulvert;

        // 토출조 직접 입력
        private double _ta = 9.4;
        private double _separation = 3;
        private int _on = 2;
        private double _ob = 2000;
        private double _oh = 2000;
        private double _od = 2000;

        // 펌프장 연동 (읽기전용)
        private double _refD;
        private int _refN;
        private double _refPumpHwl;
        private double _refH3;
        private double _refH7;
        private double _refB8PlusT5;

        /// <summary>제원이 퇴화해 생성할 수 없을 때의 한 줄 경고. 비어 있으면 정상이다.</summary>
        private string _geometryWarning = string.Empty;

        // 힌트
        private string _hintTitle = string.Empty;
        private string _hintFormula = string.Empty;
        private string _hintDescription = string.Empty;
        #endregion

        #region Constructor
        public DischargeBasinViewModel(IDialogService dialogService, ICreateDischargeBasinUseCase createUseCase)
        {
            _dialogService = dialogService;
            _createUseCase = createUseCase;

            RevertProfileCommand = new RelayCommand(_ => RevertRows(ProfileRows));
            RevertPlanCommand = new RelayCommand(_ => RevertRows(PlanRows.Concat(OpeningRows)));
            CreateDischargeBasinCommand = new RelayCommand(CreateDischargeBasin, _ => CanCreate);

            _suspendRecalculate = true;
            BuildRows();
            SplitRows();
            _suspendRecalculate = false;

            Recalculate();
        }
        #endregion

        #region Rows
        /// <summary>종단제원 권고안.</summary>
        public ObservableCollection<DischargeBasinValueRow> ProfileRows { get; } = new();

        /// <summary>평면제원 권고안.</summary>
        public ObservableCollection<DischargeBasinValueRow> PlanRows { get; } = new();

        /// <summary>상부 개구부 권고안.</summary>
        public ObservableCollection<DischargeBasinValueRow> OpeningRows { get; } = new();

        /// <summary>종단 탭 밖 표시 행. ProfileRows 행 객체 공유.</summary>
        public ObservableCollection<DischargeBasinValueRow> PrimaryProfileRows { get; } = new();

        /// <summary>종단 탭 상세 설정 행. ProfileRows 행 객체 공유.</summary>
        public ObservableCollection<DischargeBasinValueRow> DetailProfileRows { get; } = new();

        /// <summary>평면 탭 밖 표시 행. PlanRows 행 객체 공유.</summary>
        public ObservableCollection<DischargeBasinValueRow> PrimaryPlanRows { get; } = new();

        /// <summary>평면 탭 상세 설정 행. PlanRows 행 객체 공유.</summary>
        public ObservableCollection<DischargeBasinValueRow> DetailPlanRows { get; } = new();

        private void BuildRows()
        {
            // ── 종단제원 ─────────────────────────────────────────────
            AddRow(ProfileRows, "TB", "TB", "토출조 상단고 (EL)", "m", 3,
                "TA + (TH1 + TT3) / 1000", "");
            AddRow(ProfileRows, "TC", "TC", "지반고 (EL)", "m", 3,
                "펌프장HWL + (H3 - 200) / 1000", "(펌프장 상단고 - 200mm)의 m 표기");
            AddRow(ProfileRows, "TE", "TE", "토출관 BOP (EL)", "m", 3,
                "펌프장HWL + (H3 - H7 - D) / 1000", "(펌프장 상단고 - H7 - D)의 m 표기");
            AddRow(ProfileRows, "TF", "TF", "토출조 바닥고 (EL)", "m", 3,
                "TE - TH3 / 1000", "토출조 바닥고");
            AddRow(ProfileRows, "TT1", "TT1", "벽체 두께", "mm", 0,
                "MAX( ROUNDUP(TC-TF,0)×100 + ROUNDDOWN((TB-TC)/5,0)×100 , 400 )",
                "토압 높이의 10%의 정치수 반영(ROUNDUP) 및 돌출높이 5m 마다 100mm 추가. 또한 최소 400mm 적용. 구조적 최적설계.");
            AddRow(ProfileRows, "TT2", "TT2", "바닥슬래브 두께", "mm", 0,
                "TT1 + 100", "벽체 두께 + 100mm. 구조적 최적설계.");
            AddRow(ProfileRows, "TT3", "TT3", "상부슬래브 두께", "mm", 0,
                "400 (고정)", "400mm 고정. 구조적 최적설계.");
            AddRow(ProfileRows, "TH1", "TH1", "여유고", "mm", 0,
                "1000 + ROUNDUP((TH2+D+TH3)/100,0)×100 - (TH2+D+TH3)",
                "여유고 1m 적용. + 전체 높이를 정치수화 하기 위한 치수 추가.");
            AddRow(ProfileRows, "TH2", "TH2", "수면 ~ 토출관 상단", "mm", 0,
                "(TA - TE)×1000 - D", "");
            AddRow(ProfileRows, "TH3", "TH3", "토출관 하부 두께", "mm", 0,
                "D≤1350 → 400 / 1350<D≤1650 → 500 / D>1650 → 600",
                "기전설계업무편람(2001), 농업생산기반정비사업계획설계기준-배수편(2005), 첨단기술 스마트핸드북(기계, 2020)의 최대값 적용");
            AddRow(ProfileRows, "TH4", "TH4", "단차 하부 높이", "mm", 0,
                "(TC-TF)×1000 - TH5 - TT3", "", "step");
            AddRow(ProfileRows, "TH5", "TH5", "포장 여유", "mm", 0,
                "200 (고정)", "콘크리트 포장 등을 고려하여 200mm 확보", "step");
            AddRow(ProfileRows, "TB6", "TB6", "단차 상부 폭", "mm", 0,
                "좌우측: TB1 - 100 / 중앙부: TB1 - 200",
                "하단과 상부 경사 여부에 의해 단차 발생하여 TB1보다 작게 적용. ⚠ 좌우측(-100)과 중앙부(-200)의 값이 다릅니다 — 확인 필요 (docs/164_토출조_확인필요사항.md A-4)", "step");
            AddRow(ProfileRows, "TCB1", "TCB1", "기둥 가로", "mm", 0, "= TT1", "기둥 가로");
            AddRow(ProfileRows, "TCH1", "TCH1", "기둥 세로", "mm", 0, "= TT1", "기둥 세로");
            AddRow(ProfileRows, "TGB1", "TGB1", "거더 폭", "mm", 0, "= TT1", "기둥폭과 동일폭");
            AddRow(ProfileRows, "TGH1", "TGH1", "거더 돌출높이", "mm", 0, "300 (고정)",
                "돌출높이 300mm 고정. 구조적 최적설계. 지시선 표기 시에는 상부슬래브 두께를 포함하여 높이 표기");

            // ── 평면제원 ─────────────────────────────────────────────
            AddRow(PlanRows, "TB1", "TB1", "내공 폭 (펌프측)", "mm", 0,
                "ROUNDUP(2.5×D/100,0)×100",
                "「KDS 67 30 25 양배수장 구조 설계, P43, 4.3.1.4 배출수조의 수리설계」에 따라 2.5D 이상. 정치수(roundup) 적용");
            AddRow(PlanRows, "TB2", "TB2", "내공 폭 (토출측)", "mm", 0,
                "좌우측: ROUNDUP((TB1+TLT×tan12°)/100,0)×100\r\n중앙부: ROUNDUP((TB1+TL3×tan20°)/100,0)×100",
                "「KDS 67 30 25」에 따라 좌우측 θ=12°, 중앙부 θ=20° 적용. 정치수(roundup) 적용");
            AddRow(PlanRows, "TB3", "TB3", "바닥슬래브 폭", "mm", 0,
                "= TT2", "바닥슬래브의 두께와 동일 폭 적용");
            AddRow(PlanRows, "TL1", "TL1", "단부 펌프중심 ~ 내벽", "mm", 0,
                "ROUNDUP(1.0×D/100,0)×100",
                "「KDS 67 30 25」에 따라 토출관 중심에서 1.5D 이상. ⚠ 산식근거는 1.5D인데 실제 수식은 1.0D입니다 — 확인 필요 (docs/164_토출조_확인필요사항.md A-1)");
            AddRow(PlanRows, "TL2", "TL2", "펌프 순간격", "mm", 0,
                "(펌프장 B8+T5) - D", "펌프장의 펌프 순간격");
            AddRow(PlanRows, "TLT", "TLT", "내공 총길이", "mm", 0,
                "2×TL1 + N×D + (N-1)×TL2", "내공 총길이");
            AddRow(PlanRows, "TL3", "TL3", "경사 구간 길이", "mm", 1,
                "암거: (TLT - ON×OH - (ON-1)×OT - 2×TL4)/2\r\nPIPE: (TLT - ON×OD - (ON-1)×O1 - 2×TL4)/2",
                "⚠ 암거 분기가 OB(폭)가 아닌 OH(높이)를 참조합니다 — 확인 필요 (docs/164_토출조_확인필요사항.md A-2)", "CT");
            AddRow(PlanRows, "TL4", "TL4", "절곡부 여유폭", "mm", 0,
                "1000 (고정)", "벽체 절곡부의 응력집중으로 인한 구조적 안정성 확보를 위해 1m 여유폭 적용", "CT");
            AddRow(PlanRows, "OT", "OT", "암거 사이 벽두께", "mm", 0,
                "형식=암거 → 300, 아니면 0", "암거일 경우 적용. 토출관로 규모는 대부분 크지 않으므로 300으로 고정");
            AddRow(PlanRows, "O1", "O1", "PIPE 사이 간격", "mm", 0,
                "형식=PIPE AND ON≥2 → 1000, 아니면 0", "여러 본의 PIPE를 설치할 경우 PIPE 사이 작업공간 1m 확보");
            AddRow(PlanRows, "Theta", "θ", "", "°", 4,
                "좌우측: atan((TB2-TB1)/TLT)\r\n중앙부: atan((TB2-TB1)/TL3)",
                "TB2 정치수 적용에 의한 각도 보정");
            AddRow(PlanRows, "Theta1", "θ1", "", "°", 4, "(90° + θ) / 2", "");
            AddRow(PlanRows, "Theta2", "θ2", "", "°", 4, "90° - θ1",
                "⚠ 엑셀 계산식 표기는 90°-θ 이지만 실제 수식은 90-θ1 입니다 (docs/164_토출조_확인필요사항.md A-3)", "LR");
            AddRow(PlanRows, "TB4", "TB4", "바닥 경사 투영 (θ1)", "mm", 1, "TB3 / tan(θ1)", "");
            AddRow(PlanRows, "TB5", "TB5", "바닥 경사 투영 (θ2)", "mm", 1, "TB3 / tan(θ2)", "", "LR");
            AddRow(PlanRows, "TT4", "TT4", "벽체 경사 투영 (θ1)", "mm", 1, "TT1 / tan(θ1)", "");
            AddRow(PlanRows, "TT5", "TT5", "벽체 경사 투영 (θ2)", "mm", 1, "TT1 / tan(θ2)", "", "LR");
            AddRow(PlanRows, "TL6", "TL6", "", "mm", 1, "TL4 + TT1×tan(θ/2)", "", "CT");
            AddRow(PlanRows, "TL8", "TL8", "", "mm", 1, "TL4 + (TT1+TB3)×tan(θ/2)", "", "CT");
            AddRow(PlanRows, "TL5", "TL5", "", "mm", 1, "TT1 + TL3 + TL4 - TL6", "", "CT");
            AddRow(PlanRows, "TL7", "TL7", "", "mm", 1, "TT1 + TB3 + TL3 + TL4 - TL8", "", "CT");

            // ── 상부 개구부 ──────────────────────────────────────────
            AddRow(OpeningRows, "TOB1", "TOB1", "상부 개구부 폭", "mm", 0,
                "ROUNDUP(1.5×D/100,0)×100",
                "토출 밸브 유지관리용 상부 개구부 폭. 제작사별 상이. 1.5D의 정치수(roundup) 적용");
            AddRow(OpeningRows, "TOL1", "TOL1", "상부 개구부 길이", "mm", 0,
                "ROUNDUP((D+500)/100,0)×100",
                "토출 밸브 유지관리용 상부 개구부 길이. D + 500의 정치수(roundup) 적용");
            AddRow(OpeningRows, "TOL2", "TOL2", "진입 개구부 길이", "mm", 0,
                "TOL1 + 1000",
                "토출 밸브 유지관리 및 진입 동선용 상부 개구부 길이. TOL1 + 1m 적용. 펌프 N대 중 1개소에만 적용한다.");
        }

        /// <summary>전체 행을 주요/상세로 나눔. 원본 순서 유지, 되돌리기는 원본 컬렉션 사용(docs/319).</summary>
        private void SplitRows()
        {
            foreach (var row in ProfileRows.OrderBy(row => row.Code, StringComparer.Ordinal))
                (PrimaryProfileKeys.Contains(row.Key) ? PrimaryProfileRows : DetailProfileRows).Add(row);
            foreach (var row in PlanRows.Where(row => !HiddenPlanKeys.Contains(row.Key)).OrderBy(row => row.Code, StringComparer.Ordinal))
                (PrimaryPlanKeys.Contains(row.Key) ? PrimaryPlanRows : DetailPlanRows).Add(row);
        }

        private void AddRow(
            ObservableCollection<DischargeBasinValueRow> target,
            string key, string code, string name, string unit, int decimals,
            string formula, string basis, string? visibleWhen = null)
        {
            var row = new DischargeBasinValueRow(key, code, name, unit, decimals, formula, basis, visibleWhen);
            row.UserEdited += OnRowEdited;
            _rowsByKey[key] = row;
            target.Add(row);
        }

        private void OnRowEdited(DischargeBasinValueRow row)
        {
            if (row.Value.HasValue) _overrides[row.Key] = row.Value.Value;
            else _overrides.Remove(row.Key);

            SetHintFrom(row);
            Recalculate();
        }

        private void RevertRows(IEnumerable<DischargeBasinValueRow> rows)
        {
            foreach (var row in rows)
            {
                _overrides.Remove(row.Key);
                row.ClearEdit();
            }
            Recalculate();
        }
        #endregion

        #region 유형 선택
        public IReadOnlyList<string> Positions { get; } = new[] { PositionLeft, PositionRight, PositionCenter };
        public IReadOnlyList<string> Steps { get; } = new[] { StepNone, StepApplied };
        public IReadOnlyList<string> Conduits { get; } = new[] { ConduitCulvert, ConduitPipe };

        public string SelectedPosition
        {
            get => _selectedPosition;
            set { if (SetProperty(ref _selectedPosition, value)) { OnPropertyChanged(nameof(PlanImagePath)); OnPropertyChanged(nameof(PlanHighlightGeometry)); Recalculate(); } }
        }

        public string SelectedStep
        {
            get => _selectedStep;
            set { if (SetProperty(ref _selectedStep, value)) { OnPropertyChanged(nameof(ProfileImagePath)); OnPropertyChanged(nameof(ProfileHighlightGeometry)); Recalculate(); } }
        }

        public string SelectedConduit
        {
            get => _selectedConduit;
            set
            {
                if (!SetProperty(ref _selectedConduit, value)) return;
                OnPropertyChanged(nameof(IsCulvert));
                OnPropertyChanged(nameof(IsPipe));
                OnPropertyChanged(nameof(PlanImagePath));
                OnPropertyChanged(nameof(PlanHighlightGeometry));
                Recalculate();
            }
        }

        public bool IsCulvert => _selectedConduit == ConduitCulvert;
        public bool IsPipe => _selectedConduit == ConduitPipe;
        #endregion

        #region 토출조 직접 입력
        public double TA { get => _ta; set { if (SetProperty(ref _ta, value)) Recalculate(); } }

        /// <summary>펌프장과 이격거리 (m). ※ 어느 수식에도 사용되지 않는다 (확인필요사항 B-1).</summary>
        public double Separation { get => _separation; set => SetProperty(ref _separation, value); }

        public int ON { get => _on; set { if (SetProperty(ref _on, value)) Recalculate(); } }
        public double OB { get => _ob; set { if (SetProperty(ref _ob, value)) Recalculate(); } }
        public double OH { get => _oh; set { if (SetProperty(ref _oh, value)) Recalculate(); } }
        public double OD { get => _od; set { if (SetProperty(ref _od, value)) Recalculate(); } }
        #endregion

        #region 펌프장 연동 (읽기전용)
        public double RefD => _refD;
        public int RefN => _refN;
        public double RefPumpHwl => _refPumpHwl;
        public double RefH3 => _refH3;
        public double RefH7 => _refH7;
        public double RefB8PlusT5 => _refB8PlusT5;

        /// <summary>펌프장 화면의 제원이 바뀔 때마다 호출된다. 토출조 쪽에서는 읽기전용이다.</summary>
        public void UpdatePumpReference(double d, int n, double pumpHwl, double h3, double h7, double b8PlusT5)
        {
            _refD = d;
            _refN = n;
            _refPumpHwl = pumpHwl;
            _refH3 = h3;
            _refH7 = h7;
            _refB8PlusT5 = b8PlusT5;

            OnPropertyChanged(nameof(RefD));
            OnPropertyChanged(nameof(RefN));
            OnPropertyChanged(nameof(RefPumpHwl));
            OnPropertyChanged(nameof(RefH3));
            OnPropertyChanged(nameof(RefH7));
            OnPropertyChanged(nameof(RefB8PlusT5));
            OnPropertyChanged(nameof(CanCreate));
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasValidationMessage));

            Recalculate();
        }

        /// <summary>펌프장 제원이 채워져야 계산·생성이 가능하다.</summary>
        public bool CanCreate => _refD > 0 && _refN > 0 && _refB8PlusT5 > 0;

        /// <summary>
        /// 화면 경고. 펌프장 제원 미비가 먼저고, 그다음이 제원 자체가 퇴화한 경우다.
        /// 버튼을 누르기 전에 보이지 않으면 사용자는 생성이 막힌 이유를 모른 채 값을 이리저리 바꾸게 된다.
        /// </summary>
        public string ValidationMessage
            => !CanCreate ? "펌프장 제원을 먼저 입력하세요. (펌프 직경 · 대수 · B8+T5)" : _geometryWarning;

        public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);
        #endregion

        #region 이미지
        private string PlanSheet => SelectedPosition == PositionCenter
            ? "평면-중앙부"
            : $"평면-{(SelectedPosition == PositionLeft ? "좌측" : "우측")}-{(IsCulvert ? "암거" : "PIPE")}";

        private string ProfileSheet => SelectedStep == StepApplied ? "종단-단차적용" : "종단-단차미적용";

        private const string OpeningSheet = "개구부-평면";

        public string PlanImagePath => ImageRoot + $"토출조_{PlanSheet}.png";

        /// <summary>포커스 입력칸의 매개변수 키. 도해 강조에 사용.</summary>
        public string? FocusedParameter
        {
            get => _focusedParameter;
            private set
            {
                if (!SetProperty(ref _focusedParameter, value)) return;
                OnPropertyChanged(nameof(PlanHighlightGeometry));
                OnPropertyChanged(nameof(ProfileHighlightGeometry));
                OnPropertyChanged(nameof(OpeningHighlightGeometry));
            }
        }

        /// <summary>도해별 강조 경로. 도해에 글자가 없는 매개변수는 null.</summary>
        public string? PlanHighlightGeometry => FindHighlight(PlanSheet);
        public string? ProfileHighlightGeometry => FindHighlight(ProfileSheet);
        public string? OpeningHighlightGeometry => FindHighlight(OpeningSheet);

        private string? FindHighlight(string sheet)
            => FocusedParameter is { } parameter ? DischargeBasinDiagramLabels.Find(sheet, parameter) : null;

        /// <summary>입력칸 포커스 해제 시 View가 호출.</summary>
        public void ClearFocusedParameter() => FocusedParameter = null;

        /// <summary>도해 영역 펼침 상태. 세 탭이 공유.</summary>
        public bool IsDiagramExpanded { get => _isDiagramExpanded; set => SetProperty(ref _isDiagramExpanded, value); }

        public string ProfileImagePath => ImageRoot + $"토출조_{ProfileSheet}.png";

        public string OpeningImagePath => ImageRoot + $"토출조_{OpeningSheet}.png";
        #endregion

        #region 힌트
        public string HintTitle { get => _hintTitle; private set => SetProperty(ref _hintTitle, value); }
        public string HintFormula { get => _hintFormula; private set => SetProperty(ref _hintFormula, value); }
        public string HintDescription { get => _hintDescription; private set => SetProperty(ref _hintDescription, value); }

        /// <summary>View 의 GotFocus 핸들러가 Tag(기호)를 넘겨 호출한다.</summary>
        public void SetHint(string key)
        {
            FocusedParameter = key;
            if (_rowsByKey.TryGetValue(key, out var row))
            {
                SetHintFrom(row);
                return;
            }

            if (_inputHints.TryGetValue(key, out var hint))
            {
                HintTitle = hint.title;
                HintFormula = string.Empty;
                HintDescription = hint.desc;
            }
        }

        private void SetHintFrom(DischargeBasinValueRow row)
        {
            HintTitle = string.IsNullOrEmpty(row.Name) ? row.Code : $"{row.Code} · {row.Name}";
            HintFormula = row.Formula;
            HintDescription = row.Basis;
        }

        private static readonly Dictionary<string, (string title, string desc)> _inputHints = new()
        {
            ["TA"] = ("TA — H.W.L (EL, m)", "토출조 계획 고수위. TB(상단고)·TH2 산정의 기준값."),
            ["Separation"] = ("펌프장과 이격거리 (m)",
                "펌프장 최우측 벽 외면에서 토출조 벽체 외면까지의 거리. 제원 수식에는 쓰이지 않고 "
                + "토출조를 펌프장 오른쪽 어디에 놓을지(배치 원점)를 정한다. "
                + "※ 바닥슬래브는 벽 외면에서 TB3 만큼 더 나가므로 지중 실간격은 이 값보다 TB3 만큼 좁다 "
                + "(docs/164_토출조_확인필요사항.md B-1)."),
            ["ON"] = ("ON — 토출관로 련수", "토출관로의 련(개소) 수. PIPE 2련 이상이면 O1(1m 작업공간)이 적용된다."),
            ["OB"] = ("OB — 토출암거 폭 (mm)", "토출관로 형식이 암거일 때 사용."),
            ["OH"] = ("OH — 토출암거 높이 (mm)",
                "토출관로 형식이 암거일 때 사용. ⚠ 중앙부 TL3 수식이 OB 대신 이 값을 참조합니다 (docs/164_토출조_확인필요사항.md A-2)."),
            ["OD"] = ("OD — 토출관 직경 (mm)", "토출관로 형식이 PIPE일 때 사용."),
            ["RefD"] = ("D — 펌프 직경 (mm)", "펌프장 설계조건에서 연동되는 읽기전용 값. TB1·TL1·TOB1·TH3 등 대부분 제원의 기준값."),
            ["RefN"] = ("N — 펌프 대수", "펌프장 설계조건에서 연동되는 읽기전용 값. TLT(내공 총길이) 산정에 사용."),
            ["RefPumpHwl"] = ("펌프장 HWL (EL, m)", "펌프장 설계조건에서 연동되는 읽기전용 값. TC·TE 산정 기준."),
            ["RefH3"] = ("H3 (mm)", "펌프장 종단제원에서 연동되는 읽기전용 값. 펌프장 상단고 산정에 사용."),
            ["RefH7"] = ("H7 — 최소 토피 (mm)", "펌프장 종단제원에서 연동되는 읽기전용 값. TE(토출관 BOP) 산정에 사용."),
            ["RefB8PlusT5"] = ("B8 + T5 (mm)",
                "펌프장 평면제원의 B8 + T5 = 펌프 중심 간격. TL2(펌프 순간격) = (B8+T5) − D 에 사용된다."),
        };

        private static readonly IReadOnlyDictionary<string, string> _inputHintTexts =
            _inputHints.ToDictionary(pair => pair.Key, pair => pair.Value.desc);

        /// <summary>설계조건 탭 ? 툴팁 문구. _inputHints 단일 출처(docs/319).</summary>
        public IReadOnlyDictionary<string, string> InputHintTexts => _inputHintTexts;
        #endregion

        #region Commands
        public ICommand RevertProfileCommand { get; }
        public ICommand RevertPlanCommand { get; }
        public ICommand CreateDischargeBasinCommand { get; }

        /// <summary>모델리스 창의 생성 실행 위임. 명령이 ExternalEvent로 연결. null이면 직접 실행(docs/320 § B).</summary>
        public Action<Action>? RunInRevitContext { get; set; }

        private void CreateDischargeBasin(object? _)
        {
            if (!CanCreate)
            {
                _dialogService.Warn("토출조", ValidationMessage);
                return;
            }

            var input = BuildInput();

            // 사용자가 덮어쓴 권고안이 그대로 형상에 반영되도록 _overrides 를 함께 넘긴다.
            var geometry = DischargeBasinGeometryCalculator.Calculate(input, _overrides);

            // 막힌 유형(단차 적용)과 퇴화한 제원(중앙부 TL3 ≤ 0)은 Transaction 을 열기 전에 걸러 사유를 알린다.
            if (_createUseCase?.GetBlockingReason(input, geometry) is { } reason)
            {
                _dialogService.Warn("토출조", reason);
                return;
            }

            // 모델리스 창: 생성만 API 컨텍스트에서 실행. 입력 검사·계산은 순수 코드(docs/320 § B)
            if (RunInRevitContext is { } run) run(() => _createUseCase?.Execute(input, geometry));
            else _createUseCase?.Execute(input, geometry);
        }
        #endregion

        #region 계산
        /// <summary>
        /// 펌프장 화면에서 배치 기준값을 가져온다 — (펌프장 최우측 벽 외면 X, 1번 펌프 중심 Y), mm.
        /// <para>펌프장 종단제원(B1~B7·T3·T4)이 바뀔 때마다 밀어 넣는 대신 <b>필요할 때 당겨 온다</b>.
        /// 밀어 넣는 방식이면 setter 9개에 전파 호출을 더 달아야 하고 하나만 빠져도 배치가 옛 값으로 남는다.</para>
        /// <para>연결되지 않으면 (0, 0) — 프로젝트 원점 기준이다(단위 테스트가 이 상태를 쓴다).</para>
        /// </summary>
        public Func<(double RightEdgeX, double FirstPumpCenterY)>? StationPlacementProvider { get; set; }

        private DischargeBasinInputDto BuildInput()
        {
            var (rightEdgeX, firstPumpCenterY) = StationPlacementProvider?.Invoke() ?? (0, 0);
            return BuildInput(rightEdgeX, firstPumpCenterY);
        }

        private DischargeBasinInputDto BuildInput(double stationRightEdgeX, double firstPumpCenterY) => new(
            Position: SelectedPosition switch
            {
                PositionCenter => DischargeOutletPosition.Center,
                PositionLeft => DischargeOutletPosition.Left,
                _ => DischargeOutletPosition.Right,
            },
            Step: SelectedStep == StepApplied
                ? DischargeStepType.Applied
                : DischargeStepType.None,
            ConduitType: IsPipe ? OutletConduitType.Pipe : OutletConduitType.Culvert,
            TA: TA,
            Separation: Separation,
            ON: ON,
            OB: OB,
            OH: OH,
            OD: OD,
            D: _refD,
            N: _refN,
            PumpHwl: _refPumpHwl,
            H3: _refH3,
            H7: _refH7,
            B8PlusT5: _refB8PlusT5,
            StationRightEdgeX: stationRightEdgeX,
            FirstPumpCenterY: firstPumpCenterY);

        private void Recalculate()
        {
            if (_suspendRecalculate) return;

            ApplyVisibility();

            if (!CanCreate)
            {
                // 펌프장 제원이 없으면 권고안을 비운다.
                foreach (var row in _rowsByKey.Values) row.SetComputed(null);
                SetGeometryWarning(string.Empty);
                return;
            }

            var g = DischargeBasinGeometryCalculator.Calculate(BuildInput(), _overrides);
            ApplyGeometry(g);

            // 중앙부 토출은 토출관로가 내공 총길이를 다 먹으면 경사 구간이 사라져 평면이 뒤집힌다.
            // 상세 사유는 생성 시 UseCase 가 띄우고, 화면에는 한 줄만 상시 보여 준다.
            SetGeometryWarning(
                SelectedPosition == PositionCenter && (g.TL3 ?? 0) <= 0
                    ? $"⚠ 경사 구간 TL3 = {g.TL3 ?? 0:N0} mm (0 이하) — 토출관로 규모가 내공 총길이를 넘어 생성할 수 없습니다. "
                      + "펌프 대수·직경을 키우거나 토출관로 련수·규모를 줄여 주세요."
                    : string.Empty);
        }

        /// <summary>
        /// 계산 결과를 각 행에 반영한다.
        /// ★ 반드시 <see cref="DischargeBasinValueRow.SetComputed"/> 를 쓴다.
        ///   Value setter 로 넣으면 전 항목이 "사용자 수정"으로 기록돼 권고안이 더는 갱신되지 않는다.
        /// </summary>
        private void ApplyGeometry(DischargeBasinGeometryDto g)
        {
            Set("TB", g.TB); Set("TC", g.TC); Set("TE", g.TE); Set("TF", g.TF);
            Set("TT1", g.TT1); Set("TT2", g.TT2); Set("TT3", g.TT3);
            Set("TH1", g.TH1); Set("TH2", g.TH2); Set("TH3", g.TH3);
            Set("TCB1", g.TCB1); Set("TCH1", g.TCH1); Set("TGB1", g.TGB1); Set("TGH1", g.TGH1);
            Set("TH4", g.TH4); Set("TH5", g.TH5); Set("TB6", g.TB6);

            Set("TB1", g.TB1); Set("TB2", g.TB2); Set("TB3", g.TB3);
            Set("TB4", g.TB4); Set("TT4", g.TT4);
            Set("TL1", g.TL1); Set("TL2", g.TL2); Set("TLT", g.TLT);
            Set("OT", g.OT); Set("O1", g.O1);
            Set("Theta", g.Theta); Set("Theta1", g.Theta1);
            Set("Theta2", g.Theta2); Set("TB5", g.TB5); Set("TT5", g.TT5);
            Set("TL3", g.TL3); Set("TL4", g.TL4);
            Set("TL5", g.TL5); Set("TL6", g.TL6); Set("TL7", g.TL7); Set("TL8", g.TL8);

            Set("TOB1", g.TOB1); Set("TOL1", g.TOL1); Set("TOL2", g.TOL2);

            void Set(string key, double? value)
            {
                if (_rowsByKey.TryGetValue(key, out var row)) row.SetComputed(value);
            }
        }

        private void SetGeometryWarning(string message)
        {
            if (_geometryWarning == message) return;
            _geometryWarning = message;
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasValidationMessage));
        }

        /// <summary>유형(토출 위치 · 단차)에 따라 해당 없는 행을 숨긴다.</summary>
        private void ApplyVisibility()
        {
            var isCenter = SelectedPosition == PositionCenter;
            var hasStep = SelectedStep == StepApplied;

            foreach (var row in _rowsByKey.Values)
            {
                row.IsVisible = row.VisibleWhen switch
                {
                    null => true,
                    "step" => hasStep,
                    "LR" => !isCenter,
                    "CT" => isCenter,
                    _ => true,
                };
            }
        }
        #endregion
    }
}
