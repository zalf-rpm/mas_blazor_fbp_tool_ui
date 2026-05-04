using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Pages;
using Capnp;
using Capnp.Rpc;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Microsoft.AspNetCore.Components;
using Exception = System.Exception;

namespace BlazorDrawFBP.Models;

public class CapnpFbpViewComponentModel : NodeModel, IAsyncDisposable
{
    private CancellationTokenSource _cancellationTokenSource;
    private MarkupString _viewContent;

    public CapnpFbpViewComponentModel(Point position = null)
        : base(position) { }

    public CapnpFbpViewComponentModel(string id, Point position = null)
        : base(id, position) { }

    // public BlazorDispatcher Dispatcher { get; set; }

    public Editor Editor { get; set; }
    public string ComponentId { get; set; }
    public string ComponentName { get; set; }
    public string ProcessName { get; set; }

    public ComponentLifecycleState LifecycleState { get; private set; } =
        ComponentLifecycleState.Stopped;
    public string LifecycleError { get; private set; }
    public bool CanStart =>
        LifecycleState is ComponentLifecycleState.Stopped or ComponentLifecycleState.Faulted;
    public bool CanStop => LifecycleState == ComponentLifecycleState.Running;
    public bool IsLifecycleBusy =>
        LifecycleState is ComponentLifecycleState.Starting or ComponentLifecycleState.Stopping;
    public string LifecycleLabel => LifecycleState switch
    {
        ComponentLifecycleState.Stopped => "Stopped",
        ComponentLifecycleState.Starting => "Starting",
        ComponentLifecycleState.Running => "Running",
        ComponentLifecycleState.Stopping => "Stopping",
        ComponentLifecycleState.Faulted => "Error",
        _ => "Unknown",
    };

    public bool ProcessStarted { get; protected set; }
    private Task ViewMsgReceiveTask { get; set; }

    public int DisplayWidthPx { get; set; } = 100;
    public int DisplayHeightPx { get; set; } = 132;

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

    public async Task StartProcess(ConnectionManager conMan)
    {
        if (Editor.CurrentChannelStarterService == null)
        {
            SetLifecycleFault(
                new InvalidOperationException("No channel service connected."),
                refresh: true
            );
            return;
        }

        if (!CanStart)
            return;

        SetLifecycleState(ComponentLifecycleState.Starting, refresh: true);
        CancellationToken cancelToken = default;
        try
        {
            Console.WriteLine(
                $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: StartProcess"
            );

            await CancelAndDisposeViewTasks();

            _cancellationTokenSource = new CancellationTokenSource();
            cancelToken = _cancellationTokenSource.Token;

            Channel<IP>.IReader reader = null;

            // collect SRs from IN and OUT ports and for IIPs send it into the channel
            foreach (var pl in Shared.Shared.AttachedLinks(this))
            {
                if (
                    pl
                    is not RememberCapnpPortsLinkModel
                    {
                        InPortModel: { } inPort,
                        OutPortModel: { } outPort
                    } rcplm
                )
                {
                    continue;
                }

                // deal with IN port
                // the IN port (link) is not associated with a channel yet -> create channel
                if (inPort.ReaderSturdyRef == null && inPort.RetrieveReaderFromChannelTask == null)
                {
                    if (inPort.Parent is not CapnpFbpViewComponentModel m)
                        continue;

                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: the IN port (link) is not associated with a channel yet -> create channel"
                    );
                    await Shared.Shared.CreateChannel(
                        conMan,
                        Editor.CurrentChannelStarterService,
                        outPort,
                        inPort
                    );
                }

                if (inPort.Parent == this)
                {
                    reader = Proxy.Share(inPort.Reader);
                }

                CapnpFbpPortColors.ApplyLinkColor(rcplm);

                // deal with OUT port
                if (outPort.RetrieveWriterFromChannelTask != null)
                {
                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: awaiting out port '{outPort.Name}' ChannelTask"
                    );
                    await outPort.RetrieveWriterFromChannelTask;
                }
                else
                {
                    if (outPort.WriterSturdyRef == null)
                    {
                        Console.WriteLine(
                            $"T{Environment.CurrentManagedThreadId} {ProcessName}: getting new writer for out port '{outPort.Name}' from channel"
                        );
                        (outPort.Writer, outPort.WriterSturdyRef) =
                            await Shared.Shared.GetNewWriterFromChannel(
                                inPort.Channel,
                                cancelToken
                            );
                        outPort.Parent.Refresh();
                    }
                }
                outPort.Parent.Refresh();
                outPort.Parent.RefreshLinks();
            }

            if (reader == null)
            {
                SetLifecycleState(ComponentLifecycleState.Stopped, refresh: true);
                return;
            }

