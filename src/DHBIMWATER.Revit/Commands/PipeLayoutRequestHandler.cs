using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Revit.Commands;

/// <summary>배관 생성 UseCase와 피킹(기존 객체 — 벽체·기둥, 기존 배관)을 Revit API 컨텍스트에서 실행한다.</summary>
internal sealed class PipeLayoutRequestHandler : IExternalEventHandler
{
    private readonly CreateValvePipingUseCase _useCase;
    private readonly IValveRoomOutlinePickRepo _outlinePick;
    private readonly IValveRoomPipingPickRepo _pipingPick;
    private readonly SavePipeLayoutFamilySettingsUseCase _saveFamilySettings;

    public PipeLayoutRequest Request { get; } = new();
    public PipeNetworkDefinition? PendingNetwork { get; set; }
    /// <summary>저장 대기 중인 패밀리 설정(docs/175 § E-6). 창이 모델리스라 저장도 ExternalEvent로 들어온다.</summary>
    public PipeLayoutFamilySettings? PendingFamilySettings { get; set; }
    /// <summary>이번 모델 생성과 함께 남길 패밀리 설정(docs/195 § D). <see cref="PendingFamilySettings"/>와 따로 두는 이유 —
    /// 그쪽은 [저장] 버튼이 만든 독립 요청이고, 이쪽은 <see cref="PipeLayoutRequestId.CreateModel"/>에 얹혀 오는 곁다리다.
    /// 한 칸을 공유하면 저장 요청과 생성 요청이 큐에 겹칠 때 서로를 덮어쓴다.</summary>
    public PipeLayoutFamilySettings? PendingNetworkFamilySettings { get; set; }
    /// <summary>외곽 피킹·창 닫기에서 함께 남길 레이아웃 저장본(docs/204 § E-2).</summary>
    public PipeLayoutFamilySettings? PendingLayoutSettings { get; set; }
    /// <summary>Revit에서 선택할 부재(docs/179 § F-2). DHBoost QuantityRequestHandler.ElementIdsToSelect와 같은 역할이다.</summary>
    public IReadOnlyList<long> ElementIdsToSelect { get; set; } = [];
    /// <summary>창을 연 문서(docs/237 § B). 다른 문서가 활성이면 어떤 요청도 실행하지 않는다 — 원본 삭제가 엉뚱한 문서에서 돌지 않게.</summary>
    public Autodesk.Revit.DB.Document? OwnerDocument { get; set; }
    /// <summary>[Revit에서 보기] 결과(요청 수, 실제 선택 수)를 ViewModel로 되돌리는 콜백(docs/216 § B-1).
    /// 삭제된 부재를 거른 뒤의 수라 상태줄이 실제와 맞는다. UI 스레드 마샬링은 호출측이 담당한다.</summary>
    public Action<int, int>? ElementsSelected { get; set; }

    /// <summary>피킹 결과를 ViewModel로 되돌리는 콜백. UI 스레드 마샬링은 호출측이 담당한다.</summary>
    public Action<ValveRoomOutline?>? OutlinePicked { get; set; }
    /// <summary>기존 배관 피킹 결과를 ViewModel로 되돌리는 콜백(docs/158). UI 스레드 마샬링은 호출측이 담당한다.</summary>
    public Action<PipeLayoutImport?>? PipingPicked { get; set; }
    /// <summary>생성 결과와 함께 캔버스 Guid → Revit ElementId 매핑을 되돌린다(docs/179 § F-3).
    /// 실패 경로는 빈 딕셔너리를 넘긴다 — 롤백됐으므로 새 매핑이 없다.</summary>
    public Action<bool, string, IReadOnlyList<PipeConnectionFailure>, IReadOnlyDictionary<Guid, IReadOnlyList<long>>>? ModelCreationCompleted { get; set; }
    /// <summary>패밀리 설정 저장 결과(성공 여부, 상태 메시지)를 ViewModel로 되돌리는 콜백. UI 스레드 마샬링은 호출측이 담당한다.</summary>
    public Action<bool, string>? FamilySettingsSaved { get; set; }
    /// <summary>피킹 요청이 예외로 끝났을 때 상태줄 문구(`[코드] 메시지`)를 돌려준다(docs/239 § C). 종전에는 null 콜백만 가서 "취소"로 보였다.</summary>
    public Action<string>? RequestFailed { get; set; }

