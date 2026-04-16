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
    public string ChannelName { get; set; } = "no-name";

    public Task Status(
        Channel<IP>.StatsCallback.Stats stats,
        CancellationToken cancellationToken_ = default
    )
    {
        // Console.WriteLine(
        //     $"T{Environment.CurrentManagedThreadId} {ChannelName} StatsCallback::Status@port {inPortModel.Name}: received status message {stats.Timestamp} Int:{stats.UpdateIntervalInMs} #bws:{stats.NoOfWaitingWriters} #q:{stats.NoOfIpsInQueue} #tot:{stats.TotalNoOfIpsReceived} #brs:{stats.NoOfWaitingReaders}"
        // );
        foreach (var link in inPortModel.Parent.Links)
        {
            if (
                link is not RememberCapnpPortsLinkModel rcplm
                || rcplm.InPortModel.Channel != inPortModel.Channel
            )
                continue;
            rcplm.Stats = stats;
            link.Refresh();
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // Console.WriteLine(
        //     $"T{Environment.CurrentManagedThreadId} StatsCallback::Dispose@port {inPortModel.Name}"
        // );
    }
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

    private StatsCallback _statsCallback;
    private Channel<IP>.StatsCallback.IUnregister _unregisterCallback;

    public bool ReceivingStats => _statsCallback != null;

    public async Task ReceiveChannelStats(uint updateIntervalInMs = 2000)
    {
        var info = await Channel.Info();
        _statsCallback.ChannelName = info.Name ?? info.Id;
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} Port {Name}: ReceiveChannelStats Channel.id: {info.Id} updateIntervalInMs: {updateIntervalInMs}"
        );
        if (_unregisterCallback != null)
            await _unregisterCallback.Unreg();
        _unregisterCallback = await Channel.RegisterStatsCallback(
            _statsCallback,
            updateIntervalInMs
        );

        // Console.WriteLine(
        //     $"T{Environment.CurrentManagedThreadId} Port {Name}: Unregister callback not null: {_unregisterCallback != null}"
        // );
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} Port {Name}: CapnpFbpPortModel::Dispose"
        );
        Task.Run(async () => await FreeRemoteChannelResources());
    }

    private async Task FreeRemoteChannelResources()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} Port {Name}: FreeRemoteChannelResources"
        );
        if (StopChannel != null && ThePortType == PortType.In)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} Port {Name}: FreeRemoteChannelResources: Stop Channel"
            );
            await StopChannel.Stop();
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} Port {Name}: FreeRemoteChannelResources: Stopped and disposing port now."
            );
            StopChannel.Dispose();
            StopChannel = null;
        }

        if (_unregisterCallback != null)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} Port {Name}: FreeRemoteChannelResources: unregistering StatsCallback"
            );
            var success = await _unregisterCallback.Unreg();
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} Port {Name}: FreeRemoteChannelResources: unregistered StatsCallback successfully? {success}."
            );
            _statsCallback.Dispose();
        }

        Channel?.Dispose();
        Channel = null;
        Reader?.Dispose();
        Reader = null;
        if (RetrieveReaderFromChannelTask != null)
            await RetrieveReaderFromChannelTask.ContinueWith(t => t.Dispose());
    }
}
