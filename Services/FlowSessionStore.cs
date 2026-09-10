namespace BlazorDrawFBP.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Mas.Infrastructure.Common;

public class FlowSessionStore : IFlowSessionStore
{
    private readonly ConcurrentDictionary<Guid, FlowSession> _sessions = new();

    public int SessionCount => _sessions.Count;

    public FlowSession GetOrCreateSession(Guid flowId, Func<IFbpRuntimeService>? runtimeFactory = null)
    {
        return _sessions.AddOrUpdate(
            flowId,
            id =>
            {
                var runtime = runtimeFactory?.Invoke() ?? new FbpRuntimeService(new ConnectionManager());
                return new FlowSession
                {
                    Id = id,
                    RuntimeService = runtime,
                };
            },
            (id, existing) =>
            {
                existing.MarkAttached();
                return existing;
            }
        );
    }

    public bool TryGetSession(Guid flowId, [NotNullWhen(true)] out FlowSession? session) =>
        _sessions.TryGetValue(flowId, out session);

    public bool RemoveSession(Guid flowId, [NotNullWhen(true)] out FlowSession? removedSession) =>
        _sessions.TryRemove(flowId, out removedSession);

    public IReadOnlyCollection<FlowSession> GetAllSessions() =>
        _sessions.Values.ToArray();

    public void MarkAttached(Guid flowId)
    {
        if (_sessions.TryGetValue(flowId, out var session))
        {
            session.MarkAttached();
        }
    }

    public void MarkDetached(Guid flowId)
    {
        if (_sessions.TryGetValue(flowId, out var session))
        {
            session.MarkDetached();
        }
    }
}
