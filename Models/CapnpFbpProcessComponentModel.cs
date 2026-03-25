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
using Tomlyn;
using Process = Mas.Schema.Fbp.Process;

namespace BlazorDrawFBP.Models;

public class CapnpFbpProcessComponentModel : CapnpFbpComponentModel {
    public CapnpFbpProcessComponentModel(Point position = null)
        : base(position) { }

    public CapnpFbpProcessComponentModel(string id, Point position = null)
        : base(id, position) { }

    private CancellationTokenSource _cancellationTokenSource;
    private ProcessStateTransition _processStateTransitionCallback;
    private Task _processStartTask;
    private Task _processStopTask;
    public IProcess Process { get; set; }

    public Process.IFactory ProcessFactory { get; set; }

    public override bool RemoteProcessAttached() => Process != null;

    public override async Task StartProcess(ConnectionManager conMan) {
        try {
            if (
                Editor.CurrentChannelStarterService == null ||
                ProcessFactory == null ||
                ProcessStarted ||
                _processStartTask != null
            ) {
                return;
            }

            Console.WriteLine($"{ProcessName}: StartProcess");

            _cancellationTokenSource = new CancellationTokenSource();
            var cancelToken = _cancellationTokenSource.Token;

            //get a fresh Process
            if (Process == null) {
                Process = await ProcessFactory.Create(cancelToken);
                if (Process == null) {
                    return;
                }

                if (_processStateTransitionCallback == null) {
                    // register a state transition callback
                    // to switch the state display
                    _processStateTransitionCallback = new ProcessStateTransition((old, @new) => {
                        switch (@new) {
                            case Mas.Schema.Fbp.Process.State.started:
                                ProcessStarted = true;
                                Refresh();
                                break;
                            case Mas.Schema.Fbp.Process.State.canceled: break;
                            case Mas.Schema.Fbp.Process.State.stopped:
                                ProcessStarted = false;
                                Refresh();
                                break;
                        }
                    });
                    await Process.State(_processStateTransitionCallback, cancelToken);
                }
            }

            var configInPortConnected = false;
            // collect SRs from IN and OUT ports and for IIPs send it into the channel
            foreach (var pl in Links) {
                if (pl is not RememberCapnpPortsLinkModel {
                        InPortModel: CapnpFbpInPortModel inPort,
                        OutPortModel: CapnpFbpOutPortModel outPort } rcplm) {
                    continue;
                }

                // deal with IN port
                // the IN port (link) is not associated with a channel yet -> create channel
                if (
                    inPort.ReaderSturdyRef == null
                    && inPort.RetrieveReaderFromChannelTask == null
                ) {
                    // if (inPort.Parent is not CapnpFbpComponentModel &&
                    //     inPort.Parent is not CapnpFbpViewComponentModel) {
                    //     continue;
                    // }

                    Console.WriteLine(
                        $"{ProcessName}: the IN port (link) is not associated with a channel yet -> create channel");
                    await Shared.Shared.CreateChannel(conMan,
                        Editor.CurrentChannelStarterService,
                        outPort,
                        inPort);
                }

                // if this is our IN port, set it at the remote process
                if (inPort.Parent == this) {
                    Console.WriteLine($"{ProcessName}: setting in port '{inPort.Name}' at remote process");
                    inPort.Connected = await Process.ConnectInPort(inPort.Name,
                        inPort.ReaderSturdyRef,
                        cancelToken);
                }

                if (inPort.Name == "config") {
                    configInPortConnected = true;
                }

                //color links with connected channel green
                rcplm.Color = inPort.Channel != null ? "#1ac12e" : "black";

                // deal with OUT port
                Console.WriteLine(
                    $"{ProcessName}: dealing with out port '{outPort.Name}'\noutPort.RetrieveReaderOrWriterFromChannelTask: {outPort.RetrieveWriterFromChannelTask}\noutPort.ReaderWriterSturdyRef: {outPort.WriterSturdyRef} inPort.Channel: {inPort.Channel}");
                // the task for setting the writer was not yet finished
                // wait for the task so the writer will be set
                if (outPort.RetrieveWriterFromChannelTask != null) {
                    Console.WriteLine($"{ProcessName}: awaiting out port '{outPort.Name}' ChannelTask");
                    await outPort.RetrieveWriterFromChannelTask;
                } else {
                    // there is no task anymore, but also no sturdy ref available,
                    // so get a new writer from the channel
                    if (outPort.WriterSturdyRef == null) {
                        Console.WriteLine(
                            $"{ProcessName}: getting new writer for out port '{outPort.Name}' from channel");
                        (outPort.Writer, outPort.WriterSturdyRef) =
                            await Shared.Shared.GetNewWriterFromChannel(inPort.Channel,
                                cancelToken);
                    }
                }

                outPort.Parent.Refresh();
                outPort.Parent.RefreshLinks();

                if (outPort.Parent != this) continue;
                Console.WriteLine($"{ProcessName}: setting out port '{outPort.Name}' at remote process");
                outPort.Connected = await Process.ConnectOutPort(outPort.Name,
                    outPort.WriterSturdyRef,
                    cancelToken);
            }

            //there is no config port connected, so we setup up a config channel and send the process config on the fly
            Console.WriteLine(
                $"{ProcessName}: configInPort connected: {configInPortConnected} ConfigString: {ConfigString}");
            if (!configInPortConnected && !string.IsNullOrWhiteSpace(ConfigString)) {
                Console.WriteLine($"{ProcessName}: sending config on the fly");

                //var model = Toml.ToModel(ConfigString);
                var model = JObject.Parse(ConfigString);
                foreach (var kv in model) {
                    var val = kv.Value.Type switch {
                        JTokenType.String => new Value { T = kv.Value.Value<string>() },
                        JTokenType.Integer => new Value { I64 = kv.Value.Value<long>() },
                        JTokenType.Float => new Value { F64 = kv.Value.Value<double>() },
                        JTokenType.Boolean => new Value { B = kv.Value.Value<bool>() },
                    };
                    await Process.SetConfigEntry(new Process.ConfigEntry { Name = kv.Key, Val = val },
                        cancelToken);
                }

                Console.WriteLine($"{ProcessName}: set full config");
            }

            //await Process.Start(cancelToken);
            _processStartTask = Task.Run(async () => await Process.Start(cancelToken), cancelToken).
                ContinueWith((r) => _processStartTask = null);
            ProcessStarted = true;
            Console.WriteLine($"{ProcessName}: Process started: {ProcessStarted}");
            RefreshAll();
            RefreshLinks();
        } catch (Exception e) {
            Console.WriteLine($"{ProcessName}: CapnpFbpProcessComponentModel::StartProcess: Caught exception: {e}");
        }
    }

