using Blazor.Diagrams;

namespace BlazorDrawFBP.Services;

public interface IFbpDiagramFactory
{
    BlazorDiagram CreateConfiguredDiagram(
        IFbpRuntimeService runtime,
        Action? onStructureChanged = null,
        Action? onDiagramInteracted = null,
        Func<Task>? onZoomToFit = null
    );
}
