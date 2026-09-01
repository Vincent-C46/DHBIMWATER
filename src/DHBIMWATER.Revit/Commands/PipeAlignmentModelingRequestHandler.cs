using Autodesk.Revit.UI;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.UseCases.Gis;

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

    public PipeAlignmentModelingRequest Request { get; } = new();
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

        try
        {
            var progress = new DelegateProgress<PipeAlignmentProgress>(value => ProgressReported?.Invoke(value));

            if (requestId == PipeAlignmentModelingRequestId.CreateModel)
            {
                if (PendingRequest is not { } request) return;
                PendingRequest = null;

                var preview = _useCase.Preview(request, progress);
                app.ActiveUIDocument?.RefreshActiveView();
                _confirmedRequest = request;
                PreviewReady?.Invoke(preview);
                return;
            }

            if (requestId == PipeAlignmentModelingRequestId.FinalizeModel)
            {
                if (_confirmedRequest is not { } request)
                {
                    Failed?.Invoke("확인 대기 중인 미리보기 요청이 없습니다.");
                    return;
                }

                Completed?.Invoke(_useCase.Execute(request, progress));
                _confirmedRequest = null;
                return;
            }
        }
        catch (Exception ex)
        {
            _confirmedRequest = null;
            Failed?.Invoke(ex.Message);
        }
    }

    public string GetName() => "PipeAlignmentModelingRequest";

    private sealed class DelegateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public DelegateProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }
}
