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

public class CapnpFbpRunnableComponentModel : CapnpFbpComponentModel
{
    public CapnpFbpRunnableComponentModel(Point position = null)
        : base(position) { }

    public CapnpFbpRunnableComponentModel(string id, Point position = null)
        : base(id, position) { }

    private CancellationTokenSource _cancellationTokenSource;
    private CapnpFbpInPortModel _configInPort;
    private CapnpFbpOutPortModel _confIipOutPort;
    private SturdyRef _portInfosReaderSr;
    private Channel<PortInfos>.IWriter _portInfosWriter;

    public IRunnable Runnable { get; set; }

    public Runnable.IFactory RunnableFactory { get; set; }

    public override bool RemoteProcessAttached() => Runnable != null;

    public override async Task StartProcess(ConnectionManager conMan)
    {
        try
        {
            if (
                Editor.CurrentChannelStarterService == null
                || RunnableFactory == null
                || ProcessStarted
            )
            {
                return;
            }

            Console.WriteLine($"T{Environment.CurrentManagedThreadId} {ProcessName}: StartProcess");

            _cancellationTokenSource = new CancellationTokenSource();
            var cancelToken = _cancellationTokenSource.Token;

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
            List<PortInfos.NameAndSR> outPortSRs = [];

            async Task CollectPortSrs(CapnpFbpPortModel port, IChannel<IP> channel = null)
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
                        inPortSRs.Add(
                            new PortInfos.NameAndSR
                            {
                                Name = inPort.Name,
                                Sr = inPort.ReaderSturdyRef,
                            }
                        );
                        break;
                    case CapnpFbpOutPortModel outPort:
                        // if there is no SR
                        if (outPort.WriterSturdyRef == null)
                        {
                            // but we have a task to wait for that SR
                            if (outPort.RetrieveWriterFromChannelTask != null)
                            {
                                Console.WriteLine(
                                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: awaiting outPort.ChannelTask"
                                );
                                await outPort.RetrieveWriterFromChannelTask;
                            }
                            else
                            {
                                // no task, the outPort must have been updated and we get a new writer for that out port
                                (outPort.Writer, outPort.WriterSturdyRef) =
                                    await Shared.Shared.GetNewWriterFromChannel(
                                        channel,
                                        cancelToken
                                    );
                            }
                        }
                        outPortSRs.Add(
                            new PortInfos.NameAndSR
                            {
                                Name = outPort.Name,
                                Sr = outPort.WriterSturdyRef,
                            }
                        );
                        break;
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
                    await Shared.Shared.CreateChannel(
                        conMan,
                        Editor.CurrentChannelStarterService,
                        outPort,
                        inPort
                    );
                }

                if (inPort.Parent == this)
                {
                    await CollectPortSrs(inPort);
                }

                if (inPort.Name == "config")
                {
                    configInPortConnected = true;
                }

                //color links with connected channel green
                rcplm.Color = inPort.Channel != null ? "#1ac12e" : "black";

                // deal with OUT port
                if (outPort.WriterSturdyRef == null)
                {
                    if (outPort.RetrieveWriterFromChannelTask != null)
                    {
                        Console.WriteLine(
                            $"T{Environment.CurrentManagedThreadId} {ProcessName}: awaiting outPort.ChannelTask"
                        );
                        await outPort.RetrieveWriterFromChannelTask;
                    }
                    else
                    {
                        (outPort.Writer, outPort.WriterSturdyRef) =
                            await Shared.Shared.GetNewWriterFromChannel(
                                inPort.Channel,
                                cancelToken
                            );
                    }
                }

                if (outPort.Parent == this)
                {
                    await CollectPortSrs(outPort, inPort.Channel);
                }
                outPort.Parent.Refresh();
            }

            //there is no config port connected, so we setup up a config channel and send the process config on the fly
            Console.WriteLine(
                $"{ProcessName}: configInPort connected: {configInPortConnected} ConfigString: {ConfigString}"
            );
            if (!configInPortConnected && !string.IsNullOrWhiteSpace(ConfigString))
            {
                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: sending config on the fly"
                );

                //create ports, if this is the first time
                if (_configInPort == null)
                {
                    _configInPort = new CapnpFbpInPortModel(null) { Name = "conf" };
                }

                if (_confIipOutPort == null)
                {
                    _confIipOutPort = new CapnpFbpOutPortModel(null);
                }