    public PipeLayoutRequestHandler(CreateValvePipingUseCase useCase, IValveRoomOutlinePickRepo outlinePick,
        IValveRoomPipingPickRepo pipingPick, SavePipeLayoutFamilySettingsUseCase saveFamilySettings)
    {
        _useCase = useCase;
        _outlinePick = outlinePick;
        _pipingPick = pipingPick;
        _saveFamilySettings = saveFamilySettings;
    }

    public void Execute(UIApplication uiapp)
    {
        while (Request.TryTake(out var requestId))
            ExecuteRequest(uiapp, requestId);
    }

    /// <summary>모델을 만든 그 설정을 프로젝트에 함께 남긴다(docs/195 § D). [저장] 버튼을 누르지 않아도
    /// 다음 세션에 값이 돌아오게 하려는 것이다 — 저장본이 없으면 전 칸이 이름 추정으로 되돌아간다.
    /// <para>예외를 올리지 않고 사유만 돌려준다. 방금 커밋한 모델은 멀쩡한데 저장 실패 때문에
    /// 생성 전체가 실패로 보이면 안 되기 때문이다.</para>
    /// <para>성공해도 <see cref="FamilySettingsSaved"/>를 부르지 않는다 — 그 콜백은 ViewModel의
    /// <c>_familySettingsLoaded</c>를 세워 "기존 배관 가져오기"가 패밀리 칸을 덮지 못하게 만든다(PipeLayoutViewModel.cs:1686).
    /// 곁다리 저장이 그 판단까지 바꾸면 역가져오기 동작이 조용히 달라진다.</para></summary>
    private string? SaveFamilySettingsWithModel()
    {
        if (PendingNetworkFamilySettings is not { } settings) return null;
        PendingNetworkFamilySettings = null;
        try
        {
            _saveFamilySettings.Execute(settings);
            return null;
        }
        catch (Exception ex)
        {
            return ErrorPresenter.Format(ex);
        }
    }

    /// <summary>곁다리 저장(docs/204 § E-2). 성공해도 <see cref="FamilySettingsSaved"/>를 부르지 않는다.</summary>
    private void SaveLayoutSettings()
    {
        if (PendingLayoutSettings is not { } settings) return;
        PendingLayoutSettings = null;
        try { _saveFamilySettings.Execute(settings); }
        catch (Exception ex) { ErrorPresenter.Show("레이아웃 저장 실패", "PipeLayout", new DhbimwaterException(ErrorCodes.PL_FamilySettingsSaveFailed, $"외곽·포트를 프로젝트에 저장하지 못했습니다.\n{ex.Message}", ex)); }
    }

