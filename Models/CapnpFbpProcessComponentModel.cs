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
using Tomlyn;
using Process = Mas.Schema.Fbp.Process;

namespace BlazorDrawFBP.Models;

public class CapnpFbpProcessComponentModel : CapnpFbpComponentModel
{
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

    public override async Task StartProcess(ConnectionManager conMan)
    {
        try
        {
            if (
                Editor.CurrentChannelStarterService == null
                || ProcessFactory == null
                || ProcessStarted
                || _processStartTask != null
            )
            {
                return;
            }

            Console.WriteLine($"T{Environment.CurrentManagedThreadId} {ProcessName}: StartProcess");

            _cancellationTokenSource = new CancellationTokenSource();
            var cancelToken = _cancellationTokenSource.Token;

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
                            switch (@new)
                            {
                                case Mas.Schema.Fbp.Process.State.started:
                                    ProcessStarted = true;
                                    Refresh();
                                    break;
                                case Mas.Schema.Fbp.Process.State.canceled:
                                    break;
                                case Mas.Schema.Fbp.Process.State.stopped:
                                    ProcessStarted = false;
                                    Refresh();
                                    break;
                            }
                        }
                    );
                    await Process.State(_processStateTransitionCallback, cancelToken);
                }
            }

            var configInPortConnected = false;
            // collect SRs from IN and OUT ports and for IIPs send it into the channel
            foreach (var pl in Links)
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

                //color links with connected channel green
                rcplm.Color = inPort.Channel != null ? "#1ac12e" : "black";

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
                                            JTokenType.Array => MakeCommonValue(t),
                                            JTokenType.Object => MakeCommonValue(t),
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
                                                JTokenType.Array => MakeCommonValue(v),
                                                JTokenType.Object => MakeCommonValue(v),
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
                    //if (val != null)
                    await Process.SetConfigEntry(
                        new Process.ConfigEntry { Name = kv.Key, Val = val },
                        cancelToken
                    );
                }

                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: set full config"
                );
            }

            //await Process.Start(cancelToken);
            _processStartTask = Task.Run(async () => await Process.Start(cancelToken), cancelToken)
                .ContinueWith((r) => _processStartTask = null);
            ProcessStarted = true;
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: Process started: {ProcessStarted}"
            );
            RefreshAll();
            RefreshLinks();
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::StartProcess: Caught exception: {e}"
            );
        }
    }

    public override async Task StopProcess(ConnectionManager conMan)
    {
        try
        {
            if (Process != null && _processStopTask == null)
            {
                //await Process.Stop();
                _processStopTask = Task.Run(
                        async () => await Process.Stop(),
                        _cancellationTokenSource.Token
                    )
                    .ContinueWith((r) => _processStopTask = null);
                ProcessStarted = false;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::StopProcess: Caught exception: {e}"
            );
        }
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

        if (Process == null)
            return;

        //cancel task
        if (_cancellationTokenSource != null)
            await _cancellationTokenSource.CancelAsync();

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        //stop remote process
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::CancelAndDisposeRemoteComponent stopping runnable/process"
        );
        await Process.Stop();
        ProcessStarted = false;

        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpProcessComponentModel::CancelAndDisposeRemoteComponent stopped runnable/process (ProcessStarted: {ProcessStarted})"
        );
        Process?.Dispose();
        Process = null;
        _processStateTransitionCallback?.Dispose();
        _processStateTransitionCallback = null;
    }

    private class ProcessStateTransition(Action<Process.State, Process.State> action)
        : Process.IStateTransition
    {
        public Task StateChanged(
            Process.State old,
            Process.State @new,
            CancellationToken cancellationToken = default
        )
        {
            action(old, @new);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}
