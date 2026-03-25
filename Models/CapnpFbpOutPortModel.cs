using System;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Mas.Schema.Service;

namespace BlazorDrawFBP.Models;

public class CapnpFbpOutPortModel : CapnpFbpPortModel, IDisposable
{
    public CapnpFbpOutPortModel(
        NodeModel parent,
        PortAlignment alignment = PortAlignment.Bottom,
        Point position = null,
        Size size = null
    )
        : base(parent, PortType.Out, alignment, position, size)
    {
    }

    public CapnpFbpOutPortModel(
        string id,
        NodeModel parent,
        PortAlignment alignment = PortAlignment.Bottom,
        Point position = null,
        Size size = null
    )
        : base(id, parent, PortType.Out, alignment, position, size)
    {
    }

    public Task RetrieveWriterFromChannelTask { get; set; }
    public SturdyRef WriterSturdyRef { get; set; }

    public Channel<IP>.IWriter Writer { get; set; }

    public void Dispose()
    {
        Console.WriteLine($"Port {Name}: CapnpFbpOutPortModel::Dispose");
        FreeRemoteChannelResources();
    }

    public void FreeRemoteChannelResources()
    {
        Console.WriteLine($"Port {Name}: FreeRemoteChannelResources");
        Writer?.Dispose();
        Writer = null;
        RetrieveWriterFromChannelTask?.ContinueWith(t => t.Dispose());
    }
}
