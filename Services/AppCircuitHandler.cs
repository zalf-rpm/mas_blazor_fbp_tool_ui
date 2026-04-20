using System.Threading;
using System.Threading.Tasks;
using BlazorDrawFBP.Pages;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace BlazorDrawFBP.Services;

public class AppCircuitHandler(CleanupDiagramService service) : CircuitHandler
{
    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken ct)
    {
        return service.Cleanup(); //circuit.Id);
        //return Task.CompletedTask;
    }
}

public class CleanupDiagramService
{
    public Editor Editor { get; set; }

    public async Task Cleanup()
    {
        if (Editor != null)
            await Editor.ClearDiagram();
    }
}
