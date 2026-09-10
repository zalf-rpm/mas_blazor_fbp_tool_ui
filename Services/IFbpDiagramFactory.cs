namespace BlazorDrawFBP.Services;

using System;
using System.Threading.Tasks;
using Blazor.Diagrams;

public interface IFbpDiagramFactory
{
    BlazorDiagram CreateConfiguredDiagram(
        IFbpRuntimeService runtime,
        Action? onStructureChanged = null,
        Action? onDiagramInteracted = null,
        Func<Task>? onZoomToFit = null
    );
}
