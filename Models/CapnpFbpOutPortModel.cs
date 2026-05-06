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

    public void SyncLinkedWriterState()
    {
        var linkedWriters = Links
            .OfType<RememberCapnpPortsLinkModel>()
            .Where(link => ReferenceEquals(link.OutPortModel, this))
            .ToList();

        if (linkedWriters.Count == 0)
        {
            if (Parent != null)
            {
                Writer = null;
                WriterSturdyRef = null;
                Connected = false;
            }

            return;
        }

        Writer = linkedWriters.Select(link => link.Writer).FirstOrDefault(writer => writer != null);
        WriterSturdyRef = linkedWriters
            .Select(link => link.WriterSturdyRef)
            .FirstOrDefault(sturdyRef => sturdyRef != null);
        SyncProcessConnectionState();
    }

    public void SyncProcessConnectionState()
    {
        Connected = Links
            .OfType<RememberCapnpPortsLinkModel>()
            .Where(link => ReferenceEquals(link.OutPortModel, this))
            .Any(link => link.ProcessOutConnected);
    }

    public async Task DisconnectWriterAsync()
    {
        foreach (
            var link in Links
                .OfType<RememberCapnpPortsLinkModel>()
                .Where(link => ReferenceEquals(link.OutPortModel, this))
                .ToList()
        )
        {
            await link.DisconnectWriterAsync();
        }

        if (RetrieveWriterFromChannelTask != null)
        {
            await RetrieveWriterFromChannelTask.ContinueWith(t => t.Dispose());
            RetrieveWriterFromChannelTask = null;
        }

        Writer?.Dispose();
        Writer = null;
        WriterSturdyRef = null;
        SyncProcessConnectionState();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        Console.WriteLine($"Port {Name}: CapnpFbpOutPortModel::DisposeAsyncCore");
        await DisconnectWriterAsync();
    }
}
