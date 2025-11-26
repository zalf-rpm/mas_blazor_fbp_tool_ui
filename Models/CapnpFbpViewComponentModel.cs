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

public class CapnpFbpViewComponentModel : CapnpFbpComponentModel
{
    public CapnpFbpViewComponentModel(Point position = null) : base(position) {}

    public CapnpFbpViewComponentModel(string id, Point position = null) : base(id, position) {}

    public Task ViewMsgReceiveTask { get; set; }
    private CancellationTokenSource _viewTaskCts = null;

    private List<Task> _iipTasks = [];
    private List<CancellationTokenSource> _iipCtss = [];

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

    public override async Task StartProcess(ConnectionManager conMan, bool start)
    {
        try
        {
            if (Editor.CurrentChannelStarterService == null) return;

            Console.WriteLine($"{ProcessName}: StartProcess start={start}");

            if (start)
            {
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

                            _iipCtss.Add(new CancellationTokenSource());
                            var iipt = _iipCtss.Last().Token;
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
                                    iipt);
                                Console.WriteLine($"{ProcessName}: sent IIP to writer");
                            }, iipt));
                            break;
                        }
                    }
                }

                _viewTaskCts = new CancellationTokenSource();
                var viewTaskCtsToken = _viewTaskCts.Token;
                ViewMsgReceiveTask = Task.Run(async () =>
                {
                    Console.WriteLine($"{ProcessName}: starting view's receive loop");
                    var leave = false;
                    while (!leave && reader != null)
                    {
                        //Console.WriteLine($"{ProcessName}: trying to read msg");
                        var msg = await reader.Read(viewTaskCtsToken);
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
                                    var str = CapnpSerializable.Create<string>(ds);
                                    if (str != null)
                                    {
                                        //Console.WriteLine($"{ProcessName}: received msg: '{str}'");
                                        ViewContent = new MarkupString(str);
                                        //Console.WriteLine(AppendMode);
                                        //Console.WriteLine(ViewContent.Value);
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
                }, viewTaskCtsToken);

                ProcessStarted = !ViewMsgReceiveTask.IsFaulted;
            }
            else // stop
            {
                foreach (var cts in _iipCtss)
                {
                    await cts.CancelAsync();
                    cts.Dispose();
                }
                foreach (var t in _iipTasks) t.Dispose();

                await FreeRemoteComponentResources();
                // await _viewTaskCts.CancelAsync();
                // _viewTaskCts.Dispose();
                // ViewMsgReceiveTask.ContinueWith(t => ViewMsgReceiveTask.Dispose());
                // //ViewMsgReceiveTask.Dispose();
                ProcessStarted = false;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"{ProcessName}: Caught exception: " + e);
        }
    }

    public async Task FreeRemoteComponentResources()
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpViewComponentModel::FreeComponentResources");
        foreach (var port in Ports)
        {
            //will also free channels
            if (port is IDisposable disposable) disposable.Dispose();
        }
        await _viewTaskCts.CancelAsync();
        _viewTaskCts.Dispose();
        ViewMsgReceiveTask.ContinueWith(t => ViewMsgReceiveTask.Dispose());
    }

    public new void Dispose()
    {
        base.Dispose();
        Task.Run(FreeRemoteComponentResources);
    }
}