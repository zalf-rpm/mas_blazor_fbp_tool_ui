using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Mas.Infrastructure.Common;

namespace BlazorDrawFBP.Services;

public class FlowSessionStore : IFlowSessionStore
{
    private readonly ConcurrentDictionary<Guid, FlowSession> _sessions = new();

    public int SessionCount => _sessions.Count;

    public FlowSession GetOrCreateSession(
        Guid flowId,
        Func<IFbpRuntimeService>? runtimeFactory = null
    )
    {
        return _sessions.AddOrUpdate(
            flowId,
            id =>
            {
                var runtime = runtimeFactory?.Invoke() ?? CreateDefaultRuntime();
                return new FlowSession { Id = id, RuntimeService = runtime };
            },
            (id, existing) =>
            {
                existing.MarkAttached();
                return existing;
            }
        );
    }

    public bool TryGetSession(Guid flowId, [NotNullWhen(true)] out FlowSession? session)
    {
        return _sessions.TryGetValue(flowId, out session);
    }

    public bool RemoveSession(Guid flowId, [NotNullWhen(true)] out FlowSession? removedSession)
    {
        return _sessions.TryRemove(flowId, out removedSession);
    }

    public IReadOnlyCollection<FlowSession> GetAllSessions()
    {
        return _sessions.Values.ToArray();
    }

    public void MarkAttached(Guid flowId)
    {
        if (_sessions.TryGetValue(flowId, out var session))
            session.MarkAttached();
    }

    public void MarkDetached(Guid flowId)
    {
        if (_sessions.TryGetValue(flowId, out var session))
            session.MarkDetached();
    }

    public static IFbpRuntimeService CreateDefaultRuntime()
    {
        var conMan = new ConnectionManager();
        var restorer = new Restorer { TcpHost = ConnectionManager.GetLocalIPAddress() };
        conMan.Restorer = restorer;
        conMan.Bind(IPAddress.Any, 0, restorer);
        restorer.TcpPort = conMan.Port;
        return new FbpRuntimeService(conMan);
    }
}
