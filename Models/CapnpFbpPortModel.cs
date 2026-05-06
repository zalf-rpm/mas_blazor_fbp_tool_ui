using System;
using System.Linq;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Mas.Schema.Service;

namespace BlazorDrawFBP.Models;

public class CapnpFbpPortModel : PortModel, IAsyncDisposable
{
    public enum PortType
    {
        In,
        Out,
    }

    public enum VisibilityState
    {
        Hidden,
        Visible,
        Dashed,
    }

    public CapnpFbpPortModel(
        NodeModel parent,
        PortType thePortType,
        PortAlignment alignment = PortAlignment.Bottom,
        Point position = null,
        Size size = null
    )
        : base(parent, alignment, position, size)
    {
        ThePortType = thePortType;
        Name = ThePortType.ToString();
        LayoutAlignment = alignment;
    }

    public CapnpFbpPortModel(
        string id,
        NodeModel parent,
        PortType thePortType,
        PortAlignment alignment = PortAlignment.Bottom,
        Point position = null,
        Size size = null
    )
        : base(id, parent, alignment, position, size)
    {
        ThePortType = thePortType;
        Name = ThePortType.ToString();
        LayoutAlignment = alignment;
    }

    public PortType ThePortType { get; }
    public string Name { get; set; }

    public string ContentType { get; set; } = "?";

    public string Description { get; set; } = "";

    public bool Connected { get; set; } = false;

    public bool IsArrayPort { get; set; } = false;

    public VisibilityState Visibility { get; set; } = VisibilityState.Visible;

    public int ConnectedChannelCount => Links.OfType<RememberCapnpPortsLinkModel>().Count();

    public bool CanAcceptMoreConnections =>
        ThePortType == PortType.In || IsArrayPort || ConnectedChannelCount == 0;

    // order of the port in the list of ports with the same type
    public int OrderNo { get; set; } = 0;

    public PortAlignment LayoutAlignment { get; set; } = PortAlignment.Bottom;

    public double LayoutOffsetPx { get; set; }

    public override bool CanAttachTo(ILinkable other)
    {
        // default constraints
        if (!base.CanAttachTo(other))
            return false;

        if (other is not CapnpFbpPortModel otherPort)
            return false;

        return CanConnect(this, otherPort);
    }

    public void SyncVisibility(BaseLinkModel ignoredLink = null)
    {
        var remainingConnections = Links
            .OfType<RememberCapnpPortsLinkModel>()
            .Count(link => !ReferenceEquals(link, ignoredLink));

        Visibility = ThePortType switch
        {
            PortType.Out => IsArrayPort || remainingConnections == 0
                ? VisibilityState.Visible
                : VisibilityState.Hidden,
            PortType.In => IsArrayPort || remainingConnections == 0
                ? VisibilityState.Visible
                : VisibilityState.Dashed,
            _ => VisibilityState.Visible,
        };
    }

    public static bool CanConnect(CapnpFbpPortModel firstPort, CapnpFbpPortModel secondPort)
    {
        if (!TryResolveEndpoints(firstPort, secondPort, out var outPort, out var inPort))
            return false;

        if (!outPort.CanAcceptMoreConnections || !inPort.CanAcceptMoreConnections)
            return false;

        return !outPort.Links.OfType<RememberCapnpPortsLinkModel>().Any(link =>
            ReferenceEquals(link.InPortModel, inPort)
        );
    }

    private static bool TryResolveEndpoints(
        CapnpFbpPortModel firstPort,
        CapnpFbpPortModel secondPort,
        out CapnpFbpOutPortModel outPort,
        out CapnpFbpInPortModel inPort
    )
    {
        switch (firstPort, secondPort)
        {
            case (CapnpFbpOutPortModel sourceOut, CapnpFbpInPortModel targetIn):
                outPort = sourceOut;
                inPort = targetIn;
                return true;
            case (CapnpFbpInPortModel targetIn, CapnpFbpOutPortModel sourceOut):
                outPort = sourceOut;
                inPort = targetIn;
                return true;
            default:
                outPort = null;
                inPort = null;
                return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore() { }
}
