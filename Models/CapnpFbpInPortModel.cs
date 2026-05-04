using System;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Capnp.Rpc;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Mas.Schema.Service;

namespace BlazorDrawFBP.Models;

public class CapnpFbpInPortModel : CapnpFbpPortModel
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
    public Mas.Schema.Fbp.Process.IDisconnect ProcessDisconnect { get; private set; }

    private readonly StatsCallback _statsCallback;
    private Channel<IP>.StatsCallback.IUnregister _unregisterStatsCallback;

    public bool ReceivingStats => _statsCallback != null;

    public void SetProcessDisconnect(Mas.Schema.Fbp.Process.IDisconnect disconnect, bool connected)
    {
        if (!ReferenceEquals(ProcessDisconnect, disconnect))
            ProcessDisconnect?.Dispose();

        ProcessDisconnect = disconnect;
        Connected = connected;
    }

    public async Task<bool> DisconnectProcessAsync(CancellationToken cancelToken = default)
    {
        var disconnect = ProcessDisconnect;
        ProcessDisconnect = null;
        Connected = false;

        if (disconnect == null)
            return false;

        try
        {
            return await disconnect.Disconnect(cancelToken);
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine($"Port {Name}: process disconnect already disposed: {ex.Message}");
            return false;
        }
        catch (RpcException ex)
        {
            Console.WriteLine($"Port {Name}: process disconnect RPC failed: {ex.Message}");
            return false;
        }
        finally
        {
            disconnect.Dispose();
        }
    }

    public void ClearProcessDisconnect()
    {
        ProcessDisconnect?.Dispose();
        ProcessDisconnect = null;
        Connected = false;
    }

    public async Task DisconnectChannelAsync(bool stopChannel)
    {
        if (_unregisterStatsCallback != null)
        {
            await _unregisterStatsCallback.Unreg();
            _unregisterStatsCallback = null;
        }

        if (stopChannel && StopChannel != null)
        {
            await StopChannel.Stop();
            StopChannel.Dispose();
            StopChannel = null;
        }

        if (RetrieveReaderFromChannelTask != null)
        {
            await RetrieveReaderFromChannelTask.ContinueWith(t => t.Dispose());
            RetrieveReaderFromChannelTask = null;
        }

        Channel?.Dispose();
        Channel = null;

        Reader?.Dispose();
        Reader = null;
        ReaderSturdyRef = null;
        Connected = false;
    }

    public async Task ReceiveChannelStats(uint updateIntervalInMs = 2000)
    {
        var info = await Channel.Info();
        _statsCallback.ChannelName = info.Name ?? info.Id;
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} Port {Name}: ReceiveChannelStats Channel.id: {info.Id} updateIntervalInMs: {updateIntervalInMs}"
        );
        if (_unregisterStatsCallback != null)
            await _unregisterStatsCallback.Unreg();
        _unregisterStatsCallback = await Channel.RegisterStatsCallback(
            _statsCallback,
            updateIntervalInMs
        );

        // Console.WriteLine(
        //     $"T{Environment.CurrentManagedThreadId} Port {Name}: Unregister callback not null: {_unregisterCallback != null}"
        // );
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} Port {Name}: CapnpFbpInPortModel::DisposeAsyncCore"
        );
        // unregister from channel
        if (_unregisterStatsCallback != null)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} Port {Name}: CapnpFbpInPortModel::DisposeAsyncCore: unregistering StatsCallback"
            );
        }
        if (StopChannel != null)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} Port {Name}: CapnpFbpInPortModel::DisposeAsyncCore: Stop Channel"
            );
        }
        await DisconnectChannelAsync(stopChannel: true);
        _statsCallback.Dispose();
    }

    private class StatsCallback(CapnpFbpInPortModel inPortModel)
        : Mas.Schema.Fbp.Channel<IP>.IStatsCallback
    {
        public string ChannelName { get; set; } = "no-name";

        public Task Status(
            Channel<IP>.StatsCallback.Stats stats,
            CancellationToken cancellationToken = default
        )
        {
            // Console.WriteLine(
            //     $"T{Environment.CurrentManagedThreadId} {ChannelName} StatsCallback::Status@port {inPortModel.Name}: received status message {stats.Timestamp} Int:{stats.UpdateIntervalInMs} #bws:{stats.NoOfWaitingWriters} #q:{stats.NoOfIpsInQueue} #tot:{stats.TotalNoOfIpsReceived} #brs:{stats.NoOfWaitingReaders}"
            // );
            foreach (var link in Shared.Shared.AttachedLinks(inPortModel.Parent))
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
}
