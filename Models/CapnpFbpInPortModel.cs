using System;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Mas.Schema.Service;

namespace BlazorDrawFBP.Models;

public class StatsCallback(CapnpFbpInPortModel inPortModel)
    : Mas.Schema.Fbp.Channel<IP>.IStatsCallback
{
    public Task Status(
        Channel<IP>.StatsCallback.Stats stats,
        CancellationToken cancellationToken_ = default
    )
    {
        foreach (var link in inPortModel.Links)
        {
            if (link is RememberCapnpPortsLinkModel rcplm)
            {
                rcplm.Stats = stats;
            }
        }
        return Task.CompletedTask;
    }

    public void Dispose() { }
}

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
        _statsCallback = new StatsCallback(this);
    }

    // public CapnpFbpInPortModel(
    //     string id,
    //     NodeModel parent,
    //     PortAlignment alignment = PortAlignment.Bottom,
    //     Point position = null,
    //     Size size = null
    // )
    //     : base(id, parent, PortType.In, alignment, position, size) { }

    public Task RetrieveReaderFromChannelTask { get; set; }
    public SturdyRef ReaderSturdyRef { get; set; }

    public Channel<IP>.IReader Reader { get; set; }

    public IChannel<IP> Channel { get; set; }
    public IStoppable StopChannel { get; set; }

    private readonly StatsCallback _statsCallback;
    private Channel<IP>.StatsCallback.IUnregister _unregisterCallback;

    public bool ReceivingStats => _statsCallback != null;

    public async Task ReceiveChannelStats(uint updateIntervalInMs = 2000)
    {
        if (_unregisterCallback != null)
            await _unregisterCallback.Unreg();
        _unregisterCallback = await Channel.RegisterStatsCallback(
            _statsCallback,
            updateIntervalInMs
        );
    }

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
