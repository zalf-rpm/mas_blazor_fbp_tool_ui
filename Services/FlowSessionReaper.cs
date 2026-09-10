namespace BlazorDrawFBP.Services;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class FlowSessionReaper : BackgroundService
{
    private readonly IFlowSessionStore _sessionStore;
    private readonly ILogger<FlowSessionReaper> _logger;
    private readonly TimeSpan _checkInterval;

    public FlowSessionReaper(
        IFlowSessionStore sessionStore,
        ILogger<FlowSessionReaper> logger,
        TimeSpan? checkInterval = null
    )
    {
        _sessionStore = sessionStore;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(30);
    }

    public async Task<int> ReapExpiredSessionsAsync(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var candidateSessions = _sessionStore
            .GetAllSessions()
            .Where(session =>
                session.IsDetached
                && session.DetachedAt.HasValue
                && (now - session.DetachedAt.Value) > session.Ttl
                && !session.RuntimeService.IsExecutingFlow
            )
            .ToList();

        var reapedCount = 0;
        foreach (var session in candidateSessions)
        {
            if (!_sessionStore.RemoveSession(session.Id, out var removedSession))
                continue;

            try
            {
                if (removedSession.RuntimeService is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync();
                }
                else
                {
                    await removedSession.RuntimeService.ClearDiagramAsync();
                }

                reapedCount++;
                _logger.LogInformation(
                    "Reaped expired detached flow session {FlowId} after TTL of {Ttl}",
                    session.Id,
                    session.Ttl
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while disposing expired flow session {FlowId}",
                    session.Id
                );
            }
        }

        return reapedCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_checkInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await ReapExpiredSessionsAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in FlowSessionReaper loop.");
            }
        }
    }
}