                //create channel, if not done before
                if (
                    _configInPort.ReaderSturdyRef == null
                    && _configInPort.RetrieveReaderFromChannelTask == null
                )
                {
                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: creating config channel"
                    );
                    await Shared.Shared.CreateChannel(
                        conMan,
                        Editor.CurrentChannelStarterService,
                        _confIipOutPort,
                        _configInPort
                    );
                }

                Console.WriteLine(
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: _configInPort.RWSR: {_configInPort.ReaderSturdyRef}"
                );
                //insert config port sturdy ref into collections for port info message later
                await CollectPortSrs(_configInPort);

                //now insert the current toml configuration into the config channel
                //check if channel creation task has been finished
                if (_confIipOutPort.WriterSturdyRef == null)
                {
                    if (_confIipOutPort.RetrieveWriterFromChannelTask != null)
                    {
                        Console.WriteLine(
                            $"T{Environment.CurrentManagedThreadId} {ProcessName}: awaiting configIipOutPort.ChannelTask"
                        );
                        await _confIipOutPort.RetrieveWriterFromChannelTask;
                    }
                    else
                    {
                        (_confIipOutPort.Writer, _confIipOutPort.WriterSturdyRef) =
                            await Shared.Shared.GetNewWriterFromChannel(
                                _configInPort.Channel,
                                cancelToken
                            );
                    }
                }

                //if we didn't connect yet to the writer, do so
                if (_confIipOutPort.Writer == null)
                {
                    Debug.Assert(
                        _confIipOutPort.Writer != null,
                        "Here we should already have a writer, so no need to connect."
                    );
                    Console.WriteLine(
                        $"T{Environment.CurrentManagedThreadId} {ProcessName}: before connecting to writer for iipPort.ChannelTask: {_confIipOutPort.RetrieveWriterFromChannelTask?.IsCompletedSuccessfully}"
                    );
                    _confIipOutPort.Writer = await conMan.Connect<Channel<IP>.IWriter>(
                        _confIipOutPort.WriterSturdyRef
                    );
                }

                //send actual config string into channel
                await _confIipOutPort.Writer.Write(
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
                    $"T{Environment.CurrentManagedThreadId} {ProcessName}: sent config IIP to config port"
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
                    new StartChannelsService.Params { Name = $"config_{ProcessName}" },
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
            }

            if (_portInfosWriter == null)
            {
                return;
            }

            ProcessStarted = await Runnable.Start(_portInfosReaderSr, ProcessName, null);
            if (!ProcessStarted)
            {
                return;
            }

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
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: Wrote port infos to port info channel"
            );
            //don't close writer to reuse channel for further restarts
            //close writer
            //await writer.Close(cancelToken);
            RefreshAll();
            RefreshLinks();
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::StartProcess: Caught exception: {e}"
            );
        }
    }

    public override async Task StopProcess(ConnectionManager conMan)
    {
        try
        {
            await CancelAndDisposeRemoteComponent();
            ProcessStarted = false;
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::StopProcess: Caught exception: {e}"
            );
        }
    }

    public override async Task CancelAndDisposeRemoteComponent()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::CancelAndDisposeRemoteComponent"
        );

        if (Runnable == null)
        {
            return;
        }

        //cancel task
        if (_cancellationTokenSource != null)
        {
            await _cancellationTokenSource.CancelAsync();
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        //stop remote process
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::CancelAndDisposeRemoteComponent stopping runnable/process"
        );
        await _portInfosWriter.Write(
            new Channel<PortInfos>.Msg { which = Channel<PortInfos>.Msg.WHICH.Done }
        );
        await Task.Delay(500); // let the process stop
        ProcessStarted = !(await Runnable.Stop());

        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::CancelAndDisposeRemoteComponent stopped runnable/process (ProcessStarted: {ProcessStarted})"
        );
        Runnable?.Dispose();
        Runnable = null;
    }

    public override void Dispose()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::Dispose"
        );
        base.Dispose();
        RunnableFactory?.Dispose();
        _portInfosWriter?.Dispose();
    }

    public override void FreeRemoteChannelsAttachedToPorts()
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: CapnpFbpRunnableComponentModel::FreeRemoteChannelsAttachedToPorts"
        );
        base.FreeRemoteChannelsAttachedToPorts();

        _configInPort?.Dispose();
        _configInPort = null;
        _confIipOutPort?.Dispose();
        _confIipOutPort = null;
    }
}
