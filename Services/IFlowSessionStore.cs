using System.Diagnostics.CodeAnalysis;

namespace BlazorDrawFBP.Services;

public interface IFlowSessionStore
{
    int SessionCount { get; }
    FlowSession GetOrCreateSession(Guid flowId, Func<IFbpRuntimeService>? runtimeFactory = null);
    bool TryGetSession(Guid flowId, [NotNullWhen(true)] out FlowSession? session);
    bool RemoveSession(Guid flowId, [NotNullWhen(true)] out FlowSession? removedSession);
    IReadOnlyCollection<FlowSession> GetAllSessions();
    void MarkAttached(Guid flowId);
    void MarkDetached(Guid flowId);
}
