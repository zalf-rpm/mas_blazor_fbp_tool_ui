using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;

namespace BlazorDrawFBP.Models;

public class RememberCapnpPortsLinkModel : LinkModel
{
    public RememberCapnpPortsLinkModel(Anchor source, Anchor target)
        : base(source, target)
    {}

    public RememberCapnpPortsLinkModel(string id, Anchor source, Anchor target)
        : base(id, source, target)
    {}

    public RememberCapnpPortsLinkModel(PortModel sourcePort, PortModel targetPort)
        : base(new SinglePortAnchor(sourcePort), new SinglePortAnchor(targetPort))
    {}

    public RememberCapnpPortsLinkModel(NodeModel sourceNode, NodeModel targetNode)
        : base(new ShapeIntersectionAnchor(sourceNode), new ShapeIntersectionAnchor(targetNode))
    {}

    public RememberCapnpPortsLinkModel(string id, PortModel sourcePort, PortModel targetPort)
        : base(id, new SinglePortAnchor(sourcePort), new SinglePortAnchor(targetPort))
    {}

    public RememberCapnpPortsLinkModel(string id, NodeModel sourceNode, NodeModel targetNode)
        : base(id, new ShapeIntersectionAnchor(sourceNode), new ShapeIntersectionAnchor(targetNode))
    {}

    public PortModel SourcePortModel { get; set; }
    public PortModel TargetPortModel { get; set; }
}