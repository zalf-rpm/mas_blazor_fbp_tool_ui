using System;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Models;

public class CapnpFbpIipPortModel : PortModel, IDisposable
{
    public CapnpFbpIipPortModel(NodeModel parent, PortAlignment alignment = PortAlignment.Bottom,
        Point position = null, Size size = null) : base(parent, alignment, position, size)
    {
    }

    public CapnpFbpIipPortModel(string id, NodeModel parent, PortAlignment alignment = PortAlignment.Bottom,
        Point position = null, Size size = null) : base(id, parent, alignment, position, size)
    {
    }

    public Task ChannelTask { get; set; }
    public string WriterSturdyRef { get; set; }

    public Channel<IP>.IWriter Writer { get; set; }

    public void Dispose()
    {
        Console.WriteLine("IIP: Disposing");
        //ChannelTask?.Dispose();
        Writer?.Dispose();
    }

    public override bool CanAttachTo(ILinkable other)
    {
        // default constraints
        if (!base.CanAttachTo(other)) return false;

        if (other is not CapnpFbpPortModel otherPort) return false;

        // Only connect IIP to In ports
        return otherPort.ThePortType == CapnpFbpPortModel.PortType.In;
    }
}