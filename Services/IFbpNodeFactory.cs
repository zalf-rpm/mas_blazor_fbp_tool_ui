namespace BlazorDrawFBP.Services;

using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Mas.Schema.Fbp;
using Newtonsoft.Json.Linq;

public interface IFbpNodeFactory
{
    NodeModel AddFbpNode(
        BlazorDiagram diagram,
        IFbpRuntimeService runtime,
        Point position,
        Component component,
        JObject? initNode = null,
        string cmd = "",
        Action<NodeModel>? onNodeLayoutChanged = null
    );

    Component? CreateComponentFromJson(JToken jComp);
}
