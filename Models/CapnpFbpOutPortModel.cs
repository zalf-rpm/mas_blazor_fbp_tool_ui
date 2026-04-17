using System;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Mas.Schema.Service;

namespace BlazorDrawFBP.Models;

public class CapnpFbpOutPortModel : CapnpFbpPortModel
{
    public CapnpFbpOutPortModel(
        NodeModel parent,
        PortAlignment alignment = PortAlignment.Bottom,
        Point position = null,
        Size size = null
    )
        : base(parent, PortType.Out, alignment, position, size) { }

    public CapnpFbpOutPortModel(
        string id,
        NodeModel parent,
        PortAlignment alignment = PortAlignment.Bottom,
        Point position = null,
        Size size = null
    )
        : base(id, parent, PortType.Out, alignment, position, size) { }

    public Task RetrieveWriterFromChannelTask { get; set; }
    public SturdyRef WriterSturdyRef { get; set; }

    public Channel<IP>.IWriter Writer { get; set; }

    protected override async ValueTask DisposeAsyncCore()
    {
        Console.WriteLine($"Port {Name}: CapnpFbpOutPortModel::DisposeAsyncCore");
        if (RetrieveWriterFromChannelTask != null)
            await RetrieveWriterFromChannelTask.ContinueWith(t => t.Dispose());
        Writer?.Dispose();
        Writer = null;
    }
}
