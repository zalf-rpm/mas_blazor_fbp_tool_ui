namespace BlazorDrawFBP.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

public interface IFlowSessionStore
{
    FlowSession GetOrCreateSession(Guid flowId, Func<IFbpRuntimeService>? runtimeFactory = null);
    bool TryGetSession(Guid flowId, [NotNullWhen(true)] out FlowSession? session);
    bool RemoveSession(Guid flowId, [NotNullWhen(true)] out FlowSession? removedSession);
    IReadOnlyCollection<FlowSession> GetAllSessions();
    void MarkAttached(Guid flowId);
    void MarkDetached(Guid flowId);
    int SessionCount { get; }
}
