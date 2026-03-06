using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Pages;
using Capnp;
using Capnp.Rpc;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Microsoft.AspNetCore.Components;
using Exception = System.Exception;

namespace BlazorDrawFBP.Models;

public class CapnpFbpViewComponentModel : NodeModel, IDisposable // : CapnpFbpComponentModel
{
    private readonly List<Task> _iipTasks = [];
    private CancellationTokenSource _cancellationTokenSource;

    private MarkupString _viewContent;

    public CapnpFbpViewComponentModel(Point position = null)
        : base(position) { }

    public CapnpFbpViewComponentModel(string id, Point position = null)
        : base(id, position) { }

    public Editor Editor { get; set; }
    public string ComponentId { get; set; }
    public string ComponentName { get; set; }
    public string ProcessName { get; set; }

    public bool ProcessStarted { get; protected set; }
    public Task ViewMsgReceiveTask { get; set; }

    public int DisplayWidthPx { get; set; } = 100;
    public int DisplayHeightPx { get; set; } = 140;

    public bool AppendMode { get; set; } = true;

    public void ResetViewContent()
    {
        _viewContent = new MarkupString();
        Refresh();
    }

    public MarkupString ViewContent
    {
        get => _viewContent;
        set =>
            _viewContent = AppendMode
                ? new MarkupString(
                    _viewContent.Value
                        + (string.IsNullOrWhiteSpace(_viewContent.Value) ? "" : "<br>")
                        + value.Value
                )
                : value;
    }

