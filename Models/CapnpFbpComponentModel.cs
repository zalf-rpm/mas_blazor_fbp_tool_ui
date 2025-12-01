using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorDrawFBP.Pages;
using Mas.Infrastructure.Common;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Models;

using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

public class CapnpFbpComponentModel : NodeModel, IDisposable
{
    public CapnpFbpComponentModel(Point position = null) : base(position) {}

    public CapnpFbpComponentModel(string id, Point position = null) : base(id, position) {}

    public Editor Editor { get; set; }
    public string ComponentId { get; set; }
    public string ComponentServiceId { get; set; }
    public string ComponentName { get; set; }
    public string ProcessName { get; set; }
    public string ShortDescription { get; set; }
    public string Cmd { get; set; }
    public int InParallelCount { get; set; } = 1;
    public bool Editable { get; set; } = true;
    public static int ProcessNo { get; set; } = 0;
    public string DefaultConfigString { get; set; }

    public string ConfigString { get; set; }

    public int DisplayNoOfConfigLines { get; set; } = 3;

    public Mas.Schema.Fbp.Component.IRunnable Runnable { get; set; }

    public Mas.Schema.Fbp.Component.IRunnableFactory RunnableFactory { get; set; }

    public bool ProcessStarted { get; protected set; }

    private CancellationTokenSource _cancellationTokenSource = null;
    private CapnpFbpPortModel _configInPort = null;
    private CapnpFbpIipPortModel _confIipOutPort = null;
    private Channel<PortInfos>.IWriter _portConfigWriter = null;
    private string _portConfigReaderSr = null;

    private Task CreateTaskAndSendIip(ConnectionManager conMan, CapnpFbpIipPortModel confIipOutPort, string content,
        CancellationToken cancelToken = default)
    {
        return Task.Run(async () =>
        {
            Console.WriteLine($"{ProcessName}: async code for sending configIIP: '{ConfigString}'");
            if (confIipOutPort.Writer == null)
            {
                Console.WriteLine(
                    $"{ProcessName}: before connecting to writer for iip iipPort.ChannelTask: {confIipOutPort.ChannelTask?.IsCompletedSuccessfully}");
                confIipOutPort.Writer =
                    await conMan.Connect<Mas.Schema.Fbp.Channel<Mas.Schema.Fbp.IP>.IWriter>(
                        confIipOutPort.WriterSturdyRef);
            }
            await confIipOutPort.Writer.Write(new Channel<IP>.Msg { Value = new IP { Content = content } },
                cancelToken);
            Console.WriteLine($"{ProcessName}: sent IIP to writer");
        }, cancelToken);
    }

