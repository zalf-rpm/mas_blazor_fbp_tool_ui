using Blazor.Diagrams;
using Blazor.Diagrams.Core.Models;
using Newtonsoft.Json.Linq;

namespace BlazorDrawFBP.Services;

public interface IFlowDocumentService
{
    Task<JObject> ExportFlowJsonAsync(BlazorDiagram diagram, IFbpRuntimeService runtime);
    Task<string> ExportFlowMermaidAsync(BlazorDiagram diagram, IFbpRuntimeService runtime);

    Task<(JObject? Json, string? Mermaid)> ExportFlowDocumentAsync(
        BlazorDiagram diagram,
        IFbpRuntimeService runtime,
        bool asMermaid
    );

    Task LoadFlowFromJsonAsync(
        BlazorDiagram diagram,
        IFbpRuntimeService runtime,
        JObject dia,
        Func<Task>? onZoomToFit = null,
        Action<NodeModel>? onNodeLayoutChanged = null
    );
}
