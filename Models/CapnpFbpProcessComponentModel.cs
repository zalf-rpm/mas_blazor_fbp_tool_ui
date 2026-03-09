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
    private ProcessStateTransition _processStateTransitionCallback;

    public CapnpFbpProcessComponentModel(Point position = null)
        : base(position) { }

    public CapnpFbpProcessComponentModel(string id, Point position = null)
        : base(id, position) { }

    public IProcess Process { get; set; }

    public Process.IFactory ProcessFactory { get; set; }

    public override async Task StartProcess(ConnectionManager conMan, bool start) {
        try {
            if (
                Editor.CurrentChannelStarterService == null || ProcessFactory == null
            ) {
                return;
            }

            Console.WriteLine($"{ProcessName}: StartProcess start={start}");

            if (start) {
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
                    if (pl is not RememberCapnpPortsLinkModel rcplm) {
                        continue;
                    }

                    // deal with IN port
                    if (rcplm.InPortModel is not CapnpFbpPortModel inPort) {
                        continue;
                    }

                    // the IN port (link) is not associated with a channel yet -> create channel
                    if (
                        inPort.ReaderWriterSturdyRef == null
                        && inPort.RetrieveReaderOrWriterFromChannelTask == null
                    ) {
                        if (inPort.Parent is not CapnpFbpComponentModel m) {
                            return;
                        }

                        Console.WriteLine(
                            $"{ProcessName}: the IN port (link) is not associated with a channel yet -> create channel");
                        await Shared.Shared.CreateChannel(conMan,
                            Editor.CurrentChannelStarterService,
                            rcplm.OutPortModel,
                            inPort);
                    }

                    if (inPort.Parent == this) {
                        inPort.Connected = await Process.ConnectInPort(inPort.Name,
                            inPort.ReaderWriterSturdyRef,
                            cancelToken);
                    }

                    if (inPort.Name == "config") {
                        configInPortConnected = true;
                    }

                    //color links with connected channel green
                    rcplm.Color = inPort.Channel != null ? "#1ac12e" : "black";

                    // deal with OUT port
                    switch (rcplm.OutPortModel) {
                        case CapnpFbpPortModel outPort:
                            if (outPort.ReaderWriterSturdyRef == null) {
                                (outPort.Writer, outPort.ReaderWriterSturdyRef) =
                                    await Shared.Shared.GetNewWriterFromChannel(inPort.Channel,
                                        cancelToken);
                            }

                            if (outPort.Parent == this) {
                                outPort.Connected = await Process.ConnectOutPort(outPort.Name,
                                    outPort.ReaderWriterSturdyRef,
                                    cancelToken);
                            }

                            break;
                        case CapnpFbpIipPortModel iipPort: {
                            if (iipPort.WriterSturdyRef == null) {
                                if (iipPort.RetrieveWriterFromChannelTask != null) {
                                    Console.WriteLine($"{ProcessName}: awaiting iipPort.ChannelTask");
                                    await iipPort.RetrieveWriterFromChannelTask;
                                } else {
                                    (iipPort.Writer, iipPort.WriterSturdyRef) =
                                        await Shared.Shared.GetNewWriterFromChannel(inPort.Channel,
                                            cancelToken);
                                }

                                iipPort.Parent.Refresh();
                            }

                            break;
                        }
                    }
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

                await Process.Start(cancelToken);
                ProcessStarted = true;
                Console.WriteLine($"{ProcessName}: Process started: {ProcessStarted}");
                RefreshAll();
                RefreshLinks();
            } else // stop
            {
                if (Process != null) {
                    await Process.Stop();
                    ProcessStarted = false;
                } else {
                    await CancelAndDisposeRemoteComponent();
                }
            }
        } catch (Exception e) {
            Console.WriteLine($"{ProcessName}: Caught exception: " + e);
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
        foreach (var baseLinkModel in Links) {
            Shared.Shared.RestoreDefaultPortVisibility(Editor.Diagram, baseLinkModel);
        }

        Console.WriteLine($"{ProcessName}: CapnpFbpProcessComponentModel::Dispose");
        Task.Run(CancelAndDisposeRemoteComponent);
        FreeRemoteChannelsAttachedToPorts();
    }

    public override void FreeRemoteChannelsAttachedToPorts() {
        Console.WriteLine($"{ProcessName}: CapnpFbpProcessComponentModel::FreeRemoteChannelsAttachedToPorts");
        foreach (var port in Ports) {
            if (port is IDisposable disposable) {
                disposable.Dispose();
            }
        }

        _configInPort?.Dispose();
        _configInPort = null;
        _confIipOutPort?.Dispose();
        _confIipOutPort = null;
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