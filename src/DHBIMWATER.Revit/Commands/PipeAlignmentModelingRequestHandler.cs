using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.UseCases.Gis;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Revit.Commands;

internal enum PipeAlignmentModelingRequestId { None, CreateModel, FinalizeModel }

internal sealed class PipeAlignmentModelingRequest
{
    private int _requestId;

    public PipeAlignmentModelingRequestId Take() =>
        (PipeAlignmentModelingRequestId)Interlocked.Exchange(ref _requestId, (int)PipeAlignmentModelingRequestId.None);

    public void Make(PipeAlignmentModelingRequestId requestId) =>
        Interlocked.Exchange(ref _requestId, (int)requestId);
}

internal sealed class PipeAlignmentModelingRequestHandler : IExternalEventHandler
{
    private readonly ModelPipeAlignmentUseCase _useCase;
    private Application.DTOs.Gis.PipeAlignmentModelingRequest? _confirmedRequest;
    // 배치 중 취소 토큰(docs/209 § A-4). Cancel()은 UI 스레드, Token은 Revit 스레드에서 읽는다 — CTS는 스레드 안전하다.
    private CancellationTokenSource? _cts;
    // 핸들러가 아직 시작하지 않았을 때 들어온 취소(docs/236 § B). 1이면 다음 Execute가 시작하자마자 취소한다.
    private int _cancelRequested;

    /// <summary>진행 중이거나 대기 중인 미리보기·최종 배치를 취소한다.
    /// <para>Raise는 수락됐지만 핸들러가 아직 돌지 않았으면 CTS가 없다 — 종전에는 이 취소가 사라져 화면이 "취소 중"에 멈췄다.
    /// 플래그를 남겨 두면 <see cref="Execute"/>가 시작하자마자 취소로 끝낸다(PA-304).</para>
    /// <para>finally의 CTS Dispose와 겹치면 <see cref="ObjectDisposedException"/>이 UI 스레드에서 난다 — 삼킨다(이미 끝난 실행이다).</para></summary>
    public void Cancel()
    {
        Interlocked.Exchange(ref _cancelRequested, 1);
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>새 요청을 올리기 전에 지난 취소 플래그를 내린다(docs/236 § B).</summary>
    public void ResetCancel() => Interlocked.Exchange(ref _cancelRequested, 0);

    public PipeAlignmentModelingRequest Request { get; } = new();
    /// <summary>창을 연 문서(docs/237 § B). 다른 문서가 활성이면 미리보기·최종 배치를 실행하지 않는다.</summary>
    public Document? OwnerDocument { get; set; }
    public Application.DTOs.Gis.PipeAlignmentModelingRequest? PendingRequest { get; set; }
    public Action<PipeAlignmentProgress>? ProgressReported { get; set; }
    public Action<PipeAlignmentModelingResult>? Completed { get; set; }
    public Action<PipeAlignmentModelingResult>? PreviewRetained { get; set; }
    public Action<PipeAlignmentModelingResult>? PreviewReady { get; set; }
    public Action<string>? Failed { get; set; }

    public PipeAlignmentModelingRequestHandler(ModelPipeAlignmentUseCase useCase) => _useCase = useCase;

    public void Execute(UIApplication app)
    {
        var requestId = Request.Take();

        _cts = new CancellationTokenSource();
        // 시작 전에 들어온 취소를 반영한다 — UseCase가 첫 토큰 검사에서 PA-304로 끝낸다(docs/236 § B).
        if (Interlocked.Exchange(ref _cancelRequested, 0) == 1) _cts.Cancel();
        try
        {
            // 실패는 아래 catch가 Failed([SYS-108] …)로 창에 알린다(docs/237 § B).
            if (requestId != PipeAlignmentModelingRequestId.None) DocumentGuard.EnsureOwner(OwnerDocument, app);
            var progress = new DelegateProgress<PipeAlignmentProgress>(value => ProgressReported?.Invoke(value));

            if (requestId == PipeAlignmentModelingRequestId.CreateModel)
            {
                if (PendingRequest is not { } request)
                {
                    // 콜백 없이 돌아가면 창이 IsBusy=true에서 영구 정지한다(docs/209 § 0-7).
                    Failed?.Invoke($"[{ErrorCodes.RequestLost}] 생성 요청이 유실됐습니다. 다시 시도하세요.");
                    return;
                }
                PendingRequest = null;

                var preview = _useCase.Preview(request, progress, _cts.Token);
                app.ActiveUIDocument?.RefreshActiveView();
                ZoomToPreview(app.ActiveUIDocument, preview.PreviewElementIds);
                _confirmedRequest = request;
                PreviewReady?.Invoke(preview);
                return;
            }

            if (requestId == PipeAlignmentModelingRequestId.FinalizeModel)
            {
                if (_confirmedRequest is not { } request)
                {
                    Failed?.Invoke($"[{ErrorCodes.RequestLost}] 확인 대기 중인 미리보기 요청이 없습니다.");
                    return;
                }

                Completed?.Invoke(_useCase.Execute(request, progress, _cts.Token));
                _confirmedRequest = null;
                return;
            }
        }
        catch (Exception ex)
        {
            _confirmedRequest = null;
            ErrorPresenter.Log("PipeAlignment", ex);
            Failed?.Invoke(ErrorPresenter.Format(ex));
        }
        finally { _cts?.Dispose(); _cts = null; }
    }

    public string GetName() => "PipeAlignmentModelingRequest";

    /// <summary>
    /// 선형 미리보기 DirectShape 전체가 보이도록 <b>현재 활성 뷰</b>를 확대·이동한다(docs/226 § D).
    /// ShowElements는 요소가 활성 뷰에 없으면 다른 뷰를 열 수 있어 쓰지 않는다. 선택 집합은 건드리지 않는다.
    /// 평면뷰는 뷰 범위 밖 Z면 위치로만 이동하고 선은 안 보일 수 있다. Zoom 실패는 미리보기 실패가 아니므로 로그만 남긴다.
    /// </summary>
    private static void ZoomToPreview(UIDocument? uiDoc, IReadOnlyList<long>? elementIds)
    {
        if (uiDoc is null || elementIds is not { Count: > 0 }) return;
        try
        {
            var view = uiDoc.ActiveView;
            // 모델 요소를 확대할 수 있는 뷰만 — 일람표·시트·범례·도면 등은 건너뛴다.
            if (view is not (ViewPlan or View3D or ViewSection)) return;
            var doc = uiDoc.Document;
            XYZ? min = null, max = null;
            foreach (var id in elementIds)
            {
                if (doc.GetElement(new ElementId(id))?.get_BoundingBox(null) is not { } box) continue;
                min = min is null ? box.Min : new XYZ(Math.Min(min.X, box.Min.X), Math.Min(min.Y, box.Min.Y), Math.Min(min.Z, box.Min.Z));
                max = max is null ? box.Max : new XYZ(Math.Max(max.X, box.Max.X), Math.Max(max.Y, box.Max.Y), Math.Max(max.Z, box.Max.Z));
            }
            if (min is null || max is null) return;
            // 여백: 대각선의 5%, 최소 3ft(약 1m).
            var margin = Math.Max(min.DistanceTo(max) * 0.05, 3d);
            var offset = new XYZ(margin, margin, margin);
            uiDoc.GetOpenUIViews().FirstOrDefault(x => x.ViewId == view.Id)?.ZoomAndCenterRectangle(min - offset, max + offset);
        }
        catch (Exception ex) { ErrorPresenter.Log("PipeAlignment", ex); }
    }

    private sealed class DelegateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public DelegateProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }
}
