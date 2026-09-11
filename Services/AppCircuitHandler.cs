using Microsoft.AspNetCore.Components.Server.Circuits;

namespace BlazorDrawFBP.Services;

public class AppCircuitHandler(CleanupDiagramService service) : CircuitHandler
{
    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken ct)
    {
        return service.Cleanup();
    }
}

public class CleanupDiagramService
{
    private Func<Task>? _cleanupAction;

    public void RegisterCleanup(Func<Task> cleanupAction)
    {
        _cleanupAction = cleanupAction;
    }

    public void UnregisterCleanup()
    {
        _cleanupAction = null;
    }

    public async Task Cleanup()
    {
        if (_cleanupAction != null)
            await _cleanupAction();
    }
}
