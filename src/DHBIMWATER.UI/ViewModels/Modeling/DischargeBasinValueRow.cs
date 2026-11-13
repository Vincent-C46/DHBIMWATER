using DHBIMWATER.UI.Base;

namespace DHBIMWATER.UI.ViewModels.Modeling
{
    /// <summary>
    /// 토출조 권고안 한 줄. 화면에서는 `코드 | 항목명 | 입력칸 | 단위` 로 그려진다.
    ///
    /// 값이 들어오는 경로가 두 가지라 구분해 둔다.
    ///  - <see cref="Value"/> setter  : 사용자가 직접 고친 것 → <see cref="IsEdited"/> 를 세우고 재계산을 요청한다.
    ///  - <see cref="SetComputed"/>   : 계산기가 채워 넣은 것 → 수정으로 기록하지 않는다.
    /// 이 구분이 깨지면 계산 결과 전체가 "사용자 수정"으로 잡혀 권고안이 더 이상 갱신되지 않는다.
    /// </summary>
    public class DischargeBasinValueRow : ViewModelBase
    {
        private double? _value;
        private bool _isEdited;
        private bool _isVisible = true;

        public DischargeBasinValueRow(
            string key,
            string code,
            string name,
            string unit,
            int decimals,
            string formula,
            string basis,
            string? visibleWhen = null)
        {
            Key = key;
            Code = code;
            Name = name;
            Unit = unit;
            Decimals = decimals;
            Formula = formula;
            Basis = basis;
            VisibleWhen = visibleWhen;
        }

        /// <summary>계산기 오버라이드 키. 엑셀 기호와 동일(각도만 Theta/Theta1/Theta2).</summary>
        public string Key { get; }

        /// <summary>화면에 찍히는 기호. 예 "TT1".</summary>
        public string Code { get; }

        public string Name { get; }

        public string Unit { get; }

        /// <summary>표시 소수 자릿수.</summary>
        public int Decimals { get; }

        /// <summary>힌트 패널에 띄울 수식.</summary>
        public string Formula { get; }

        /// <summary>힌트 패널에 띄울 산식근거(엑셀 원문).</summary>
        public string Basis { get; }

        /// <summary>
        /// 표시 조건. null = 항상, "step" = 단차 적용 시, "LR" = 좌우측 토출, "CT" = 중앙부 토출.
        /// </summary>
        public string? VisibleWhen { get; }

        public string CodeLabel => string.IsNullOrEmpty(Code) ? string.Empty : Code + ":";

        /// <summary>사용자가 이 칸을 직접 고쳤을 때 발생한다.</summary>
        public event Action<DischargeBasinValueRow>? UserEdited;

        public double? Value
        {
            get => _value;
            set
            {
                if (Nullable.Equals(_value, value)) return;
                _value = value;
                OnPropertyChanged();
                IsEdited = true;
                UserEdited?.Invoke(this);
            }
        }

        /// <summary>사용자가 권고안을 덮어썼는지. 화면에서 색으로 구분한다.</summary>
        public bool IsEdited
        {
            get => _isEdited;
            private set
            {
                if (_isEdited == value) return;
                _isEdited = value;
                OnPropertyChanged();
            }
        }

        /// <summary>유형(토출 위치·단차)에 따른 표시 여부.</summary>
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                OnPropertyChanged();
            }
        }

        /// <summary>계산 결과를 반영한다. 사용자 수정으로 기록하지 않는다.</summary>
        public void SetComputed(double? computed)
        {
            var rounded = computed.HasValue
                ? Math.Round(computed.Value, Decimals, MidpointRounding.AwayFromZero)
                : (double?)null;

            if (Nullable.Equals(_value, rounded)) return;
            _value = rounded;
            OnPropertyChanged(nameof(Value));
        }

        /// <summary>저장본 복원용. 덮어쓴 값으로 표시만 함 — UserEdited 안 냄(재계산·힌트는 호출부가).</summary>
        public void RestoreEdited(double value)
        {
            _value = value;
            OnPropertyChanged(nameof(Value));
            IsEdited = true;
        }

        /// <summary>덮어쓰기 표시만 해제한다. 값 복원은 재계산이 담당한다.</summary>
        public void ClearEdit() => IsEdited = false;
    }
}
