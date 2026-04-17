using System;
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
    }

    public PortType ThePortType { get; }
    public string Name { get; set; }

    public string ContentType { get; set; } = "?";

    public string Description { get; set; } = "";

    public bool Connected { get; set; } = false;

    public VisibilityState Visibility { get; set; } = VisibilityState.Visible;

    // order of the port in the list of ports with the same alignment
    public int OrderNo { get; set; } = 0;

    public override bool CanAttachTo(ILinkable other)
    {
        // default constraints
        if (!base.CanAttachTo(other))
            return false;

        if (other is not CapnpFbpPortModel otherPort)
            return false;

        // Only link Ins with Outs
        return ThePortType != otherPort.ThePortType;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore() { }
}
