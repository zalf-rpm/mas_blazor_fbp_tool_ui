using System;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Mas.Schema.Service;

namespace BlazorDrawFBP.Models;

public class CapnpFbpInPortModel : CapnpFbpPortModel, IDisposable
{
    public CapnpFbpInPortModel(
        NodeModel parent,
        PortAlignment alignment = PortAlignment.Bottom,
        Point position = null,
        Size size = null
    )
        : base(parent, PortType.In, alignment, position, size)
    {
    }

    public CapnpFbpInPortModel(
        string id,
        NodeModel parent,
        PortAlignment alignment = PortAlignment.Bottom,
        Point position = null,
        Size size = null
    )
        : base(id, parent, PortType.In, alignment, position, size)
    {
    }

    public Task RetrieveReaderFromChannelTask { get; set; }
    public SturdyRef ReaderSturdyRef { get; set; }

    public Channel<IP>.IReader Reader { get; set; }

    public IChannel<IP> Channel { get; set; }
    public IStoppable StopChannel { get; set; }

    public void Dispose()
    {
        Console.WriteLine($"Port {Name}: CapnpFbpPortModel::Dispose");
        FreeRemoteChannelResources();
    }

    public void FreeRemoteChannelResources()
    {
        Console.WriteLine($"Port {Name}: FreeRemoteChannelResources");
        if (StopChannel != null && ThePortType == PortType.In)
        {
            Console.WriteLine($"Port {Name}: FreeRemoteChannelResources: Stop Channel");
            Task.Run(async () => await StopChannel.Stop())
                .ContinueWith(t =>
                {
                    Console.WriteLine(
                        $"Port {Name}: FreeRemoteChannelResources: Stopped and disposing port now."
                    );
                    StopChannel.Dispose();
                    StopChannel = null;
                });
        }

        Channel?.Dispose();
        Channel = null;
        Reader?.Dispose();
        Reader = null;
        RetrieveReaderFromChannelTask?.ContinueWith(t => t.Dispose());
    }
}
