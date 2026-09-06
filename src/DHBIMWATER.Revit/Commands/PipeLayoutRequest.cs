using System.Collections.Concurrent;

namespace DHBIMWATER.Revit.Commands;

internal enum PipeLayoutRequestId { None, CreateModel, PickOutline }

internal sealed class PipeLayoutRequest
{
    private readonly ConcurrentQueue<PipeLayoutRequestId> _requestIds = new();

    public bool TryTake(out PipeLayoutRequestId requestId) => _requestIds.TryDequeue(out requestId);
    public void Make(PipeLayoutRequestId requestId) => _requestIds.Enqueue(requestId);
}