    private void ExecuteRequest(UIApplication uiapp, PipeLayoutRequestId requestId)
    {
        var modelCompletionNotified = false;
        try
        {
            // 요청 종류별 catch가 결과 통지(생성)·null 콜백(피킹)·대화상자를 그대로 처리한다(docs/237 § B).
            DocumentGuard.EnsureOwner(OwnerDocument, uiapp);
            switch (requestId)
            {
                case PipeLayoutRequestId.CreateModel:
                    if (PendingNetwork is null)
                    {
                        PendingNetworkFamilySettings = null;
                        NotifyModelCreationCompleted(false, $"[{ErrorCodes.RequestLost}] 생성 요청이 유실됐습니다. 다시 시도하세요.", []);
                        modelCompletionNotified = true;
                        break;
                    }
                    var network = PendingNetwork;
                    PendingNetwork = null;
                    var result = _useCase.Execute(network);
                    // 모델 트랜잭션은 이 줄에서 이미 커밋됐다. 패밀리 설정 저장은 자기 트랜잭션을 여는 별개 작업이므로
                    // (SavePipeLayoutFamilySettingsUseCase.cs:18) 반드시 커밋 "뒤"에 부른다 — 안에서 부르면 중첩 트랜잭션이다.
                    var settingsSaveError = SaveFamilySettingsWithModel();
                    // 경고는 배치 실패가 아니다(트랜잭션은 이미 커밋됐다). 성공으로 알리되 본문을 함께 보여준다.
                    // 건너뛴 부속도 마찬가지다 — 나머지는 정상 생성됐으므로 성공으로 알리고 alert로만 보여준다(docs/145 § 4-7).
                    NotifyModelCreationCompleted(true, result.Warnings.Count == 0
                        ? result.Summary
                        : $"{result.Summary} 확인 필요 {result.Warnings.Count}건.", result.Skipped, result.PlacedElementIds);
                    modelCompletionNotified = true;
                    // 설정 저장 실패는 모델 생성 실패가 아니다(docs/195 § D). 상태줄로만 알린다 —
                    // 성공했을 때는 아무 것도 덮어쓰지 않아 모델 요약이 상태줄에 그대로 남는다.
                    if (settingsSaveError is not null)
                        FamilySettingsSaved?.Invoke(false, $"모델은 생성했습니다. 다만 패밀리 설정 저장에 실패했습니다: {settingsSaveError}");
                    if (result.Skipped.Count > 0)
                        ShowSkippedDialog(result.Skipped);
                    if (result.Warnings.Count > 0)
                        TaskDialog.Show("배관 생성 확인 필요", $"{result.Summary}\n\n- " + string.Join("\n- ", result.Warnings.Take(20)));
                    break;

                case PipeLayoutRequestId.PickOutline:
                    OutlinePicked?.Invoke(_outlinePick.PickExistingObjects());
                    SaveLayoutSettings();
                    break;

                case PipeLayoutRequestId.SaveLayout:
                    SaveLayoutSettings();
                    break;

                case PipeLayoutRequestId.SaveFamilySettings:
                    if (PendingFamilySettings is { } pendingSettings)
                    {
                        PendingFamilySettings = null;
                        _saveFamilySettings.Execute(pendingSettings);
                        FamilySettingsSaved?.Invoke(true, "패밀리 설정을 프로젝트에 저장했습니다.");
                    }
                    else FamilySettingsSaved?.Invoke(false, $"[{ErrorCodes.RequestLost}] 패밀리 설정 저장 요청이 유실됐습니다. 다시 시도하세요.");
                    break;

                case PipeLayoutRequestId.SelectInRevit:
                    // DHBoost QuantityRequestHandler.cs:97-101 이식(docs/179 § F-2).
                    // 같은 요청이 큐에 두 번 쌓이면(Pending, docs/235 § A) 두 번째는 목록이 이미 비어 있다 — 알리지 않고 건너뛴다.
                    if (ElementIdsToSelect.Count == 0) break;
                    var uiDoc = uiapp.ActiveUIDocument;
                    if (uiDoc is null) break;
                    // 지워진 부재를 걸러야 Revit이 예외를 던지지 않는다. 캔버스 매핑은 무효화하지 않으므로
                    // 사용자가 그 사이 모델에서 지운 부재가 섞여 들어올 수 있다(docs/179 § E-1).
                    // 이 파일은 Autodesk.Revit.DB를 using하지 않는다(다른 case가 필요로 하지 않는다).
                    // 짧은 이름 ElementId는 다른 형식으로 잡히므로 정규화한다.
                    var requestedCount = ElementIdsToSelect.Count;
                    var elementIds = ElementIdsToSelect
                        .Select(id => new Autodesk.Revit.DB.ElementId(id))
                        .Where(id => uiDoc.Document.GetElement(id) != null)
                        .ToList();
                    ElementIdsToSelect = [];
                    // 0개여도 알린다 — 그래야 "모두 삭제됐다"를 상태줄에 띄울 수 있다(docs/216 § B-1).
                    ElementsSelected?.Invoke(requestedCount, elementIds.Count);
                    if (elementIds.Count == 0) break;
                    uiDoc.Selection.SetElementIds(elementIds);
                    // DHBoost에는 없는 한 줄 — 화면 밖 부재도 찾아가야 하므로 뷰를 옮긴다(docs/179 § 0).
                    uiDoc.ShowElements(elementIds);
                    break;

                case PipeLayoutRequestId.PickPiping:
                    var import = _pipingPick.PickPiping();
                    PipingPicked?.Invoke(import);
                    // 경고는 가져오기 실패가 아니다. 캔버스에는 이미 반영했고, 확인할 거리만 알린다.
                    if (import is { Warnings.Count: > 0 })
                        TaskDialog.Show("배관 가져오기 확인 필요",
                            string.Join(Environment.NewLine, import.Warnings.Take(20).Select(x => "- " + x)));
                    break;
            }
        }
        catch (Exception ex)
        {
            if (requestId == PipeLayoutRequestId.CreateModel)
            {
                var failures = ex is PipeNetworkBuildException build ? build.Failures : [];
                NotifyModelCreationCompleted(false, ErrorPresenter.Format(ex), failures);
                modelCompletionNotified = true;
                ErrorPresenter.Show("배관 생성 오류", "PipeLayout", ex);
            }
            else if (requestId == PipeLayoutRequestId.PickPiping)
            {
                PipingPicked?.Invoke(null);
                RequestFailed?.Invoke(ErrorPresenter.Format(ex));
                ErrorPresenter.Show("배관 가져오기 오류", "PipeLayout", ex);
            }
            else if (requestId == PipeLayoutRequestId.SelectInRevit)
            {
                ElementIdsToSelect = [];
                ErrorPresenter.Show("Revit 선택 오류", "PipeLayout", ex);
            }
            else if (requestId == PipeLayoutRequestId.SaveFamilySettings)
            {
                FamilySettingsSaved?.Invoke(false, "패밀리 설정 저장 실패: " + ErrorPresenter.Format(ex));
                ErrorPresenter.Show("패밀리 설정 저장 오류", "PipeLayout", ex);
            }
            else if (requestId == PipeLayoutRequestId.SaveLayout)
            {
                PendingLayoutSettings = null;
                // docs/205 잔여 정리 — 코드 없는 예외는 PL-501로 포장해 ErrorPresenter로 통일.
                ErrorPresenter.Show("레이아웃 저장 오류", "PipeLayout",
                    ex is DhbimwaterException ? ex : new DhbimwaterException(ErrorCodes.PL_FamilySettingsSaveFailed, $"레이아웃을 저장하지 못했습니다.\n{ex.Message}", ex));
            }
            else
            {
                OutlinePicked?.Invoke(null);
                var error = ex is DhbimwaterException ? ex : new DhbimwaterException(ErrorCodes.PL_ExistingObjectPickFailed, $"기존 객체를 선택하지 못했습니다.\n{ex.Message}", ex);
                RequestFailed?.Invoke(ErrorPresenter.Format(error));
                ErrorPresenter.Show("기존 객체 선택 오류", "PipeLayout", error);
            }
        }
        finally
        {
            if (requestId == PipeLayoutRequestId.CreateModel && !modelCompletionNotified)
                NotifyModelCreationCompleted(false, $"[{ErrorCodes.RequestLost}] 생성 요청을 완료하지 못했습니다. 다시 시도하세요.", []);
        }
    }

