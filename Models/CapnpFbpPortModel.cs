using System;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Mas.Schema.Fbp;
using Mas.Schema.Service;

namespace BlazorDrawFBP.Models;

public class CapnpFbpPortModel : PortModel, IDisposable
{
    public enum PortType
    {
        In,
        Out
    }

    public enum VisibilityState
    {
        Hidden,
        Visible,
        Dashed
    }

    public CapnpFbpPortModel(NodeModel parent, PortType thePortType, PortAlignment alignment = PortAlignment.Bottom,
        Point position = null, Size size = null) : base(parent, alignment, position, size)
    {
        ThePortType = thePortType;
        Name = ThePortType.ToString();
    }

    public CapnpFbpPortModel(string id, NodeModel parent, PortType thePortType,
        PortAlignment alignment = PortAlignment.Bottom,
        Point position = null, Size size = null) : base(id, parent, alignment, position, size)
    {
        ThePortType = thePortType;
        Name = ThePortType.ToString();
    }

    public PortType ThePortType { get; }
    public string Name { get; set; }

    public Task ChannelTask { get; set; }
    public string ReaderWriterSturdyRef { get; set; }

    public IChannel<IP> Channel { get; set; }
    public IStoppable StopChannel { get; set; }

    public VisibilityState Visibility { get; set; } = VisibilityState.Visible;

    // order of the port in the list of ports with the same alignment
    public int OrderNo { get; set; } = 0;

    public void Dispose()
    {
        Console.WriteLine($"Port {Name}: Disposing");
        Channel?.Dispose();
        if (StopChannel != null && ThePortType == PortType.In)
            Task.Run(async () => await StopChannel.Stop()).ContinueWith(t => StopChannel.Dispose());
        //ChannelTask?.Dispose();
    }

    public override bool CanAttachTo(ILinkable other)
    {
        // default constraints
        if (!base.CanAttachTo(other)) return false;

        if (other is not CapnpFbpPortModel otherPort) return false;

        // Only link Ins with Outs
        return ThePortType != otherPort.ThePortType;
    }
}