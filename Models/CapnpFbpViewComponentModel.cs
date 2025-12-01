using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorDrawFBP.Pages;
using Capnp;
using Mas.Infrastructure.Common;
using Mas.Schema.Fbp;
using Microsoft.AspNetCore.Components;
using OneOf.Types;

namespace BlazorDrawFBP.Models;

using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

public class CapnpFbpViewComponentModel : NodeModel, IDisposable// : CapnpFbpComponentModel
{
    public CapnpFbpViewComponentModel(Point position = null) : base(position) {}

    public CapnpFbpViewComponentModel(string id, Point position = null) : base(id, position) {}

    public Editor Editor { get; set; }
    public string ComponentId { get; set; }
    public string ComponentName { get; set; }
    public string ProcessName { get; set; }

    public bool ProcessStarted { get; protected set; }
    public Task ViewMsgReceiveTask { get; set; }
    private CancellationTokenSource _cancellationTokenSource = null;

    private List<Task> _iipTasks = [];

    public int DisplayWidthPx { get; set; } = 100;
    public int DisplayHeightPx { get; set; } = 100;

    public bool AppendMode { get; set; } = false;

    private MarkupString _viewContent;
    public MarkupString ViewContent {
        get => _viewContent;
        set => _viewContent = AppendMode
            ? new MarkupString(_viewContent.Value + (string.IsNullOrWhiteSpace(_viewContent.Value) ? "" : "<br>") +
                               value.Value)
            : value;
    }

    public async Task StartProcess(ConnectionManager conMan, bool start)
    {
        try
        {
            if (Editor.CurrentChannelStarterService == null) return;

            Console.WriteLine($"{ProcessName}: StartProcess start={start}");

            if (start)
            {
                _cancellationTokenSource = new CancellationTokenSource();
                var cancelToken = _cancellationTokenSource.Token;
                
                Channel<IP>.IReader reader = null;

                // collect SRs from IN and OUT ports and for IIPs send it into the channel
                foreach (var pl in Links)
                {
                    if (pl is not RememberCapnpPortsLinkModel rcplm) continue;

                    // deal with IN port
                    if (rcplm.InPortModel is not CapnpFbpPortModel inPort) continue;
                    // the IN port (link) is not associated with a channel yet -> create channel
                    if (inPort.ReaderWriterSturdyRef == null && inPort.ChannelTask == null)
                    {
                        if (inPort.Parent is not CapnpFbpViewComponentModel m) return;
                        Console.WriteLine($"{ProcessName}: the IN port (link) is not associated with a channel yet -> create channel");
                        await Shared.Shared.CreateChannel(conMan, Editor.CurrentChannelStarterService, rcplm.OutPortModel, inPort);
                    }
                    //if (inPort.Parent == this) await CollectPortSrs(inPort);
                    if (inPort.Parent == this) reader = Capnp.Rpc.Proxy.Share(inPort.Reader);

                    // deal with OUT port
                    switch (rcplm.OutPortModel)
                    {
                        case CapnpFbpPortModel outPort:
                            //if (outPort.Parent == this) await CollectPortSrs(outPort);
                            break;
                        case CapnpFbpIipPortModel iipPort:
                        {
                            if (iipPort.Parent is not CapnpFbpIipModel iipModel) continue;
                            if (iipPort.WriterSturdyRef == null && iipPort.ChannelTask != null)
                            {
                                Console.WriteLine($"{ProcessName}: awaiting iipPort.ChannelTask");
                                await iipPort.ChannelTask;
                            }

                            _iipTasks.Add(Task.Run(async () =>
                            {
                                var content = iipModel.Content;
                                Console.WriteLine($"{ProcessName}: async code for sending IIP: '{content}'");
                                if (iipPort.Writer == null)
                                {
                                    Console.WriteLine(
                                        $"{ProcessName}: before connecting to writer for iip iipPort.ChannelTask: {iipPort.ChannelTask?.IsCompletedSuccessfully}");
                                    iipPort.Writer =
                                        await conMan.Connect<Mas.Schema.Fbp.Channel<Mas.Schema.Fbp.IP>.IWriter>(
                                            iipPort.WriterSturdyRef);
                                }
                                await iipPort.Writer.Write(new Channel<IP>.Msg { Value = new IP { Content = content } },
                                    cancelToken);
                                Console.WriteLine($"{ProcessName}: sent IIP to writer");
                            }, cancelToken));
                            break;
                        }
                    }
                }

                //run loop to receive messages on in-port
                ViewMsgReceiveTask = Task.Run(async () =>
                {
                    Console.WriteLine($"{ProcessName}: starting view's receive loop");
                    var leave = false;
                    while (!leave && reader != null)
                    {
                        //Console.WriteLine($"{ProcessName}: trying to read msg");
                        var msg = await reader.Read(cancelToken);
                        //Console.WriteLine($"{ProcessName}: read msg: {msg}");
                        switch (msg.which)
                        {
                            case Channel<IP>.Msg.WHICH.Done:
                                Console.WriteLine($"{ProcessName}: received done msg");
                                leave = true;
                                break;
                            case Channel<IP>.Msg.WHICH.Value:
                                //Console.WriteLine($"{ProcessName}: received value msg");
                                if (msg.Value.Content is DeserializerState ds)
                                {
                                    if (CapnpSerializable.Create<string>(ds) is { } str)
                                    {
                                        //Console.WriteLine($"{ProcessName}: received msg: '{str}'");
                                        ViewContent = new MarkupString(str);
                                    }
                                    Refresh();
                                }

                                break;
                            case Channel<IP>.Msg.WHICH.NoMsg:
                                Console.WriteLine($"{ProcessName}: received noMsg msg");
                                break;
                            case Channel<IP>.Msg.WHICH.undefined:
                                Console.WriteLine($"{ProcessName}: received undefined msg");
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                    //Console.WriteLine($"{ProcessName}: left view's receive loop");
                }, cancelToken);

                ProcessStarted = !ViewMsgReceiveTask.IsFaulted;
            }
            else // stop
            {
                await CancelAndDisposeViewTasks();
                //actually the channel connected to the port can remain until the component gets deleted
                //FreeRemoteChannelsAttachedToPorts();
                ProcessStarted = false;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"{ProcessName}: Caught exception: " + e);
        }
    }

    public void FreeRemoteChannelsAttachedToPorts()
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpViewComponentModel::FreeRemoteChannelsAttachedToPorts");
        foreach (var port in Ports)
        {
            if (port is IDisposable disposable) disposable.Dispose();
        }
    }

    public async Task CancelAndDisposeViewTasks()
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpViewComponentModel::CancelAndDisposeViewTasks");
        //cancel task
        if (_cancellationTokenSource != null) await _cancellationTokenSource.CancelAsync();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        //dispose the IIP tasks
        foreach (var t in _iipTasks) t.ContinueWith(t => t.Dispose());
        _iipTasks.Clear();
        //dispose the actual view task
        ViewMsgReceiveTask.ContinueWith(t =>
        {
            t.Dispose();
            ViewMsgReceiveTask = null;
        });
    }

    public void Dispose()
    {
        FreeRemoteChannelsAttachedToPorts();
        Task.Run(CancelAndDisposeViewTasks);
    }
}