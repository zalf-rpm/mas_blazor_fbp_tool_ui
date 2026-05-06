using System;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;
using Capnp.Rpc;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;

namespace BlazorDrawFBP.Models;

public class RememberCapnpPortsLinkModel : LinkModel, IDisposable
{
    public RememberCapnpPortsLinkModel(CapnpFbpOutPortModel outPort, CapnpFbpInPortModel inPort)
        : base(CreatePortAnchor(outPort), CreatePortAnchor(inPort))
    {
        OutPortModel = outPort;
        InPortModel = inPort;
    }

    public CapnpFbpOutPortModel OutPortModel { get; set; }
    public CapnpFbpInPortModel InPortModel { get; set; }

    public Mas.Schema.Fbp.Channel<IP>.StatsCallback.Stats Stats { get; set; } = new();
    public Task RetrieveWriterFromChannelTask { get; set; }
    public SturdyRef WriterSturdyRef { get; private set; }
    public Channel<IP>.IWriter Writer { get; private set; }
    public Mas.Schema.Fbp.Process.IDisconnect ProcessOutDisconnect { get; private set; }
    public bool ProcessOutConnected { get; private set; }

    private static SinglePortAnchor CreatePortAnchor(CapnpFbpPortModel port) =>
        new(port)
        {
            MiddleIfNoMarker = true,
            UseShapeAndAlignment = false,
        };

    public Task EnsureWriterFromChannelAsync(CancellationToken cancelToken = default)
    {
        if (WriterSturdyRef != null)
            return Task.CompletedTask;

        if (RetrieveWriterFromChannelTask != null)
            return RetrieveWriterFromChannelTask;

        if (InPortModel.Channel == null)
            return Task.CompletedTask;

        RetrieveWriterFromChannelTask = Task.Run(
            async () =>
            {
                try
                {
                    var (writer, writerSturdyRef) =
                        await Shared.Shared.GetNewWriterFromChannel(InPortModel.Channel, cancelToken);
                    SetWriter(writer, writerSturdyRef);
                    OutPortModel.Parent?.Refresh();
                    OutPortModel.Parent?.RefreshLinks();
                }
                finally
                {
                    RetrieveWriterFromChannelTask = null;
                }
            },
            cancelToken
        );

        return RetrieveWriterFromChannelTask;
    }

    public void SetWriter(Channel<IP>.IWriter writer, SturdyRef writerSturdyRef)
    {
        if (!ReferenceEquals(Writer, writer))
            Writer?.Dispose();

        Writer = writer;
        WriterSturdyRef = writerSturdyRef;
        OutPortModel?.SyncLinkedWriterState();
    }

    public void SetProcessOutDisconnect(
        Mas.Schema.Fbp.Process.IDisconnect disconnect,
        bool connected
    )
    {
        if (!ReferenceEquals(ProcessOutDisconnect, disconnect))
            ProcessOutDisconnect?.Dispose();

        ProcessOutDisconnect = disconnect;
        ProcessOutConnected = connected;
        OutPortModel?.SyncProcessConnectionState();
    }

    public async Task<bool> DisconnectProcessOutPortAsync(
        CancellationToken cancelToken = default
    )
    {
        var disconnect = ProcessOutDisconnect;
        ProcessOutDisconnect = null;
        ProcessOutConnected = false;
        OutPortModel?.SyncProcessConnectionState();

        if (disconnect == null)
            return false;

        try
        {
            return await disconnect.Disconnect(cancelToken);
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine(
                $"Link {Id}: process out disconnect already disposed: {ex.Message}"
            );
            return false;
        }
        catch (RpcException ex)
        {
            Console.WriteLine($"Link {Id}: process out disconnect RPC failed: {ex.Message}");
            return false;
        }
        finally
        {
            disconnect.Dispose();
        }
    }

    public void ClearProcessOutDisconnect()
    {
        ProcessOutDisconnect?.Dispose();
        ProcessOutDisconnect = null;
        ProcessOutConnected = false;
        OutPortModel?.SyncProcessConnectionState();
    }

    public async Task DisconnectWriterAsync()
    {
        if (RetrieveWriterFromChannelTask != null)
        {
            await RetrieveWriterFromChannelTask.ContinueWith(t => t.Dispose());
            RetrieveWriterFromChannelTask = null;
        }

        Writer?.Dispose();
        Writer = null;
        WriterSturdyRef = null;
        OutPortModel?.SyncLinkedWriterState();
    }

    public void Dispose()
    {
        Console.WriteLine("RememberCapnpPortsLinkModel::Dispose()");
    }
}
