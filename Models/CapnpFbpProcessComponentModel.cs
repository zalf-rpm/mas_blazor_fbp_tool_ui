using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Pages;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Newtonsoft.Json.Linq;
using ProcessSchema = Mas.Schema.Fbp.Process;
using RpcException = Capnp.Rpc.RpcException;

namespace BlazorDrawFBP.Models;

public class CapnpFbpProcessComponentModel : CapnpFbpComponentModel
{
    public CapnpFbpProcessComponentModel(Point position = null)
        : base(position) { }

    public CapnpFbpProcessComponentModel(string id, Point position = null)
        : base(id, position) { }

    private CancellationTokenSource _cancellationTokenSource;
    private ProcessStateTransition _processStateTransitionCallback;
    private Task _processRunTask;
    public IProcess Process { get; set; }

    public ProcessSchema.IFactory ProcessFactory { get; set; }

    public override bool RemoteProcessAttached() => Process != null;
    public override bool CanEditCommandLine() => ProcessFactory != null || Process != null;

    public override async Task StartProcess(ConnectionManager conMan)
    {
        if (
            Editor.CurrentChannelStarterService == null
            || ProcessFactory == null
            || !CanStart
        )
        {
            return;
        }

        var shouldStopExistingRuntime =
            Process != null && LifecycleState != ComponentLifecycleState.Stopped;
        SetLifecycleState(ComponentLifecycleState.Starting, refresh: true);
        CancellationToken cancelToken = default;
        try
        {
            Console.WriteLine($"T{Environment.CurrentManagedThreadId} {ProcessName}: StartProcess");

            await ResetRemoteRuntimeAsync(shouldStopExistingRuntime);
            _cancellationTokenSource = new CancellationTokenSource();
            cancelToken = _cancellationTokenSource.Token;

            //get a fresh Process
            if (Process == null)
            {
                Process = await ProcessFactory.Create(cancelToken);
                if (Process == null)
                {
                    return;
                }

                if (_processStateTransitionCallback == null)
                {
                    // register a state transition callback
                    // to switch the state display
                    _processStateTransitionCallback = new ProcessStateTransition(
                        (old, @new) =>
                        {
                            ApplyProcessState(@new, refresh: true);
                        }
                    );
                    await Process.State(_processStateTransitionCallback, cancelToken);
                }
            }

            var configInPortConnected = false;
            // collect SRs from IN and OUT ports and for IIPs send it into the channel
            foreach (var pl in Shared.Shared.AttachedLinks(this))
            {
                if (
                    pl
                    is not RememberCapnpPortsLinkModel
                    {
                        InPortModel: CapnpFbpInPortModel inPort,
                        OutPortModel: CapnpFbpOutPortModel outPort
                    } rcplm
                )
                {
                    continue;
                }

                // deal with IN port
                // the IN port (link) is not associated with a channel yet -> create channel
                if (inPort.ReaderSturdyRef == null && inPort.RetrieveReaderFromChannelTask == null)
                {
                    // if (inPort.Parent is not CapnpFbpComponentModel &&
                    //     inPort.Parent is not CapnpFbpViewComponentModel) {
                    //     continue;
                    // }

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

                // if this is our IN port, set it at the remote process
                if (inPort.Parent == this)
                {
                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: setting in port '{inPort.Name}' at remote process"
                    );
                    inPort.Connected = await Process.ConnectInPort(
                        inPort.Name,
                        inPort.ReaderSturdyRef,
                        cancelToken
                    );
                }

                if (inPort.Name == "config")
                {
                    configInPortConnected = true;
                }

                CapnpFbpPortColors.ApplyLinkColor(rcplm);

                // deal with OUT port
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: dealing with out port '{outPort.Name}'\noutPort.RetrieveReaderOrWriterFromChannelTask: {outPort.RetrieveWriterFromChannelTask}\noutPort.ReaderWriterSturdyRef: {outPort.WriterSturdyRef} inPort.Channel: {inPort.Channel}"
                );
                // the task for setting the writer was not yet finished
                // wait for the task so the writer will be set
                if (outPort.RetrieveWriterFromChannelTask != null)
                {
                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: awaiting out port '{outPort.Name}' ChannelTask"
                    );
                    await outPort.RetrieveWriterFromChannelTask;
                }
                else
                {
                    // there is no task anymore, but also no sturdy ref available,
                    // so get a new writer from the channel
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
                    }
                }

                outPort.Parent.Refresh();
                outPort.Parent.RefreshLinks();