            //run loop to receive messages on in-port
            ViewMsgReceiveTask = Task.Run(
                async () =>
                {
                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: starting view's receive loop"
                    );
                    var leave = false;
                    while (!leave && reader != null)
                    {
                        try
                        {
                            Console.WriteLine(
                                $"T{Environment.CurrentManagedThreadId} {ProcessName}: reading from channel"
                            );
                            var msg = await reader.Read(cancelToken);
                            // Console.WriteLine(
                            //     $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: read msg: {msg}"
                            // );
                            switch (msg.which)
                            {
                                case Channel<IP>.Msg.WHICH.Done:
                                    Console.WriteLine(
                                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: received done msg"
                                    );
                                    leave = true;
                                    break;
                                case Channel<IP>.Msg.WHICH.Value:
                                    // Console.WriteLine($"T{Environment.CurrentManagedThreadId} {ProcessName}: received value msg");
                                    if (msg.Value.Content is DeserializerState ds)
                                    {
                                        try
                                        {
                                            if (
                                                CapnpSerializable.Create<StructuredText>(ds) is
                                                { } st
                                            )
                                            {
                                                var str = st.Value;
                                                str = str.ReplaceLineEndings("<br>");
                                                var stStr =
                                                    $"<b>{Shared.Shared.FormatStructuredTextType(st.TheType)}:</b><p>{str}</p>";
                                                Console.WriteLine(
                                                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: read: '{str}' from channel"
                                                );
                                                ViewContent = new MarkupString(stStr);
                                            }
                                        }
                                        catch (DeserializationException)
                                        {
                                            Console.WriteLine(
                                                $"T{Environment.CurrentManagedThreadId} {ProcessName}: Message content was no StructuredText."
                                            );
                                            try
                                            {
                                                if (CapnpSerializable.Create<string>(ds) is { } str)
                                                {
                                                    str = str.ReplaceLineEndings("<br>");
                                                    Console.WriteLine(
                                                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: read: '{str}' from channel"
                                                    );
                                                    ViewContent = new MarkupString(str);
                                                }
                                            }
                                            catch (DeserializationException)
                                            {
                                                Console.WriteLine(
                                                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: Message content was no string."
                                                );
                                            }
                                        }

                                        Refresh();
                                    }

                                    break;
                                case Channel<IP>.Msg.WHICH.NoMsg:
                                    Console.WriteLine(
                                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: received noMsg msg"
                                    );
                                    break;
                                case Channel<IP>.Msg.WHICH.undefined:
                                    Console.WriteLine(
                                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: received undefined msg"
                                    );
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            await reader.Close();
                            leave = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"T{Environment.CurrentManagedThreadId} {ProcessName}: view receive loop faulted: {ex}"
                            );
                            SetLifecycleFault(ex, refresh: true);
                            leave = true;
                        }
                    }

                    reader?.Dispose();
                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: left view's receive loop"
                    );
                    if (LifecycleState != ComponentLifecycleState.Faulted)
                        SetLifecycleState(ComponentLifecycleState.Stopped, refresh: true);
                },
                cancelToken
            );

            SetLifecycleState(ComponentLifecycleState.Running, refresh: true);
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
            SetLifecycleState(ComponentLifecycleState.Stopped, refresh: true);
        }
        catch (Exception e)
        {
            await CancelAndDisposeViewTasks();
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpViewComponentModel::StartProcess: Caught exception: "
                    + e
            );
            SetLifecycleFault(e, refresh: true);
        }
    }

    public async Task StopProcess(ConnectionManager conMan)
    {
        Console.WriteLine($"T{Environment.CurrentManagedThreadId} {ProcessName}: stop process");
        if (IsLifecycleBusy || !CanStop)
            return;

        SetLifecycleState(ComponentLifecycleState.Stopping, refresh: true);
        try
        {
            await ResetExecution();
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpViewComponentModel::StartProcess: Caught exception: "
                    + e
            );
            SetLifecycleFault(e, refresh: true);
        }
    }

    public async Task ResetExecution()
    {
        await CancelAndDisposeViewTasks();
        SetLifecycleState(ComponentLifecycleState.Stopped, refresh: true);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        Shared.Shared.RestoreDefaultPortVisibilityOfAttachedComponent(this, Editor.Diagram);
        await FreeRemoteChannelsAttachedToPorts();
        await ResetExecution();
    }

    private async Task FreeRemoteChannelsAttachedToPorts()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpViewComponentModel::FreeRemoteChannelsAttachedToPorts"
        );
        foreach (var port in Ports)
        {
            if (port is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
        }
    }

    private async Task CancelAndDisposeViewTasks()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpViewComponentModel::CancelAndDisposeViewTasks"
        );
        //cancel task
        if (_cancellationTokenSource != null)
            await _cancellationTokenSource.CancelAsync();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        // dispose the actual view task
        if (ViewMsgReceiveTask != null)
            await ViewMsgReceiveTask.ContinueWith(t =>
            {
                t.Dispose();
                ViewMsgReceiveTask = null;
            });
    }

    private void SetLifecycleState(
        ComponentLifecycleState state,
        string error = null,
        bool refresh = false
    )
    {
        LifecycleState = state;
        ProcessStarted = state == ComponentLifecycleState.Running;
        LifecycleError = state == ComponentLifecycleState.Faulted ? error ?? LifecycleError : null;

        if (refresh)
        {
            RefreshAll();
            RefreshLinks();
        }
    }

    private void SetLifecycleFault(Exception exception, bool refresh = false)
    {
        Console.Error.WriteLine(exception);
        SetLifecycleState(ComponentLifecycleState.Faulted, exception.Message, refresh);
    }
}
