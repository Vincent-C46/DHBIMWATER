using System.Windows.Input;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Application.UseCases.Documentation;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;

namespace DHBIMWATER.UI.ViewModels.Documentation;

/// <summary>
/// 부재 정보 갱신 창. 수동 배치 배관 부재·가변 패밀리에 수량산출용 매개변수 채움.
/// <para>모달 창 + 외부 명령 실행 중 → API 컨텍스트 유효. 실행은 동기, ExternalEvent 안 씀.</para>
/// <para>관종 = 재질 → 유형·패밀리 이름 판정, 못 정하면 창에서 고른 값 일괄. 격점번호는 안 채움(docs/141 § 2 판단 5), 야간은 MEP 배관만 해제(0) 기록.</para>
/// </summary>
public sealed class PipeMaterialRefreshViewModel : ViewModelBase
{
    private readonly BackfillPipeMaterialParametersUseCase _useCase;
    private readonly IPipeCatalogSource _catalog;
    private PipeMaterialBackfillScan _scan = new(0, false);
    private bool _recountAll;
    private string _resultText = "";
    private MaterialChoice _fallbackMaterial;

    public PipeMaterialRefreshViewModel(BackfillPipeMaterialParametersUseCase useCase, IPipeCatalogSource catalog)
    {
        _useCase = useCase;
        _catalog = catalog;
        MaterialChoices =
        [
            new MaterialChoice(null, "(비움)"),
            .. new[] { PipeMaterial.DuctileIron, PipeMaterial.Steel, PipeMaterial.Pe, PipeMaterial.Pvc }
                .Select(x => new MaterialChoice(PipeAlignmentParameters.Code(x), $"{PipeAlignmentParameters.Code(x)} ({PipeAlignmentParameters.Label(x)})")),
        ];
        _fallbackMaterial = MaterialChoices[0];
        RunCommand = new RelayCommand(_ => Run(), _ => CanRun);
        Rescan();
    }

    public ICommand RunCommand { get; }

    public int CandidateCount => _scan.CandidateCount;
    public int AdaptiveCount => _scan.AdaptiveCandidateCount;
    public int RecountCount => _scan.RecountCount;
    public int MepPipeCount => _scan.MepPipeCandidateCount;
    public int MaterialMissingCount => _scan.MaterialMissingCount;
    public int MaterialUnresolvedCount => _scan.MaterialUnresolvedCount;
    public bool HasUnresolvedMaterial => MaterialUnresolvedCount > 0;

    /// <summary>관종 판정 못 한 부재에 일괄로 쓸 값. 첫 항목 = 비움.</summary>
    public IReadOnlyList<MaterialChoice> MaterialChoices { get; }
    public MaterialChoice FallbackMaterial
    {
        get => _fallbackMaterial;
        set { if (SetProperty(ref _fallbackMaterial, value)) OnPropertyChanged(nameof(CanRun)); }
    }
    public string ScopeText => _scan.FromSelection ? "선택한 부재 범위" : "문서 전체 (선택한 부재 중 대상이 없음)";

    /// <summary>true = 이미 값 있는 부재 개수까지 수량 규칙으로 다시 씀. 손으로 고친 개수 덮어씀.</summary>
    public bool RecountAll
    {
        get => _recountAll;
        set { if (SetProperty(ref _recountAll, value)) OnPropertyChanged(nameof(CanRun)); }
    }

    public bool CanRun => CandidateCount > 0 || AdaptiveCount > 0 || MepPipeCount > 0 || (RecountAll && RecountCount > 0)
        || MaterialMissingCount > MaterialUnresolvedCount || (HasUnresolvedMaterial && FallbackMaterial.Code is not null);

    public string ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }

    private void Rescan()
    {
        try { _scan = _useCase.ScanCandidates(); }
        catch (Exception ex) { _scan = new(0, false); ResultText = "대상 확인 실패: " + ex.Message; }
        OnPropertyChanged(nameof(CandidateCount));
        OnPropertyChanged(nameof(AdaptiveCount));
        OnPropertyChanged(nameof(RecountCount));
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(MepPipeCount));
        OnPropertyChanged(nameof(MaterialMissingCount));
        OnPropertyChanged(nameof(MaterialUnresolvedCount));
        OnPropertyChanged(nameof(HasUnresolvedMaterial));
        OnPropertyChanged(nameof(CanRun));
    }

    private void Run()
    {
        var lines = new List<string>();
        try
        {
            // 무게 규격표 못 열면 쓰지 않음 — 격점상세도 자동 기록과 같은 태도
            if (!_catalog.TryEnsureAvailable(out var error))
            {
                ResultText = string.IsNullOrWhiteSpace(error) ? "규격표를 열 수 없어 실행하지 않았습니다." : error;
                return;
            }
            if (!string.IsNullOrWhiteSpace(error)) lines.Add("경고: " + error);
            var fallback = FallbackMaterial.Code;
            var result = RecountAll ? _useCase.Execute(fallback) : _useCase.ExecuteEmpty(fallback);
            if (result.MaterialFilledCount > 0) lines.Add($"관종(DH_관종): {result.MaterialFilledCount}개 기록");
            lines.Add($"배관 이음쇠·밸브류: {result.WrittenCount}개 기록, {result.SkippedCount}개 건너뜀");
            if (result.MepPipeFilledCount > 0) lines.Add($"MEP 배관: {result.MepPipeFilledCount}개 기록");
            if (result.AdaptiveFilledCount > 0) lines.Add($"계획관로 가변 패밀리: {result.AdaptiveFilledCount}개 기록");
            if (result.RecountedCount > 0) lines.Add($"접합·부설·절단 수 재산정: {result.RecountedCount}개");
            lines.AddRange(result.Warnings.Select(x => "경고: " + x));
        }
        catch (Exception ex) { lines.Add("실행 실패: " + ex.Message); }
        ResultText = string.Join(Environment.NewLine, lines);
        Rescan();
    }
}

/// <summary>관종 일괄 지정 콤보 항목. Code null = 비움.</summary>
public sealed record MaterialChoice(string? Code, string Label);