    public override async Task StopProcess(ConnectionManager conMan) {
        try {
            if (Process != null && _processStopTask == null) {
                //await Process.Stop();
                _processStopTask = Task.Run(async () => await Process.Stop(), _cancellationTokenSource.Token).
                    ContinueWith((r) => _processStopTask = null);
                ProcessStarted = false;
            }
        } catch (Exception e) {
            Console.WriteLine($"{ProcessName}: CapnpFbpProcessComponentModel::StopProcess: Caught exception: {e}");
        }
    }

    public override async Task CancelAndDisposeRemoteComponent() {
        Console.WriteLine($"{ProcessName}: CapnpFbpProcessComponentModel::CancelAndDisposeRemoteComponent");

        if (Process == null) {
            return;
        }

        //cancel task
        if (_cancellationTokenSource != null) {
            await _cancellationTokenSource.CancelAsync();
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        //stop remote process
        Console.WriteLine(
            $"{ProcessName}: CapnpFbpProcessComponentModel::CancelAndDisposeRemoteComponent stopping runnable/process");
        await Process.Stop();
        ProcessStarted = false;

        Console.WriteLine(
            $"{ProcessName}: CapnpFbpProcessComponentModel::CancelAndDisposeRemoteComponent stopped runnable/process (ProcessStarted: {ProcessStarted})");
        Process?.Dispose();
        Process = null;
        _processStateTransitionCallback?.Dispose();
        _processStateTransitionCallback = null;
    }

    public override void Dispose() {
        Console.WriteLine($"{ProcessName}: CapnpFbpProcessComponentModel::Dispose");
        base.Dispose();
        ProcessFactory?.Dispose();
    }

    public class ProcessStateTransition(Action<Process.State, Process.State> action)
        : Process.IStateTransition {
        public Task StateChanged(
            Process.State old,
            Process.State @new,
            CancellationToken cancellationToken = default
        ) {
            action(old, @new);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}