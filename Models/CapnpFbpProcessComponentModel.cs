using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    public IProcess Process { get; set; }
    public ProcessSchema.IProcessHandle ProcessHandle { get; set; }

    public ProcessSchema.IFactory ProcessFactory { get; set; }

    public override bool RemoteProcessAttached() => ProcessHandle != null || Process != null;
    public override bool CanEditCommandLine() =>
        ProcessFactory != null || ProcessHandle != null || Process != null;
    public bool SupportsLivePortChanges =>
        Process != null
        && LifecycleState == ComponentLifecycleState.Running
        && !IsLifecycleBusy;

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
            ProcessHandle != null
            && (
                LifecycleState is not ComponentLifecycleState.Idle
                    || !await TryIsProcessHandleAliveAsync()
            );
        SetLifecycleState(ComponentLifecycleState.Starting, refresh: true);
        CancellationToken cancelToken = default;
        try
        {
            Console.WriteLine($"T{Environment.CurrentManagedThreadId} {ProcessName}: StartProcess");

            await ResetRemoteRuntimeAsync(shouldStopExistingRuntime);
            _cancellationTokenSource = new CancellationTokenSource();
            cancelToken = _cancellationTokenSource.Token;

            if (!await EnsureProcessAsync(cancelToken))
                throw new InvalidOperationException(
                    $"Process '{ProcessName}' did not provide a usable process handle."
                );

            HashSet<CapnpFbpInPortModel> connectedInPorts = [];
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
                    await Shared.Shared.CreateChannel(conMan, Editor.CurrentChannelStarterService, rcplm);
                }

                // if this is our IN port, set it at the remote process
                if (inPort.Parent == this && connectedInPorts.Add(inPort))
                {
                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: setting in port '{inPort.Name}' at remote process"
                    );
                    await ConnectInputPortAsync(inPort, cancelToken);
                }

                if (inPort.Name == "config")
                {
                    configInPortConnected = true;
                }

                CapnpFbpPortColors.ApplyLinkColor(rcplm);

                // deal with OUT port
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: dealing with out port '{outPort.Name}'\nlink.RetrieveWriterFromChannelTask: {rcplm.RetrieveWriterFromChannelTask}\nlink.WriterSturdyRef: {rcplm.WriterSturdyRef} inPort.Channel: {inPort.Channel}"
                );
                await rcplm.EnsureWriterFromChannelAsync(cancelToken);

                outPort.Parent.Refresh();
                outPort.Parent.RefreshLinks();

                if (outPort.Parent != this)
                    continue;
                if (rcplm.WriterSturdyRef == null)
                    throw new InvalidOperationException(
                        $"Could not initialize writer for out port '{outPort.Name}'."
                    );
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: setting out port '{outPort.Name}' at remote process"
                );
                await ConnectOutputPortAsync(rcplm, cancelToken);
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

            var started = await Process.Start(cancelToken);
            if (!started)
                throw new InvalidOperationException($"Process '{ProcessName}' failed to start.");
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: Process start launched"
            );
            RefreshAll();
            RefreshLinks();
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
            SetLifecycleState(ComponentLifecycleState.Idle, refresh: true);
        }
        catch (Exception e)
        {
            await ResetRemoteRuntimeAsync(closeRemoteProcess: true);
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
            var stopped = await TryStopRemoteProcessAsync();
            if (!stopped)
                throw new InvalidOperationException(
                    $"Process '{ProcessName}' failed to accept the stop request."
                );
        }
        catch (Exception e)
        {
            SetLifecycleFault(e, refresh: true);
        }
    }

    public override async Task ResetExecution()
    {
        var shouldStopExistingRuntime =
            ProcessHandle != null
            && (
                LifecycleState is not ComponentLifecycleState.Idle
                    || !await TryIsProcessHandleAliveAsync()
            );
        await ResetRemoteRuntimeAsync(shouldStopExistingRuntime);
        SetLifecycleState(ComponentLifecycleState.Idle, refresh: true);
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
        await ResetRemoteRuntimeAsync(closeRemoteProcess: ProcessHandle != null);
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::CancelAndDisposeRemoteComponent stopped runnable/process (ProcessStarted: {ProcessStarted})"
        );
    }

    private async Task ResetRemoteRuntimeAsync(bool closeRemoteProcess)
    {
        if (closeRemoteProcess)
            await TryCloseRemoteProcessHandleAsync();
        await CancelCurrentLifecycleAsync();
        ClearOwnedDisconnects();

        ProcessStarted = false;
        if (!closeRemoteProcess)
            return;

        Process?.Dispose();
        Process = null;
        ProcessHandle?.Dispose();
        ProcessHandle = null;
        _processStateTransitionCallback?.Dispose();
        _processStateTransitionCallback = null;
    }

    private async Task<bool> TryStopRemoteProcessAsync()
    {
        if (Process == null)
            return false;

        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::ResetRemoteRuntimeAsync stopping process"
        );
        try
        {
            return await Process.Stop();
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: process already disposed during stop: {ex.Message}"
            );
            return false;
        }
        catch (RpcException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: process RPC failed during stop: {ex.Message}"
            );
            return false;
        }
    }

    private async Task<bool> TryCloseRemoteProcessHandleAsync()
    {
        if (ProcessHandle == null)
            return false;

        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::ResetRemoteRuntimeAsync closing process handle"
        );
        try
        {
            return await ProcessHandle.Close();
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: process handle already disposed during close: {ex.Message}"
            );
            return false;
        }
        catch (RpcException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: process handle RPC failed during close: {ex.Message}"
            );
            return false;
        }
    }

    private async Task<bool> TryIsProcessHandleAliveAsync(CancellationToken cancelToken = default)
    {
        if (ProcessHandle == null)
            return false;

        try
        {
            return await ProcessHandle.Alive(cancelToken);
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: process handle already disposed during alive check: {ex.Message}"
            );
            return false;
        }
        catch (RpcException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: process handle RPC failed during alive check: {ex.Message}"
            );
            return false;
        }
    }

    private async Task<bool> EnsureProcessAsync(CancellationToken cancelToken)
    {
        if (ProcessHandle == null)
        {
            ProcessHandle = await ProcessFactory.Create(cancelToken);
            if (ProcessHandle == null)
                return false;
        }

        if (Process == null)
        {
            Process = await ProcessHandle.Process(cancelToken);
            if (Process == null)
                return false;
        }

        if (_processStateTransitionCallback == null)
        {
            _processStateTransitionCallback = new ProcessStateTransition(
                (old, @new) =>
                {
                    ApplyProcessState(@new, refresh: true);
                }
            );
        }

        await Process.State(_processStateTransitionCallback, cancelToken);

        return true;
    }

    private async Task CancelCurrentLifecycleAsync()
    {
        if (_cancellationTokenSource == null)
            return;

        await _cancellationTokenSource.CancelAsync();
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = null;
    }

    private void ApplyProcessState(ProcessSchema.State state, bool refresh = false)
    {
        switch (state)
        {
            case ProcessSchema.State.idle:
                SetLifecycleState(ComponentLifecycleState.Idle, refresh: refresh);
                break;
            case ProcessSchema.State.starting:
                SetLifecycleState(ComponentLifecycleState.Starting, refresh: refresh);
                break;
            case ProcessSchema.State.running:
                SetLifecycleState(ComponentLifecycleState.Running, refresh: refresh);
                break;
            case ProcessSchema.State.stopping:
                SetLifecycleState(ComponentLifecycleState.Stopping, refresh: refresh);
                break;
            case ProcessSchema.State.failed:
                SetLifecycleState(ComponentLifecycleState.Failed, refresh: refresh);
                break;
            case ProcessSchema.State.closed:
                SetLifecycleState(ComponentLifecycleState.Closed, refresh: refresh);
                break;
        }
    }

    public async Task ConnectInputPortAsync(
        CapnpFbpInPortModel inPort,
        CancellationToken cancelToken = default
    )
    {
        if (Process == null || inPort.ReaderSturdyRef == null)
            return;

        var (connected, disconnect) = await Process.ConnectInPort(
            inPort.Name,
            inPort.ReaderSturdyRef,
            cancelToken
        );
        inPort.SetProcessDisconnect(disconnect, connected);
        RefreshAll();
        RefreshLinks();
    }

    public async Task ConnectOutputPortAsync(
        RememberCapnpPortsLinkModel link,
        CancellationToken cancelToken = default
    )
    {
        if (Process == null || link.WriterSturdyRef == null)
            return;

        var (connected, disconnect) = await Process.ConnectOutPort(
            link.OutPortModel.Name,
            link.WriterSturdyRef,
            cancelToken
        );
        link.SetProcessOutDisconnect(disconnect, connected);
        RefreshAll();
        RefreshLinks();
    }

    private void ClearOwnedDisconnects()
    {
        foreach (var inPort in Ports.OfType<CapnpFbpInPortModel>())
            inPort.ClearProcessDisconnect();

        foreach (
            var link in Shared.Shared
                .AttachedLinks(this)
                .OfType<RememberCapnpPortsLinkModel>()
                .Where(link => ReferenceEquals(link.OutPortModel.Parent, this))
        )
        {
            link.ClearProcessOutDisconnect();
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
