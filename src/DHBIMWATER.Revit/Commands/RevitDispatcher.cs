using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using System.Runtime.ExceptionServices;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Revit.Commands;

/// <summary>모델리스 UI 스레드의 Revit API 읽기를 ExternalEvent로 동기 마샬링한다.</summary>
internal sealed class RevitDispatcher : IRevitDispatcher, IDisposable
{
    private readonly object _runSync = new();
    /// <summary>Revit이 모달이거나 배치 ExternalEvent가 도는 중이면 Raise는 Accepted지만 핸들러는 한참 뒤에야 돈다.
    /// 무한 대기하면 UI 스레드가 굳는다(docs/209 § B).</summary>
    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(30);
    private readonly RevitDispatcherHandler _handler = new();
    private readonly ExternalEvent _externalEvent;
    private readonly int _revitThreadId;
    private readonly Func<Autodesk.Revit.DB.Document?> _document;
    /// <summary>이 디스패처를 만든 시점(= 창을 연 시점)의 문서(docs/237 § B). 다른 문서가 활성이면 읽기도 거부한다 —
    /// 콤보 목록·파라미터 후보가 엉뚱한 문서에서 채워지지 않게.</summary>
    private readonly Autodesk.Revit.DB.Document? _owner;
    private bool _disposed;

    public RevitDispatcher(Func<Autodesk.Revit.DB.Document?> document)
    {
        _revitThreadId = Environment.CurrentManagedThreadId;
        _externalEvent = ExternalEvent.Create(_handler);
        _document = document;
        _owner = document();
    }

    public void Run(Action action) => Run<object?>(() =>
    {
        action();
        return null;
    });

    public T Run<T>(Func<T> func)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId == _revitThreadId) return func();

        lock (_runSync)
        {
            // 문서 확인은 Revit 스레드에서 실행 직전에 한다 — 그 시점의 활성 문서가 기준이다(docs/237 § B).
            using var request = new RevitDispatchRequest<T>(() =>
            {
                DocumentGuard.EnsureOwner(_owner, _document());
                return func();
            });
            _handler.SetPending(request.Execute);
            var status = _externalEvent.Raise();
            if (status is ExternalEventRequest.Denied or ExternalEventRequest.TimedOut)
            {
                _handler.ClearPending(request.Execute);
                throw new DhbimwaterException(ErrorCodes.ExternalEventRejected, $"Revit이 요청을 받지 않았습니다({status}). 열려 있는 대화상자나 명령을 끝낸 뒤 다시 시도하세요.");
            }

            if (!request.Wait(DispatchTimeout))
            {
                _handler.ClearPending(request.Execute);
                throw new DhbimwaterException(ErrorCodes.PA_DispatcherTimeout,
                    "Revit이 요청에 응답하지 않습니다. 열려 있는 대화상자나 진행 중인 배치가 끝난 뒤 다시 시도하세요.");
            }
            return request.GetResult();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _externalEvent.Dispose();
    }

    private sealed class RevitDispatcherHandler : IExternalEventHandler
    {
        private Action? _pending;

        public void SetPending(Action action)
        {
            if (Interlocked.CompareExchange(ref _pending, action, null) is not null)
                throw new DhbimwaterException(ErrorCodes.PA_DispatcherBusy, "처리되지 않은 Revit 요청이 남아 있습니다.");
        }

        public void ClearPending(Action action) => Interlocked.CompareExchange(ref _pending, null, action);

        public void Execute(UIApplication app) => Interlocked.Exchange(ref _pending, null)?.Invoke();
        public string GetName() => "DHBIMWATER Revit Dispatcher";
    }

    private sealed class RevitDispatchRequest<T> : IDisposable
    {
        private readonly Func<T> _func;
        private readonly ManualResetEventSlim _completed = new(false);
        private T? _result;
        private ExceptionDispatchInfo? _error;

        public RevitDispatchRequest(Func<T> func) => _func = func;

        public void Execute()
        {
            try { _result = _func(); }
            catch (Exception ex) { _error = ExceptionDispatchInfo.Capture(ex); }
            // 타임아웃으로 요청자가 이미 Dispose했으면 Set이 던진다 — Revit 스레드를 죽이지 않는다(docs/209 § B).
            finally { try { _completed.Set(); } catch (ObjectDisposedException) { } }
        }

        public bool Wait(TimeSpan timeout) => _completed.Wait(timeout);

        public T GetResult()
        {
            _error?.Throw();
            return _result!;
        }

        public void Dispose() => _completed.Dispose();
    }
}
