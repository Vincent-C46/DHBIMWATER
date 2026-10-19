using System.Collections.Concurrent;

namespace DHBIMWATER.Revit.Commands;

internal enum PipeLayoutRequestId { None, CreateModel, PickOutline, PickPiping, SaveFamilySettings, SelectInRevit, SaveLayout, PickVerticalBranchStart, PlaceVerticalBranch }

internal sealed class PipeLayoutRequest
{
    private readonly ConcurrentQueue<PipeLayoutRequestId> _requestIds = new();

    public bool TryTake(out PipeLayoutRequestId requestId) => _requestIds.TryDequeue(out requestId);
    public void Make(PipeLayoutRequestId requestId) => _requestIds.Enqueue(requestId);

    /// <summary>Raise가 거부됐을 때 방금 넣은 요청을 되돌린다(docs/205 § C-2).
    /// ConcurrentQueue는 특정 항목만 뺄 수 없으므로 전부 비운다 — Denied/TimedOut이면 이벤트가 돌지 않으므로 실행될 요청이 없다.
    /// Pending(이미 올려 둔 이벤트가 아직 안 돈 것)에는 부르지 않는다 — 큐의 요청이 곧 실행된다(docs/235 § A).</summary>
    public void Clear() { while (_requestIds.TryDequeue(out _)) { } }
}