    public async Task StartProcess(ConnectionManager conMan, bool start)
    {
        try
        {
            if (Editor.CurrentChannelStarterService == null || RunnableFactory == null) return;

            Console.WriteLine($"{ProcessName}: StartProcess start={start}");

            if (start)
            {
                _cancellationTokenSource = new CancellationTokenSource();
                var cancelToken = _cancellationTokenSource.Token;

                //get a fresh runnable
                if (Runnable == null)
                {
                    Runnable = await RunnableFactory.Create(cancelToken);
                    if (Runnable == null) return;
                }

                List<Mas.Schema.Fbp.PortInfos.NameAndSR> inPortSRs = [];
                List<Mas.Schema.Fbp.PortInfos.NameAndSR> outPortSRs = [];
                async Task CollectPortSrs(CapnpFbpPortModel port)
                {
                    Console.WriteLine($"{ProcessName}: collecting port srs");
                    if (port.ReaderWriterSturdyRef == null && port.ChannelTask != null)
                    {
                        Console.WriteLine($"{ProcessName}: awaiting port.ChannelTask");
                        await port.ChannelTask;
                    }
                    switch (port.ThePortType)
                    {
                        case CapnpFbpPortModel.PortType.In:
                            inPortSRs.Add(new PortInfos.NameAndSR { Name = port.Name, Sr = port.ReaderWriterSturdyRef, });
                            break;
                        case CapnpFbpPortModel.PortType.Out:
                            outPortSRs.Add(new PortInfos.NameAndSR { Name = port.Name, Sr = port.ReaderWriterSturdyRef, });
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                var configInPortConnected = false;
                // collect SRs from IN and OUT ports and for IIPs send it into the channel
                foreach (var pl in Links)
                {
                    if (pl is not RememberCapnpPortsLinkModel rcplm) continue;

                    // deal with IN port
                    if (rcplm.InPortModel is not CapnpFbpPortModel inPort) continue;
                    // the IN port (link) is not associated with a channel yet -> create channel
                    if (inPort.ReaderWriterSturdyRef == null && inPort.ChannelTask == null)
                    {
                        if (inPort.Parent is not CapnpFbpComponentModel m) return;
                        Console.WriteLine($"{ProcessName}: the IN port (link) is not associated with a channel yet -> create channel");
                        await Shared.Shared.CreateChannel(conMan, Editor.CurrentChannelStarterService, rcplm.OutPortModel, inPort);
                    }
                    if (inPort.Parent == this) await CollectPortSrs(inPort);
                    if (inPort.Name == "config") configInPortConnected = true;

                    // deal with OUT port
                    switch (rcplm.OutPortModel)
                    {
                        case CapnpFbpPortModel outPort:
                            if (outPort.Parent == this) await CollectPortSrs(outPort);
                            break;
                        case CapnpFbpIipPortModel iipPort:
                        {
                            if (iipPort.Parent is not CapnpFbpIipModel iipModel) continue;
                            if (iipPort.WriterSturdyRef == null && iipPort.ChannelTask != null)
                            {
                                Console.WriteLine($"{ProcessName}: awaiting iipPort.ChannelTask");
                                await iipPort.ChannelTask;
                            }
                            if (iipPort.Writer == null)
                            {
                                Console.WriteLine(
                                    $"{ProcessName}: before connecting to writer for iipPort.ChannelTask: {iipPort.ChannelTask?.IsCompletedSuccessfully}");
                                iipPort.Writer =
                                    await conMan.Connect<Mas.Schema.Fbp.Channel<Mas.Schema.Fbp.IP>.IWriter>(
                                        iipPort.WriterSturdyRef);
                            }
                            await iipPort.Writer.Write(new Channel<IP>.Msg { Value = new IP { Content = iipModel.Content } },
                                cancelToken);
                            Console.WriteLine($"{ProcessName}: sent IIP to writer");
                            //CreateTaskAndSendIip(conMan, iipPort, iipModel.Content, cancelToken);
                            break;
                        }
                    }
                }

                //there is no config port connected, so we setup up a config channel and send the process config on the fly
                Console.WriteLine($"{ProcessName}: configInPort connected: {configInPortConnected} ConfigString: {ConfigString}");
                if (!configInPortConnected && !string.IsNullOrWhiteSpace(ConfigString))
                {
                    Console.WriteLine($"{ProcessName}: sending config on the fly");

                    //create ports, if this is the first time
                    if (_configInPort == null) _configInPort = new(null, CapnpFbpPortModel.PortType.In)
                    {
                        Name = "conf"
                    };
                    if (_confIipOutPort == null) _confIipOutPort = new(null);

                    //create channel, if not done before
                    if (_configInPort.ReaderWriterSturdyRef == null && _configInPort.ChannelTask == null)
                    {
                        Console.WriteLine($"{ProcessName}: creating config channel");
                        await Shared.Shared.CreateChannel(conMan, Editor.CurrentChannelStarterService, _confIipOutPort,
                            _configInPort);
                    }

                    Console.WriteLine($"{ProcessName}: _configInPort.RWSR: {_configInPort.ReaderWriterSturdyRef}");
                    //insert config port sturdy ref into collections for port info message later
                    await CollectPortSrs(_configInPort);

                    //now insert the current toml configuration into the config channel
                    //check if channel creation task has been finished
                    if (_confIipOutPort.WriterSturdyRef == null && _confIipOutPort.ChannelTask != null)
                    {
                        Console.WriteLine($"{ProcessName}: awaiting configIipOutPort.ChannelTask");
                        await _confIipOutPort.ChannelTask;
                    }

                    //if we didn't connect yet to the writer, do so
                    if (_confIipOutPort.Writer == null)
                    {
                        Console.WriteLine(
                            $"{ProcessName}: before connecting to writer for iipPort.ChannelTask: {_confIipOutPort.ChannelTask?.IsCompletedSuccessfully}");
                        _confIipOutPort.Writer =
                            await conMan.Connect<Mas.Schema.Fbp.Channel<Mas.Schema.Fbp.IP>.IWriter>(
                                _confIipOutPort.WriterSturdyRef);
                    }

                    //send actual config string into channel
                    await _confIipOutPort.Writer.Write(new Channel<IP>.Msg { Value = new IP { Content = ConfigString } },
                        cancelToken);
                    Console.WriteLine($"{ProcessName}: sent config IIP to config port");
                    //_configSendTask = CreateTaskAndSendIip(conMan, _confIipOutPort, ConfigString, cancelToken);
                }

                //start temporary port info channel and send port infos to component
                if (_portConfigWriter == null)
                {
                    Console.WriteLine($"{ProcessName}: Trying to start port info channel");
                    var si = await Editor.CurrentChannelStarterService.Start(new StartChannelsService.Params
                    {
                        Name = $"config_{ProcessName}"
                    }, cancelToken);
                    Console.WriteLine($"{ProcessName}: Port info channel started si.Count={si.Item1.Count}, si[0].ReaderSRs.Count={si.Item1[0].ReaderSRs.Count}, si[0].WriterSRs.Count={si.Item1[0].WriterSRs.Count}");
                    if (si.Item1.Count == 0 || si.Item1[0].ReaderSRs.Count == 0 || si.Item1[0].WriterSRs.Count == 0)
                    {
                        return;
                    }
                    _portConfigReaderSr = si.Item1[0].ReaderSRs[0];
                    _portConfigWriter = (si.Item1[0].Writers[0] as Channel<object>.Writer_Proxy)
                        ?.Cast<Channel<PortInfos>.IWriter>(false);
                }
                if (_portConfigWriter == null || string.IsNullOrEmpty(_portConfigReaderSr)) return;

                ProcessStarted = await Runnable.Start(_portConfigReaderSr, ProcessName);
                if (!ProcessStarted) return;
                Console.WriteLine($"{ProcessName}: Runnable started: {ProcessStarted}");
                Console.WriteLine($"{ProcessName}: Writing port infos to port info channel");
                Console.WriteLine($"{ProcessName}: inPortSRs: {inPortSRs} outPortSRs: {outPortSRs}");
                await _portConfigWriter.Write(new Channel<PortInfos>.Msg
                {
                    Value = new PortInfos
                    {
                        InPorts = inPortSRs,
                        OutPorts = outPortSRs,
                    }
                }, cancelToken);
                Console.WriteLine($"{ProcessName}: Wrote port infos to port info channel");
                //don't close writer to reuse channel for further restarts
                //close writer
                //await writer.Close(cancelToken);
            }
            else // stop
            {
                ProcessStarted = await CancelAndDisposeRemoteComponent();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"{ProcessName}: Caught exception: " + e);
        }
    }

    public async Task<bool> CancelAndDisposeRemoteComponent()
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpComponentModel::CancelAndDisposeRemoteComponent");

        if (Runnable == null) return false;
        //cancel task
        if (_cancellationTokenSource != null) await _cancellationTokenSource.CancelAsync();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        //stop remote process
        Console.WriteLine(
            $"{ProcessName}: CapnpFbpComponentModel::CancelAndDisposeRemoteComponent stopping runnable");
        var success = await Runnable.Stop();
        Console.WriteLine(
            $"{ProcessName}: CapnpFbpComponentModel::CancelAndDisposeRemoteComponent stopped runnable");
        Runnable.Dispose();
        Runnable = null;
        return success;
    }

    public void FreeRemoteChannelsAttachedToPorts()
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpComponentModel::FreeRemoteChannelsAttachedToPorts");
        foreach (var port in Ports)
        {
            if (port is IDisposable disposable) disposable.Dispose();
        }
        _configInPort?.Dispose();
        _configInPort = null;
        _confIipOutPort?.Dispose();
        _confIipOutPort = null;
    }

    public void Dispose()
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpComponentModel::Dispose");
        Task.Run(CancelAndDisposeRemoteComponent);
        RunnableFactory?.Dispose();
        FreeRemoteChannelsAttachedToPorts();
        _portConfigWriter?.Dispose();
    }
}