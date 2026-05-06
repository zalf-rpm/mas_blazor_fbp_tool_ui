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
using Mas.Schema.Service;
using Newtonsoft.Json.Linq;
using Process = Mas.Schema.Fbp.Process;
using RpcException = Capnp.Rpc.RpcException;

namespace BlazorDrawFBP.Models;

public class CapnpFbpRunnableComponentModel : CapnpFbpComponentModel
{
    public CapnpFbpRunnableComponentModel(Point position = null)
        : base(position)
    {
        _stoppedCallback = new StoppedCallback(this);
    }

    public CapnpFbpRunnableComponentModel(string id, Point position = null)
        : base(id, position)
    {
        _stoppedCallback = new StoppedCallback(this);
    }

    private readonly StoppedCallback _stoppedCallback;
    private CancellationTokenSource _cancellationTokenSource;
    private CapnpFbpInPortModel _embeddedConfigInPort;
    private CapnpFbpOutPortModel _embeddedConfIipOutPort;
    private SturdyRef _portInfosReaderSr;
    private Channel<PortInfos>.IWriter _portInfosWriter;
    private IStoppable _stopPortInfosChannel { get; set; }

    public IRunnable Runnable { get; set; }

    public Runnable.IFactory RunnableFactory { get; set; }

    public override bool RemoteProcessAttached() => Runnable != null;
    public override bool CanEditCommandLine() => RunnableFactory != null || Runnable != null;

    public override async Task StartProcess(ConnectionManager conMan)
    {
        if (
            Editor.CurrentChannelStarterService == null
            || RunnableFactory == null
            || !CanStart
        )
        {
            return;
        }

        var shouldStopExistingRuntime =
            Runnable != null
            && LifecycleState is not ComponentLifecycleState.Idle
                and not ComponentLifecycleState.Closed;
        SetLifecycleState(ComponentLifecycleState.Starting, refresh: true);
        CancellationToken cancelToken = default;
        try
        {
            Console.WriteLine($"T{Environment.CurrentManagedThreadId} {ProcessName}: StartProcess");

            await ResetRemoteRuntimeAsync(shouldStopExistingRuntime);
            _cancellationTokenSource = new CancellationTokenSource();
            cancelToken = _cancellationTokenSource.Token;

            //get a fresh runnable
            if (Runnable == null)
            {
                Runnable = await RunnableFactory.Create(cancelToken);
                if (Runnable == null)
                {
                    return;
                }
            }

            List<PortInfos.NameAndSR> inPortSRs = [];
            Dictionary<CapnpFbpOutPortModel, List<SturdyRef>> outPortSrMap = [];
            HashSet<CapnpFbpInPortModel> collectedInPorts = [];

            async Task CollectPortSrs(
                CapnpFbpPortModel port,
                RememberCapnpPortsLinkModel link = null
            )
            {
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: collecting port srs"
                );

                switch (port)
                {
                    case CapnpFbpInPortModel inPort:
                        // if there is no SR
                        if (inPort.ReaderSturdyRef == null)
                        {
                            // but we have a task for the SR
                            if (inPort.RetrieveReaderFromChannelTask != null)
                            {
                                Console.WriteLine(
                                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: awaiting inPort.ChannelTask"
                                );
                                await inPort.RetrieveReaderFromChannelTask;
                            }
                        }
                        if (!collectedInPorts.Add(inPort))
                            break;
                        inPortSRs.Add(
                            new PortInfos.NameAndSR
                            {
                                Name = inPort.Name,
                                Sr = inPort.ReaderSturdyRef,
                            }
                        );
                        break;
                    case CapnpFbpOutPortModel outPort when link != null:
                        if (link.WriterSturdyRef == null)
                        {
                            await link.EnsureWriterFromChannelAsync(cancelToken);
                        }
                        if (link.WriterSturdyRef == null)
                            throw new InvalidOperationException(
                                $"Could not initialize writer for out port '{outPort.Name}'."
                            );
                        if (!outPortSrMap.TryGetValue(outPort, out var writerSrs))
                        {
                            writerSrs = [];
                            outPortSrMap[outPort] = writerSrs;
                        }
                        writerSrs.Add(link.WriterSturdyRef);
                        break;
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
                    //TODO: is bad to distinguish explicitly here, maybe we want to have more further component types later
                    if (
                        inPort.Parent is not CapnpFbpComponentModel
                        && inPort.Parent is not CapnpFbpViewComponentModel
                    )
                    {
                        continue;
                    }

                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: the IN port (link) is not associated with a channel yet -> create channel"
                    );
                    await Shared.Shared.CreateChannel(conMan, Editor.CurrentChannelStarterService, rcplm);
                }

                if (inPort.Parent == this)
                {
                    await CollectPortSrs(inPort);
                }

                if (inPort.Name == "config")
                {
                    configInPortConnected = true;
                }

                CapnpFbpPortColors.ApplyLinkColor(rcplm);

                // deal with OUT port
                await rcplm.EnsureWriterFromChannelAsync(cancelToken);

                if (outPort.Parent == this)
                {
                    await CollectPortSrs(outPort, rcplm);
                }
                outPort.Parent.Refresh();
            }

