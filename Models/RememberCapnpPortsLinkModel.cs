using System;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Models;

public class RememberCapnpPortsLinkModel : LinkModel, IDisposable
{
    // public RememberCapnpPortsLinkModel(Anchor source, Anchor target)
    //     : base(source, target) { }

    // public RememberCapnpPortsLinkModel(string id, Anchor source, Anchor target)
    //     : base(id, source, target) { }

    // public RememberCapnpPortsLinkModel(PortModel outPort, PortModel inPort)
    //     : base(new SinglePortAnchor(outPort), new SinglePortAnchor(inPort)) { }

    public RememberCapnpPortsLinkModel(NodeModel sourceNode, NodeModel targetNode)
        : base(new ShapeIntersectionAnchor(sourceNode), new ShapeIntersectionAnchor(targetNode)) { }

    // public RememberCapnpPortsLinkModel(string id, PortModel outPort, PortModel inPort)
    //     : base(id, new SinglePortAnchor(outPort), new SinglePortAnchor(inPort)) { }

    // public RememberCapnpPortsLinkModel(string id, NodeModel sourceNode, NodeModel targetNode)
    //     : base(id, new ShapeIntersectionAnchor(sourceNode), new ShapeIntersectionAnchor(targetNode))
    // { }

    public CapnpFbpOutPortModel OutPortModel { get; set; }
    public CapnpFbpInPortModel InPortModel { get; set; }

    public Mas.Schema.Fbp.Channel<IP>.StatsCallback.Stats Stats { get; set; } = new();

    public void Dispose()
    {
        Console.WriteLine("RememberCapnpPortsLinkModel::Dispose()");
    }
}