    public async Task StartProcess(ConnectionManager conMan, bool start)
    {
        try
        {
            if (Editor.CurrentChannelStarterService == null)
            {
                return;
            }

            Console.WriteLine(
                $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: StartProcess start={start}"
            );

            if (start)
            {
                _cancellationTokenSource = new CancellationTokenSource();
                var cancelToken = _cancellationTokenSource.Token;

                Channel<IP>.IReader reader = null;

                // collect SRs from IN and OUT ports and for IIPs send it into the channel
                foreach (var pl in Links)
                {
                    if (pl is not RememberCapnpPortsLinkModel rcplm)
                    {
                        continue;
                    }

                    // deal with IN port
                    if (rcplm.InPortModel is not CapnpFbpPortModel inPort)
                    {
                        continue;
                    }

                    // the IN port (link) is not associated with a channel yet -> create channel
                    if (
                        inPort.ReaderWriterSturdyRef == null
                        && inPort.RetrieveReaderOrWriterFromChannelTask == null
                    )
                    {
                        if (inPort.Parent is not CapnpFbpViewComponentModel m)
                        {
                            continue;
                        }

                        Console.WriteLine(
                            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: the IN port (link) is not associated with a channel yet -> create channel"
                        );
                        await Shared.Shared.CreateChannel(
                            conMan,
                            Editor.CurrentChannelStarterService,
                            rcplm.OutPortModel,
                            inPort
                        );
                    }

                    if (inPort.Parent == this)
                    {
                        reader = Proxy.Share(inPort.Reader);
                    }

                    rcplm.Color = inPort.Channel != null ? "#1ac12e" : "black";

                    // deal with OUT port
                    switch (rcplm.OutPortModel)
                    {
                        case CapnpFbpPortModel outPort:
                            if (outPort.ReaderWriterSturdyRef == null)
                            {
                                (outPort.Writer, outPort.ReaderWriterSturdyRef) =
                                    await Shared.Shared.GetNewWriterFromChannel(
                                        inPort.Channel,
                                        cancelToken
                                    );
                                outPort.Parent.Refresh();
                            }

                            break;
                        case CapnpFbpIipPortModel iipPort:
                        {
                            if (iipPort.Parent is not CapnpFbpIipModel iipModel)
                            {
                                continue;
                            }

                            if (iipPort.WriterSturdyRef == null)
                            {
                                if (iipPort.RetrieveWriterFromChannelTask != null)
                                {
                                    Console.WriteLine(
                                        $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: awaiting iipPort.ChannelTask"
                                    );
                                    await iipPort.RetrieveWriterFromChannelTask;
                                }
                                else
                                {
                                    (iipPort.Writer, iipPort.WriterSturdyRef) =
                                        await Shared.Shared.GetNewWriterFromChannel(
                                            inPort.Channel,
                                            cancelToken
                                        );
                                }
                                iipPort.Parent.Refresh();
                            }

                            _iipTasks.Add(
                                Task.Run(
                                    async () =>
                                    {
                                        Console.WriteLine(
                                            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: async code for automatically writing IIP: '{iipModel.Content}' to channel"
                                        );
                                        if (iipPort.Writer == null)
                                        {
                                            Debug.Assert(
                                                iipPort.Writer != null,
                                                "Here we should already have a writer, so no need to connect."
                                            );
                                            Console.WriteLine(
                                                $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: before connecting to writer for iip iipPort.ChannelTask: {iipPort.RetrieveWriterFromChannelTask?.IsCompletedSuccessfully}"
                                            );
                                            iipPort.Writer =
                                                await conMan.Connect<Channel<IP>.IWriter>(
                                                    iipPort.WriterSturdyRef
                                                );
                                        }

                                        Console.WriteLine(
                                            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: writing IIP: '{iipModel.Content}' to channel"
                                        );
                                        await iipPort.Writer.Write(
                                            new Channel<IP>.Msg
                                            {
                                                Value = new IP { Content = new StructuredText { TheType = StructuredText.Type.json, Value = iipModel.Content } },
                                            },
                                            cancelToken
                                        );
                                        Console.WriteLine(
                                            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: wrote IIP: '{iipModel.Content}' to channel"
                                        );
                                    },
                                    cancelToken
                                )
                            );
                            break;
                        }
                    }
                }

                //run loop to receive messages on in-port
                ViewMsgReceiveTask = Task.Run(
                    async () =>
                    {
                        Console.WriteLine(
                            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: starting view's receive loop"
                        );
                        var leave = false;
                        while (!leave && reader != null)
                        {
                            try
                            {
                                Console.WriteLine(
                                    $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: reading from channel"
                                );
                                var msg = await reader.Read(cancelToken);
                                // Console.WriteLine(
                                //     $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: read msg: {msg}"
                                // );
                                switch (msg.which)
                                {
                                    case Channel<IP>.Msg.WHICH.Done:
                                        Console.WriteLine(
                                            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: received done msg"
                                        );
                                        leave = true;
                                        break;
                                    case Channel<IP>.Msg.WHICH.Value:
                                        // Console.WriteLine($"{ProcessName}: received value msg");
                                        if (msg.Value.Content is DeserializerState ds)
                                        {
                                            if (CapnpSerializable.Create<StructuredText>(ds) is { } st)
                                            {
                                                var str = st.Value;
                                                str = str.ReplaceLineEndings("<br>");
                                                var stStr =
                                                    $"<b>{Shared.Shared.FormatStructuredTextType(st.TheType)}:</b><p>{str}</p>";
                                                Console.WriteLine(
                                                    $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: read: '{str}' from channel"
                                                );
                                                ViewContent = new MarkupString(stStr);
                                            }

                                            Refresh();
                                        }

                                        break;
                                    case Channel<IP>.Msg.WHICH.NoMsg:
                                        Console.WriteLine(
                                            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: received noMsg msg"
                                        );
                                        break;
                                    case Channel<IP>.Msg.WHICH.undefined:
                                        Console.WriteLine(
                                            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: received undefined msg"
                                        );
                                        break;
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }
                            }
                            catch (TaskCanceledException tce)
                            {
                                await reader.Close();
                                leave = true;
                            }
                        }

                        reader?.Dispose();
                        Console.WriteLine(
                            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: left view's receive loop"
                        );
                    },
                    cancelToken
                );

                ProcessStarted = !ViewMsgReceiveTask.IsFaulted;
                RefreshAll();
                RefreshLinks();
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
            Console.WriteLine(
                $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: Caught exception: " + e
            );
        }
    }

    public void Dispose()
    {
        foreach (var baseLinkModel in Links)
        {
            Shared.Shared.RestoreDefaultPortVisibility(Editor.Diagram, baseLinkModel);
        }

        FreeRemoteChannelsAttachedToPorts();
        Task.Run(CancelAndDisposeViewTasks);
    }

    public void FreeRemoteChannelsAttachedToPorts()
    {
        Console.WriteLine(
            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: CapnpFbpViewComponentModel::FreeRemoteChannelsAttachedToPorts"
        );
        foreach (var port in Ports)
        {
            if (port is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public async Task CancelAndDisposeViewTasks()
    {
        Console.WriteLine(
            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: CapnpFbpViewComponentModel::CancelAndDisposeViewTasks"
        );
        //cancel task
        if (_cancellationTokenSource != null)
        {
            await _cancellationTokenSource.CancelAsync();
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        //dispose the IIP tasks
        foreach (var t in _iipTasks)
        {
            t.ContinueWith(t => t.Dispose());
        }

        _iipTasks.Clear();
        //dispose the actual view task
        ViewMsgReceiveTask?.ContinueWith(t =>
        {
            t.Dispose();
            ViewMsgReceiveTask = null;
        });
    }
}
