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
    private ProcessActivityTransition _processActivityTransitionCallback;
    public IProcess Process { get; set; }
    public ProcessSchema.IProcessHandle ProcessHandle { get; set; }

    public ProcessSchema.IFactory ProcessFactory { get; set; }
    protected override bool SupportsProcMultiplication => true;

    public override bool RemoteProcessAttached() => ProcessHandle != null || Process != null;

    public override bool CanEditCommandLine() =>
        ProcessFactory != null || ProcessHandle != null || Process != null;

    public bool SupportsLivePortChanges =>
        Process != null && LifecycleState == ComponentLifecycleState.Running;
    public ProcessSchema.ActivityState ActivityState { get; private set; } =
        ProcessSchema.ActivityState.none;
    public string ActivityPortName { get; private set; }
    public string ActivitySummary => FormatActivitySummary(ActivityState, ActivityPortName);
    public ProcessSchema.RunInfo LastRunInfo { get; private set; }
    public bool HasLastRunInfo => LastRunInfo != null;
    public ProcessSchema.RunInfo.Outcome LastRunOutcome =>
        LastRunInfo?.TheOutcome ?? ProcessSchema.RunInfo.Outcome.none;
    public string LastRunSummary => FormatLastRunSummary(LastRunInfo);
    public bool IsProcessingActivity =>
        LifecycleState == ComponentLifecycleState.Running
        && ActivityState == ProcessSchema.ActivityState.processing;

    protected override CapnpFbpComponentModel CreateProcChildModel(int displayIndex) =>
        new CapnpFbpProcessComponentModel(
            $"{Id}__proc_{displayIndex}",
            Position == null ? null : new Point(Position.X, Position.Y)
        );

    public bool IsWaitingOnPort(CapnpFbpPortModel port)
    {
        if (
            port == null
            || LifecycleState != ComponentLifecycleState.Running
            || string.IsNullOrWhiteSpace(ActivityPortName)
        )
        {
            return false;
        }

        if (
            ActivityState == ProcessSchema.ActivityState.waitingInput
            && port.ThePortType != CapnpFbpPortModel.PortType.In
        )
        {
            return false;
        }

        if (
            ActivityState == ProcessSchema.ActivityState.waitingOutput
            && port.ThePortType != CapnpFbpPortModel.PortType.Out
        )
        {
            return false;
        }

        if (
            ActivityState
            is not (
                ProcessSchema.ActivityState.waitingInput or ProcessSchema.ActivityState.waitingOutput
            )
        )
        {
            return false;
        }

        return string.Equals(port.Name, ActivityPortName, StringComparison.OrdinalIgnoreCase);
    }

    public override async Task StartProcess(ConnectionManager conMan)
    {
        await StartSingleProcessAsync(conMan);
        if (!IsInternalProcChild && LifecycleState == ComponentLifecycleState.Running)
            await StartOwnedProcChildrenAsync(conMan);
    }

    private async Task StartSingleProcessAsync(ConnectionManager conMan)
    {
        if (
            Editor.CurrentChannelStarterService == null
            || ProcessFactory == null
            || LifecycleState is ComponentLifecycleState.Starting or ComponentLifecycleState.Stopping
            || LifecycleState is not (ComponentLifecycleState.Idle or ComponentLifecycleState.Failed)
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
                    await Shared.Shared.CreateChannel(
                        conMan,
                        Editor.CurrentChannelStarterService,
                        rcplm
                    );
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

                Value MakeCommonValue(JToken jt)
                {
                    switch (jt.Type)
                    {
                        case JTokenType.String:
                            return new Value { T = jt.Value<string>() };
                        case JTokenType.Integer:
                            return new Value { I64 = jt.Value<long>() };
                        case JTokenType.Float:
                            return new Value { F64 = jt.Value<double>() };
                        case JTokenType.Boolean:
                            return new Value { B = jt.Value<bool>() };
                        case JTokenType.Array:
                            if (jt is JArray arr)
                            {
                                var types = arr.Select(t => t.Type).ToHashSet();
                                if (types.Count == 1)
                                {
                                    var x = arr.Select(t => new Value { T = t.Value<string>() })
                                        .ToList();
                                    switch (types.First())
                                    {
                                        case JTokenType.String:
                                            return new Value
                                            {
                                                Lt = arr.Select(t => t.Value<string>()).ToList(),
                                            };
                                        case JTokenType.Integer:
                                            return new Value
                                            {
                                                Li64 = arr.Select(t => t.Value<long>()).ToList(),
                                            };
                                        case JTokenType.Float:
                                            return new Value
                                            {
                                                Lf64 = arr.Select(t => t.Value<double>()).ToList(),
                                            };
                                        case JTokenType.Boolean:
                                            return new Value
                                            {
                                                Lb = arr.Select(t => t.Value<bool>()).ToList(),
                                            };
                                    }
                                }
                                var hl = new List<Value>();
                                foreach (var t in arr)
                                {
                                    hl.Add(
                                        t.Type switch
                                        {
                                            JTokenType.String => new Value
                                            {
                                                T = t.Value<string>(),
                                            },
                                            JTokenType.Integer => new Value
                                            {
                                                I64 = t.Value<long>(),
                                            },
                                            JTokenType.Float => new Value
                                            {
                                                F64 = t.Value<double>(),
                                            },
                                            JTokenType.Boolean => new Value { B = t.Value<bool>() },
                                            JTokenType.Array or JTokenType.Object =>
                                                MakeCommonValue(t),
                                        }
                                    );
                                }
                                return new Value { Lv = hl };
                            }
                            break;
                        case JTokenType.Object:
                            if (jt is JObject obj)
                            {
                                var pl = new List<Pair<object, object>>(); //string, Value>>();
                                foreach (var (k, v) in obj)
                                {
                                    if (v == null)
                                        continue;
                                    pl.Add(
                                        new Pair<object, object>()
                                        { //}string, Value>() {
                                            Fst = k,
                                            Snd = v.Type switch
                                            {
                                                JTokenType.String => new Value
                                                {
                                                    T = v.Value<string>(),
                                                },
                                                JTokenType.Integer => new Value
                                                {
                                                    I64 = v.Value<long>(),
                                                },
                                                JTokenType.Float => new Value
                                                {
                                                    F64 = v.Value<double>(),
                                                },
                                                JTokenType.Boolean => new Value
                                                {
                                                    B = v.Value<bool>(),
                                                },
                                                JTokenType.Array or JTokenType.Object =>
                                                    MakeCommonValue(v),
                                            },
                                        }
                                    );
                                }
                                return new Value { Lpair = pl };
                            }

                            break;
                        case JTokenType.Null:
                        default:
                            return null;
                    }

                    return null;
                }

                //var model = Toml.ToModel(ConfigString);
                var model = JObject.Parse(ConfigString);
                foreach (var kv in model)
                {
                    var val = MakeCommonValue(kv.Value);
                    if (val == null)
                    {
                        Console.WriteLine(
                            $"T{Environment.CurrentManagedThreadId} {ProcessName}: skipping null config entry '{kv.Key}'"
                        );
                        continue;
                    }
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
            {
                var remoteError = FormatLastRunFailure(await RefreshLastRunInfoAsync(cancelToken));
                throw new InvalidOperationException(
                    remoteError ?? $"Process '{ProcessName}' failed to start."
                );
            }
            SetLifecycleState(ComponentLifecycleState.Running, refresh: true);
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
            SetLifecycleFaultPreservingRemoteError(e, refresh: true);
        }
    }

    public override async Task StopProcess(ConnectionManager conMan)
    {
        if (!IsInternalProcChild)
            await StopOwnedProcChildrenAsync(conMan);

        await StopSingleProcessAsync(conMan);
    }

    private async Task StopSingleProcessAsync(ConnectionManager conMan)
    {
        if (
            LifecycleState is ComponentLifecycleState.Starting or ComponentLifecycleState.Stopping
            || LifecycleState != ComponentLifecycleState.Running
        )
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
            SetLifecycleFaultPreservingRemoteError(e, refresh: true);
        }
    }

    public override async Task ResetExecution()
    {
        if (!IsInternalProcChild)
            await ResetOwnedProcChildrenAsync();

        await ResetSingleExecutionAsync();
    }

    private async Task ResetSingleExecutionAsync()
    {
        ClearLastRunInfo();
        var shouldStopExistingRuntime =
            ProcessHandle != null
            && (
                LifecycleState is not ComponentLifecycleState.Idle
                || !await TryIsProcessHandleAliveAsync()
            );
        await ResetRemoteRuntimeAsync(shouldStopExistingRuntime);
        SetLifecycleState(ComponentLifecycleState.Idle, refresh: true);
    }

    protected override async Task ShutdownForComponentServiceSwitchAsync()
    {
        if (!IsInternalProcChild)
            await ShutdownOwnedProcChildrenAsync();

        await ShutdownSingleForComponentServiceSwitchAsync();
    }

    private async Task ShutdownSingleForComponentServiceSwitchAsync()
    {
        if (Process != null)
            await TryStopRemoteProcessAsync();

        await ResetRemoteRuntimeAsync(closeRemoteProcess: ProcessHandle != null || Process != null);
    }

    protected override void ApplyComponentServiceBinding(Component component, string componentServiceId)
    {
        base.ApplyComponentServiceBinding(component, componentServiceId);
        ClearLastRunInfo();

        ProcessFactory?.Dispose();
        ProcessFactory =
            component.Factory?.which == Component.factory.WHICH.Process
                ? Capnp.Rpc.Proxy.Share(component.Factory.Process)
                : null;
    }

    protected override void CopyProcBindingTo(CapnpFbpComponentModel child)
    {
        if (child is not CapnpFbpProcessComponentModel processChild)
            return;

        processChild.ProcessFactory =
            ProcessFactory != null ? Capnp.Rpc.Proxy.Share(ProcessFactory) : null;
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
        ResetActivityState();
        if (!closeRemoteProcess)
            return;

        Process?.Dispose();
        Process = null;
        ProcessHandle?.Dispose();
        ProcessHandle = null;
        _processStateTransitionCallback?.Dispose();
        _processStateTransitionCallback = null;
        _processActivityTransitionCallback?.Dispose();
        _processActivityTransitionCallback = null;
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
                async (old, @new, transitionCancelToken) =>
                {
                    await ApplyProcessStateAsync(@new, refresh: true, transitionCancelToken);
                }
            );
        }

        var currentState = await Process.State(_processStateTransitionCallback, cancelToken);
        await ApplyProcessStateAsync(currentState, refresh: false, cancelToken);
        await SubscribeToProcessActivityAsync(cancelToken);

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
        }

        if (refresh)
            ProcOwnerNode?.RefreshAll();
    }

    private async Task ApplyProcessStateAsync(
        ProcessSchema.State state,
        bool refresh = false,
        CancellationToken cancelToken = default
    )
    {
        ProcessSchema.RunInfo lastRunInfo = null;
        if (state is ProcessSchema.State.failed or ProcessSchema.State.idle)
            lastRunInfo = await RefreshLastRunInfoAsync(cancelToken);

        if (state == ProcessSchema.State.failed)
        {
            SetLifecycleState(
                ComponentLifecycleState.Failed,
                FormatLastRunFailure(lastRunInfo)
                    ?? LifecycleError
                    ?? $"Process '{ProcessName}' failed on the server.",
                refresh: refresh
            );
            if (refresh)
                ProcOwnerNode?.RefreshAll();
            return;
        }

        ApplyProcessState(state, refresh);
    }

    private async Task SubscribeToProcessActivityAsync(CancellationToken cancelToken)
    {
        if (Process == null)
            return;

        if (_processActivityTransitionCallback == null)
        {
            _processActivityTransitionCallback = new ProcessActivityTransition(
                (old, @new, transitionCancelToken) =>
                {
                    ApplyActivityInfo(@new, refresh: true);
                    return Task.CompletedTask;
                }
            );
        }

        var currentActivity = await Process.Activity(_processActivityTransitionCallback, cancelToken);
        ApplyActivityInfo(currentActivity, refresh: false);
    }

    private void ApplyActivityInfo(ProcessSchema.ActivityInfo activity, bool refresh = false)
    {
        var nextState = activity?.State ?? ProcessSchema.ActivityState.none;
        var nextPortName = NormalizeActivityPortName(activity?.Port);
        var changed =
            ActivityState != nextState
            || !string.Equals(ActivityPortName, nextPortName, StringComparison.Ordinal);

        ActivityState = nextState;
        ActivityPortName = nextPortName;

        if (refresh && changed)
        {
            RefreshAll();
            ProcOwnerNode?.RefreshAll();
        }
    }

    private void ResetActivityState(bool refresh = false)
    {
        var changed =
            ActivityState != ProcessSchema.ActivityState.none
            || !string.IsNullOrWhiteSpace(ActivityPortName);

        ActivityState = ProcessSchema.ActivityState.none;
        ActivityPortName = null;

        if (refresh && changed)
        {
            RefreshAll();
            ProcOwnerNode?.RefreshAll();
        }
    }

    private static string NormalizeActivityPortName(string portName) =>
        string.IsNullOrWhiteSpace(portName) ? null : portName.Trim();

    private static string FormatActivitySummary(
        ProcessSchema.ActivityState activityState,
        string activityPortName
    )
    {
        var label = activityState switch
        {
            ProcessSchema.ActivityState.none => "None",
            ProcessSchema.ActivityState.waitingInput => "Waiting input",
            ProcessSchema.ActivityState.processing => "Processing",
            ProcessSchema.ActivityState.waitingOutput => "Waiting output",
            ProcessSchema.ActivityState.closing => "Closing",
            _ => activityState.ToString(),
        };

        if (
            string.IsNullOrWhiteSpace(activityPortName)
            || activityState
                is not (
                    ProcessSchema.ActivityState.waitingInput
                    or ProcessSchema.ActivityState.waitingOutput
                )
        )
        {
            return label;
        }

        return $"{label} on {activityPortName}";
    }

    public IReadOnlyList<string> GetLastRunTooltipLines(
        bool includeProcessIdentity = false,
        bool includeTraceback = true
    ) =>
        BuildLastRunDetailLines(
            LastRunInfo,
            includeHeading: true,
            includeProcessIdentity,
            includeTraceback
        );

    private async Task<ProcessSchema.RunInfo> RefreshLastRunInfoAsync(
        CancellationToken cancelToken = default
    )
    {
        LastRunInfo = await TryGetRemoteLastRunInfoAsync(cancelToken);
        return LastRunInfo;
    }

    private async Task<ProcessSchema.RunInfo> TryGetRemoteLastRunInfoAsync(
        CancellationToken cancelToken = default
    )
    {
        if (Process == null)
            return null;

        try
        {
            return NormalizeLastRunInfo(await Process.LastRun(cancelToken));
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
            return null;
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: process lastRun RPC unavailable: {ex.Message}"
            );
            return null;
        }
        catch (RpcException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: process lastRun RPC failed: {ex.Message}"
            );
            return null;
        }
    }

    private void ClearLastRunInfo()
    {
        LastRunInfo = null;
    }

    private static ProcessSchema.RunInfo NormalizeLastRunInfo(ProcessSchema.RunInfo runInfo)
    {
        if (
            runInfo == null
            || !runInfo.HasRunInfo
            || runInfo.TheOutcome == ProcessSchema.RunInfo.Outcome.none
        )
        {
            return null;
        }

        return runInfo;
    }

    private static string FormatLastRunFailure(ProcessSchema.RunInfo runInfo)
    {
        var lines = BuildLastRunDetailLines(
            runInfo,
            includeHeading: false,
            includeProcessIdentity: false,
            includeTraceback: true
        );
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string FormatLastRunSummary(ProcessSchema.RunInfo runInfo)
    {
        if (runInfo == null)
            return null;

        var outcome = FormatRunOutcome(runInfo.TheOutcome);
        if (string.IsNullOrWhiteSpace(outcome))
            return null;

        List<string> contextParts = [];
        var phase = FormatRunPhase(runInfo.ThePhase);
        if (!string.IsNullOrWhiteSpace(phase))
            contextParts.Add($"during {phase}");

        var port = string.IsNullOrWhiteSpace(runInfo.Port) ? null : runInfo.Port.Trim();
        if (!string.IsNullOrWhiteSpace(port))
            contextParts.Add($"on {port}");

        var context = contextParts.Count == 0 ? string.Empty : $" {string.Join(" ", contextParts)}";
        var detail = FormatRunDetail(runInfo.DetailType, runInfo.Message);
        if (
            runInfo.TheOutcome == ProcessSchema.RunInfo.Outcome.failed
            && !string.IsNullOrWhiteSpace(detail)
        )
        {
            return $"{outcome}{context}: {detail}";
        }

        return $"{outcome}{context}";
    }

    private static IReadOnlyList<string> BuildLastRunDetailLines(
        ProcessSchema.RunInfo runInfo,
        bool includeHeading,
        bool includeProcessIdentity,
        bool includeTraceback
    )
    {
        if (runInfo == null)
            return [];

        List<string> lines = [];

        var headline = FormatLastRunSummary(runInfo);
        if (!string.IsNullOrWhiteSpace(headline))
            lines.Add(includeHeading ? $"Last run: {headline}" : headline);

        var processLabel = string.IsNullOrWhiteSpace(runInfo.ProcessName)
            ? null
            : runInfo.ProcessName.Trim();
        var processId = string.IsNullOrWhiteSpace(runInfo.ProcessId)
            ? null
            : runInfo.ProcessId.Trim();
        if (!string.IsNullOrWhiteSpace(processId))
        {
            processLabel = string.IsNullOrWhiteSpace(processLabel)
                ? processId
                : $"{processLabel} ({processId})";
        }

        if (includeProcessIdentity && !string.IsNullOrWhiteSpace(processLabel))
            lines.Add($"Process: {processLabel}");

        if (runInfo.TheOutcome != ProcessSchema.RunInfo.Outcome.failed)
        {
            var detail = FormatRunDetail(runInfo.DetailType, runInfo.Message);
            if (!string.IsNullOrWhiteSpace(detail))
                lines.Add($"Detail: {detail}");
        }

        var cause = FormatRunDetail(runInfo.CauseType, runInfo.CauseMessage);
        if (!string.IsNullOrWhiteSpace(cause))
            lines.Add($"Cause: {cause}");

        var tracebackLines = runInfo.Traceback?
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();
        if (includeTraceback && tracebackLines?.Count > 0)
        {
            lines.Add("Traceback:");
            lines.AddRange(tracebackLines.Take(6).Select(line => $"  {line}"));
            if (tracebackLines.Count > 6)
                lines.Add("  ...");
        }

        return lines;
    }

    private static string FormatRunOutcome(ProcessSchema.RunInfo.Outcome outcome)
    {
        return outcome switch
        {
            ProcessSchema.RunInfo.Outcome.completed => "Completed",
            ProcessSchema.RunInfo.Outcome.stopped => "Stopped",
            ProcessSchema.RunInfo.Outcome.failed => "Failed",
            _ => null,
        };
    }

    private static string FormatRunPhase(ProcessSchema.RunInfo.Phase phase)
    {
        return phase switch
        {
            ProcessSchema.RunInfo.Phase.config => "config",
            ProcessSchema.RunInfo.Phase.read => "read",
            ProcessSchema.RunInfo.Phase.run => "run",
            ProcessSchema.RunInfo.Phase.write => "write",
            ProcessSchema.RunInfo.Phase.close => "close",
            _ => null,
        };
    }

    private static string FormatRunDetail(string detailType, string message)
    {
        var trimmedType = string.IsNullOrWhiteSpace(detailType) ? null : detailType.Trim();
        var trimmedMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

        if (string.IsNullOrWhiteSpace(trimmedType))
            return trimmedMessage;
        if (string.IsNullOrWhiteSpace(trimmedMessage))
            return trimmedType;

        return $"{trimmedType}: {trimmedMessage}";
    }

    private void SetLifecycleFaultPreservingRemoteError(Exception exception, bool refresh = false)
    {
        Console.Error.WriteLine(exception);
        var error =
            LifecycleState == ComponentLifecycleState.Failed
            && !string.IsNullOrWhiteSpace(LifecycleError)
                ? LifecycleError
                : exception.Message;
        SetLifecycleState(ComponentLifecycleState.Failed, error, refresh);
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
            var link in Shared
                .Shared.AttachedLinks(this)
                .OfType<RememberCapnpPortsLinkModel>()
                .Where(link => ReferenceEquals(link.OutPortModel.Parent, this))
        )
        {
            link.ClearProcessOutDisconnect();
        }
    }

    private class ProcessStateTransition(
        Func<ProcessSchema.State, ProcessSchema.State, CancellationToken, Task> action
    )
        : ProcessSchema.IStateTransition
    {
        public Task StateChanged(
            ProcessSchema.State old,
            ProcessSchema.State @new,
            CancellationToken cancellationToken = default
        )
        {
            return action(old, @new, cancellationToken);
        }

        public void Dispose() { }
    }

    private class ProcessActivityTransition(
        Func<ProcessSchema.ActivityInfo, ProcessSchema.ActivityInfo, CancellationToken, Task> action
    )
        : ProcessSchema.IActivityTransition
    {
        public Task ActivityChanged(
            ProcessSchema.ActivityInfo old,
            ProcessSchema.ActivityInfo @new,
            CancellationToken cancellationToken = default
        )
        {
            return action(old, @new, cancellationToken);
        }

        public void Dispose() { }
    }
}
