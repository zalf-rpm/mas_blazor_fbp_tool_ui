using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mas.Infrastructure.Common;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Models;

using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

public class CapnpFbpComponentModel : NodeModel
{
    public CapnpFbpComponentModel(Point position = null) : base(position) {}
    
    // the id of component
    public string ComponentId { get; set; }
    public string ComponentName { get; set; }
    public string ProcessName { get; set; }
    public string ShortDescription { get; set; }
    public string Cmd { get; set; }

    public int InParallelCount { get; set; } = 1;
    
    public bool Editable { get; set; } = true;
    
    public static int ProcessNo { get; set; } = 0;
    
    // if null PathToFile is a standalone executable, else a script needing the interpreter
    //public string PathToInterpreter { get; set; } = null;
    
    // public struct CmdParam
    // {
    //     public string Name { get; set; }
    //     public string Value { get; set; }
    // }
    //
    // public void AddEmptyCmdParam()
    // {
    //     CmdParameters.Add(new CmdParam { Name = "", Value = "" });
    // }
    // public readonly List<CmdParam> CmdParameters = new();
    public string DefaultConfigString { get; set; }

    public Mas.Schema.Fbp.Component.IRunnable Runnable { get; set; }
    public Mas.Schema.Fbp.IStartChannelsService ChannelStarterService { get; set; }

    public async Task<bool> StartProcess(ConnectionManager conMan, bool start)
    {
        var processStarted = false;
        try
        {
            if (ChannelStarterService == null || Runnable == null) return false;

            Console.WriteLine($"{ProcessName}: StartProcess start={start}");

            if (start)
            {
                List<Mas.Schema.Fbp.PortInfos.NameAndSR> inPortSRs = [];
                List<Mas.Schema.Fbp.PortInfos.NameAndSR> outPortSRs = [];
                async Task CollectPortSrs(CapnpFbpPortModel port)
                {
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
                    }
                }

                // collect SRs from IN and OUT ports and for IIPs send it into the channel
                foreach (var pl in Links)
                {
                    if (pl is not RememberCapnpPortsLinkModel rcplm) continue;

                    // deal with IN port
                    if (rcplm.InPortModel is not CapnpFbpPortModel inPort) continue;
                    // the IN port (link) is not associated with a channel yet -> create channel
                    if (inPort.ReaderWriterSturdyRef == null && inPort.ChannelTask == null)
                    {
                        if (inPort.Parent is not CapnpFbpComponentModel m) return false;
                        await Shared.Shared.CreateChannel(conMan, m.ChannelStarterService, rcplm.OutPortModel, inPort);
                    }
                    if (inPort.Parent == this) await CollectPortSrs(inPort);

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

                            Task.Run(async () =>
                            {
                                var content = iipModel.Content;
                                Console.WriteLine($"{ProcessName}: before connecting to writer for iip iipPort.ChannelTask: {iipPort.ChannelTask?.IsCompletedSuccessfully}");
                                iipPort.Writer = await conMan.Connect<Mas.Schema.Fbp.Channel<Mas.Schema.Fbp.IP>.IWriter>(iipPort.WriterSturdyRef);
                                await iipPort.Writer.Write(new Channel<IP>.Msg { Value = new IP { Content = content } });
                                Console.WriteLine($"{ProcessName}: after connecting to writer for iip");
                            });
                            break;
                        }
                    }
                }

                //start temporary port info channel and send port infos to component
                Console.WriteLine($"{ProcessName}: Trying to start port info channel");
                var si = await ChannelStarterService.Create(new StartChannelsService.Params
                {
                    Name = $"config_{ProcessName}"
                });
                Console.WriteLine($"{ProcessName}: Port info channel started si.Count={si.Count}, si[0].ReaderSRs.Count={si[0].ReaderSRs.Count}, si[0].WriterSRs.Count={si[0].WriterSRs.Count}");
                if (si.Count == 0 || si[0].ReaderSRs.Count == 0 || si[0].WriterSRs.Count == 0) return false;
                processStarted = await Runnable.Start(si[0].ReaderSRs[0], ProcessName);
                if (!processStarted) return false;
                Console.WriteLine($"{ProcessName}: Runnable started: {processStarted}");
                using var writer = await conMan.Connect<Mas.Schema.Fbp.Channel<Mas.Schema.Fbp.PortInfos>.IWriter>(si[0].WriterSRs[0]);
                await writer.Write(new Channel<PortInfos>.Msg
                {
                    Value = new PortInfos
                    {
                        InPorts = inPortSRs,
                        OutPorts = outPortSRs,
                    }
                });
                Console.WriteLine($"{ProcessName}: Wrote port infos to port info channel");
                //close writer
                await writer.Close();

                //close port infos channel
                using var channel = await conMan.Connect<Mas.Schema.Fbp.IChannel<Mas.Schema.Fbp.PortInfos>>(si[0].ChannelSR);
                await channel.Close(false);
            }
            else // stop
            {
                processStarted = await Runnable.Stop();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"{ProcessName}: Caught exception: " + e);
        }

        return processStarted;
    }


}