                if (outPort.Parent != this)
                    continue;
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: setting out port '{outPort.Name}' at remote process"
                );
                outPort.Connected = await Process.ConnectOutPort(
                    outPort.Name,
                    outPort.WriterSturdyRef,
                    cancelToken
                );
            }

            //there is no config port connected, so we setup up a config channel and send the process config on the fly
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: configInPort connected: {configInPortConnected} ConfigString: {ConfigString}"
            );
            if (!configInPortConnected && !string.IsNullOrWhiteSpace(ConfigString))
            {
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: sending config on the fly"
                );

                var model = JObject.Parse(ConfigString);
                foreach (var kv in model)
                {
                    var val = kv.Value.Type switch
                    {
                        JTokenType.String => new Value { T = kv.Value.Value<string>() },
                        JTokenType.Integer => new Value { I64 = kv.Value.Value<long>() },
                        JTokenType.Float => new Value { F64 = kv.Value.Value<double>() },
                        JTokenType.Boolean => new Value { B = kv.Value.Value<bool>() },
                    };
                    await Process.SetConfigEntry(
                        new ProcessSchema.ConfigEntry { Name = kv.Key, Val = val },
                        cancelToken
                    );
                }

                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: set full config"
                );
            }

            var processRunTask = Process.Start(cancelToken);
            _processRunTask = processRunTask;
            _ = ObserveProcessRunAsync(processRunTask, _cancellationTokenSource);
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: Process start launched"
            );
            RefreshAll();
            RefreshLinks();
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
            SetLifecycleState(ComponentLifecycleState.Stopped, refresh: true);
        }
        catch (Exception e)
        {
            await ResetRemoteRuntimeAsync(stopRemoteProcess: true);
            SetLifecycleFault(e, refresh: true);
        }
    }

    public override async Task StopProcess(ConnectionManager conMan)
    {
        if (IsLifecycleBusy || !CanStop)
            return;

        SetLifecycleState(ComponentLifecycleState.Stopping, refresh: true);
        try
        {
            await TryStopRemoteProcessAsync(ProcessSchema.StopMode.soft);
        }
        catch (Exception e)
        {
            SetLifecycleFault(e, refresh: true);
        }
    }

    public override async Task ResetExecution()
    {
        var shouldStopExistingRuntime =
            Process != null && LifecycleState != ComponentLifecycleState.Stopped;
        await ResetRemoteRuntimeAsync(shouldStopExistingRuntime);
        SetLifecycleState(ComponentLifecycleState.Stopped, refresh: true);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::DisposeAsyncCore"
        );
        await base.DisposeAsyncCore();
        await CancelAndDisposeRemoteComponent();
        ProcessFactory?.Dispose();
    }

    private async Task CancelAndDisposeRemoteComponent()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::CancelAndDisposeRemoteComponent"
        );
        await ResetExecution();
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::CancelAndDisposeRemoteComponent stopped runnable/process (ProcessStarted: {ProcessStarted})"
        );
    }

    private async Task ResetRemoteRuntimeAsync(bool stopRemoteProcess)
    {
        if (stopRemoteProcess)
            await TryStopRemoteProcessAsync(ProcessSchema.StopMode.hard);
        await CancelCurrentLifecycleAsync();

        _processRunTask = null;
        Process?.Dispose();
        Process = null;
        _processStateTransitionCallback?.Dispose();
        _processStateTransitionCallback = null;
        ProcessStarted = false;
    }

    private async Task TryStopRemoteProcessAsync(ProcessSchema.StopMode mode)
    {
        if (Process == null)
            return;

        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::ResetRemoteRuntimeAsync stopping process"
        );
        try
        {
            await Process.Stop(mode);
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: process already disposed during stop: {ex.Message}"
            );
        }
        catch (RpcException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: process RPC failed during stop: {ex.Message}"
            );
        }
    }

    private async Task CancelCurrentLifecycleAsync()
    {
        if (_cancellationTokenSource == null)
            return;

        await _cancellationTokenSource.CancelAsync();
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = null;
    }

    private async Task ObserveProcessRunAsync(
        Task processRunTask,
        CancellationTokenSource lifecycleTokenSource
    )
    {
        try
        {
            await processRunTask;
        }
        catch (OperationCanceledException) when (lifecycleTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            if (!ReferenceEquals(_cancellationTokenSource, lifecycleTokenSource))
                return;

            await ResetRemoteRuntimeAsync(stopRemoteProcess: true);
            SetLifecycleFault(e, refresh: true);
        }
        finally
        {
            if (ReferenceEquals(_processRunTask, processRunTask))
                _processRunTask = null;
        }
    }

    private void ApplyProcessState(ProcessSchema.State state, bool refresh = false)
    {
        switch (state)
        {
            case ProcessSchema.State.starting:
                SetLifecycleState(ComponentLifecycleState.Starting, refresh: refresh);
                break;
            case ProcessSchema.State.running:
                SetLifecycleState(ComponentLifecycleState.Running, refresh: refresh);
                break;
            case ProcessSchema.State.stopping:
                SetLifecycleState(ComponentLifecycleState.Stopping, refresh: refresh);
                break;
            case ProcessSchema.State.stopped:
                SetLifecycleState(ComponentLifecycleState.Stopped, refresh: refresh);
                break;
            case ProcessSchema.State.failed:
                SetLifecycleState(ComponentLifecycleState.Faulted, refresh: refresh);
                break;
        }
    }

    private class ProcessStateTransition(Action<ProcessSchema.State, ProcessSchema.State> action)
        : ProcessSchema.IStateTransition
    {
        public Task StateChanged(
            ProcessSchema.State old,
            ProcessSchema.State @new,
            CancellationToken cancellationToken = default
        )
        {
            action(old, @new);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}