            List<PortInfos.NameAndSR> outPortSRs = outPortSrMap
                .OrderBy(entry => entry.Key.OrderNo)
                .ThenBy(entry => entry.Key.Name)
                .Select(entry =>
                {
                    var writerSrs = entry.Value;
                    if (entry.Key.IsArrayPort)
                    {
                        return new PortInfos.NameAndSR
                        {
                            Name = entry.Key.Name,
                            Srs = writerSrs,
                        };
                    }

                    return new PortInfos.NameAndSR
                    {
                        Name = entry.Key.Name,
                        Sr = writerSrs.LastOrDefault(),
                    };
                })
                .ToList();

            //there is no config port connected, so we setup up a config channel and send the process config on the fly
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: configInPort connected: {configInPortConnected} ConfigString: {ConfigString}"
            );
            if (!configInPortConnected && !string.IsNullOrWhiteSpace(ConfigString))
            {
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: sending config on the fly"
                );

                //create ports, if this is the first time
                _embeddedConfigInPort ??= new CapnpFbpInPortModel(null) { Name = "conf" };
                _embeddedConfIipOutPort ??= new CapnpFbpOutPortModel(null);

                //create channel, if not done before
                if (
                    _embeddedConfigInPort.ReaderSturdyRef == null
                    && _embeddedConfigInPort.RetrieveReaderFromChannelTask == null
                )
                {
                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: creating embedded config channel"
                    );
                    await Shared.Shared.CreateChannel(
                        conMan,
                        Editor.CurrentChannelStarterService,
                        _embeddedConfIipOutPort,
                        _embeddedConfigInPort
                    );
                }

                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: _embeddedConfigInPort.RWSR: {_embeddedConfigInPort.ReaderSturdyRef}"
                );
                //insert config port sturdy ref into collections for port info message later
                await CollectPortSrs(_embeddedConfigInPort);

                //now insert the current toml configuration into the config channel
                //check if channel creation task has been finished
                if (_embeddedConfIipOutPort.WriterSturdyRef == null)
                {
                    if (_embeddedConfIipOutPort.RetrieveWriterFromChannelTask != null)
                    {
                        Console.WriteLine(
                            $"T{Environment.CurrentManagedThreadId} {ProcessName}: awaiting embeddedConfigIipOutPort.ChannelTask"
                        );
                        await _embeddedConfIipOutPort.RetrieveWriterFromChannelTask;
                    }
                    else
                    {
                        (_embeddedConfIipOutPort.Writer, _embeddedConfIipOutPort.WriterSturdyRef) =
                            await Shared.Shared.GetNewWriterFromChannel(
                                _embeddedConfigInPort.Channel,
                                cancelToken
                            );
                    }
                }

                //if we didn't connect yet to the writer, do so
                if (_embeddedConfIipOutPort.Writer == null)
                {
                    Debug.Assert(
                        _embeddedConfIipOutPort.Writer != null,
                        "Here we should already have a writer, so no need to connect."
                    );
                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: before connecting to writer for embeddedConfIipOutPort.ChannelTask: {_embeddedConfIipOutPort.RetrieveWriterFromChannelTask?.IsCompletedSuccessfully}"
                    );
                    _embeddedConfIipOutPort.Writer = await conMan.Connect<Channel<IP>.IWriter>(
                        _embeddedConfIipOutPort.WriterSturdyRef
                    );
                }

                //send actual config string into channel
                await _embeddedConfIipOutPort.Writer.Write(
                    new Channel<IP>.Msg
                    {
                        Value = new IP
                        {
                            Content = new StructuredText
                            {
                                TheType = StructuredText.Type.json,
                                Value = ConfigString,
                            },
                        },
                    },
                    cancelToken
                );
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: sent embedded config IIP to config port"
                );
                //_configSendTask = CreateTaskAndSendIip(conMan, _confIipOutPort, ConfigString, cancelToken);
            }

            //start temporary port info channel and send port infos to component
            if (_portInfosWriter == null)
            {
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: Trying to start port info channel"
                );
                var si = await Editor.CurrentChannelStarterService.Start(
                    new StartChannelsService.Params { Name = $"port-infos_{ProcessName}" },
                    cancelToken
                );
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: Port info channel started si.Count={si.Item1.Count}, si[0].ReaderSRs.Count={si.Item1[0].ReaderSRs.Count}, si[0].WriterSRs.Count={si.Item1[0].WriterSRs.Count}"
                );
                if (
                    si.Item1.Count == 0
                    || si.Item1[0].ReaderSRs.Count == 0
                    || si.Item1[0].WriterSRs.Count == 0
                )
                {
                    return;
                }

                _portInfosReaderSr = si.Item1[0].ReaderSRs[0];
                _portInfosWriter = (
                    si.Item1[0].Writers[0] as Channel<object>.Writer_Proxy
                )?.Cast<Channel<PortInfos>.IWriter>(false);
                _stopPortInfosChannel = si.Item2;
            }

            if (_portInfosWriter == null || _portInfosReaderSr == null)
                throw new InvalidOperationException(
                    $"Could not initialize the port info channel for '{ProcessName}'."
                );

            var runnableStarted = await Runnable.Start(
                _portInfosReaderSr,
                ProcessName,
                _stoppedCallback,
                cancelToken
            );
            if (!runnableStarted)
                throw new InvalidOperationException($"Runnable '{ProcessName}' failed to start.");

            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: Runnable started: {ProcessStarted}"
            );
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: Writing port infos to port info channel"
            );
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: inPortSRs: {inPortSRs} outPortSRs: {outPortSRs}"
            );
            await _portInfosWriter.Write(
                new Channel<PortInfos>.Msg
                {
                    Value = new PortInfos { InPorts = inPortSRs, OutPorts = outPortSRs },
                },
                cancelToken
            );
            SetLifecycleState(ComponentLifecycleState.Running);
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: Wrote port infos to port info channel"
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
            await ResetRemoteRuntimeAsync(stopRunnable: true);
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
            await TryStopRunnableAsync();
        }
        catch (Exception e)
        {
            SetLifecycleFault(e, refresh: true);
        }
    }

    public override async Task ResetExecution()
    {
        var shouldStopExistingRuntime =
            Runnable != null
            && LifecycleState is not ComponentLifecycleState.Idle
                and not ComponentLifecycleState.Closed;
        await ResetRemoteRuntimeAsync(shouldStopExistingRuntime);
        SetLifecycleState(ComponentLifecycleState.Idle, refresh: true);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::Dispose"
        );
        await base.DisposeAsyncCore();
        await DisposeAdditionalRunnablePorts();
        await CancelAndDisposeRemoteComponent();
        RunnableFactory?.Dispose();
        _portInfosWriter?.Dispose();
    }

    private async Task CancelAndDisposeRemoteComponent()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::CancelAndDisposeRemoteComponent"
        );
        await ResetExecution();
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::CancelAndDisposeRemoteComponent stopped runnable/process (ProcessStarted: {ProcessStarted})"
        );
    }

    private async Task DisposeAdditionalRunnablePorts()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::DisposeAdditionalRunnablePorts"
        );

        if (_embeddedConfigInPort != null)
            await _embeddedConfigInPort.DisposeAsync();
        _embeddedConfigInPort = null;
        if (_embeddedConfIipOutPort != null)
            await _embeddedConfIipOutPort.DisposeAsync();
        _embeddedConfIipOutPort = null;
    }

    private class StoppedCallback(CapnpFbpRunnableComponentModel runnableModel)
        : Mas.Schema.Fbp.Runnable.IStoppedCallback
    {
        public void Dispose() { }

        public Task Stopped(CancellationToken cancellationToken = default)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {runnableModel.ProcessName} StoppedCallback::Stopped received"
            );
            runnableModel.SetLifecycleState(ComponentLifecycleState.Idle, refresh: true);
            return Task.CompletedTask;
            //return runnableModel.StopProcess(null);
        }
    }

    private async Task ResetRemoteRuntimeAsync(bool stopRunnable)
    {
        await TryWritePortInfosDoneAsync();
        if (stopRunnable)
            await TryStopRunnableAsync();
        await TryStopPortInfosChannelAsync();
        await CancelCurrentLifecycleAsync();

        _portInfosWriter?.Dispose();
        _portInfosWriter = null;
        _portInfosReaderSr = null;
        Runnable?.Dispose();
        Runnable = null;
        ProcessStarted = false;
    }

    private async Task TryWritePortInfosDoneAsync()
    {
        if (_portInfosWriter == null)
            return;

        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: sending DONE to the port info channel"
        );
        try
        {
            await _portInfosWriter.Write(
                new Channel<PortInfos>.Msg { which = Channel<PortInfos>.Msg.WHICH.Done }
            );
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: port info writer already disposed: {ex.Message}"
            );
        }
        catch (RpcException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: port info writer RPC failed: {ex.Message}"
            );
        }
    }

    private async Task TryStopRunnableAsync()
    {
        if (Runnable == null)
            return;

        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::ResetRemoteRuntimeAsync stopping runnable/process"
        );
        try
        {
            await Runnable.Stop();
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: runnable already disposed during stop: {ex.Message}"
            );
        }
        catch (RpcException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: runnable RPC failed during stop: {ex.Message}"
            );
        }
    }

    private async Task TryStopPortInfosChannelAsync()
    {
        if (_stopPortInfosChannel == null)
            return;

        try
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} CapnpFbpRunnableComponentModel::DisposeAdditionalRunnablePorts: Stop port infos channel"
            );
            await _stopPortInfosChannel.Stop();
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} CapnpFbpRunnableComponentModel::DisposeAdditionalRunnablePorts: Stopped port infos channel"
            );
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: port info channel already disposed: {ex.Message}"
            );
        }
        catch (RpcException ex)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: port info channel RPC failed during stop: {ex.Message}"
            );
        }
        finally
        {
            _stopPortInfosChannel.Dispose();
            _stopPortInfosChannel = null;
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
}
