using Blazor.Diagrams.Core;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace BlazorDrawFBP.Models;

public class UpdatePortNameNode : NodeModel
{
    public UpdatePortNameNode(Point? position = null)
        : base(position) { }

    public string Label { get; set; } = "";
    public string PortName { get; set; } = "";

    public CapnpFbpPortModel PortModel { get; set; } = null!;

    public Diagram Container { get; set; } = null!;
}