    /// <summary>모델링하지 못해 건너뛴 곡관·T형·밸브를 한 번에 보여준다(docs/145 § 4-7).
    /// 생성 자체는 성공이므로 오류 대화상자가 아니라 알림이다.</summary>
    private static void ShowSkippedDialog(IReadOnlyList<PipeConnectionFailure> skipped)
    {
        var lines = skipped.Take(20).Select(x => FormattableString.Invariant(
            $"- {x.Role} '{x.FamilyTypeName}' 캔버스 ({x.CanvasPoint.X:N0}, {x.CanvasPoint.Y:N0})mm: {x.Detail}"))
            .ToList();
        if (skipped.Count > lines.Count) lines.Add($"- 그 외 {skipped.Count - lines.Count}건");
        var dialog = new TaskDialog("배관 생성 - 건너뛴 부속")
        {
            MainInstruction = $"모델링하지 못해 건너뛴 부속 {skipped.Count}개",
            MainContent = string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine +
                          "나머지 부재는 정상 생성됐습니다. 건너뛴 자리의 관 커넥터는 연결하지 않고 열어 두었습니다."
        };
        dialog.Show();
    }

    private void NotifyModelCreationCompleted(bool succeeded, string message, IReadOnlyList<PipeConnectionFailure> failures,
        IReadOnlyDictionary<Guid, IReadOnlyList<long>>? placedElementIds = null)
        => ModelCreationCompleted?.Invoke(succeeded, message, failures,
            placedElementIds ?? new Dictionary<Guid, IReadOnlyList<long>>());

    public string GetName() => "PipeLayoutRequest";
}
