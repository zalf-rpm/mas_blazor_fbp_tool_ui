using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Capnp;
using Mas.Infrastructure.Common;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Models;

using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

public class CapnpFbpViewComponentModel : CapnpFbpComponentModel
{
    public CapnpFbpViewComponentModel(Point position = null) : base(position) {}

    public CapnpFbpViewComponentModel(string id, Point position = null) : base(id, position) {}

    public Task ViewMsgReceiveTask { get; set; }

    public override async Task StartProcess(ConnectionManager conMan, bool start)
    {
        try
        {
            if (ChannelStarterService == null) return;

            Console.WriteLine($"{ProcessName}: StartProcess start={start}");

            if (start)
            {
                // List<Mas.Schema.Fbp.PortInfos.NameAndSR> inPortSRs = [];
                // List<Mas.Schema.Fbp.PortInfos.NameAndSR> outPortSRs = [];
                // async Task CollectPortSrs(CapnpFbpPortModel port)
                // {
                //     Console.WriteLine($"{ProcessName}: collecting port srs");
                //     if (port.ReaderWriterSturdyRef == null && port.ChannelTask != null)
                //     {
                //         Console.WriteLine($"{ProcessName}: awaiting port.ChannelTask");
                //         await port.ChannelTask;
                //     }
                //     switch (port.ThePortType)
                //     {
                //         case CapnpFbpPortModel.PortType.In:
                //             inPortSRs.Add(new PortInfos.NameAndSR { Name = port.Name, Sr = port.ReaderWriterSturdyRef, });
                //             break;
                //         case CapnpFbpPortModel.PortType.Out:
                //             outPortSRs.Add(new PortInfos.NameAndSR { Name = port.Name, Sr = port.ReaderWriterSturdyRef, });
                //             break;
                //         default:
                //             throw new ArgumentOutOfRangeException();
                //     }
                // }

                Channel<IP>.IReader reader = null;

                // collect SRs from IN and OUT ports and for IIPs send it into the channel
                foreach (var pl in Links)
                {
                    if (pl is not RememberCapnpPortsLinkModel rcplm) continue;

                    // deal with IN port
                    if (rcplm.InPortModel is not CapnpFbpPortModel inPort) continue;
                    // the IN port (link) is not associated with a channel yet -> create channel
                    if (inPort.ReaderWriterSturdyRef == null && inPort.ChannelTask == null)
                    {
                        if (inPort.Parent is not CapnpFbpViewComponentModel m) return;
                        Console.WriteLine($"{ProcessName}: the IN port (link) is not associated with a channel yet -> create channel");
                        await Shared.Shared.CreateChannel(conMan, m.ChannelStarterService, rcplm.OutPortModel, inPort);
                    }
                    //if (inPort.Parent == this) await CollectPortSrs(inPort);
                    if (inPort.Parent == this) reader = Capnp.Rpc.Proxy.Share(inPort.Reader);

                    // deal with OUT port
                    switch (rcplm.OutPortModel)
                    {
                        case CapnpFbpPortModel outPort:
                            //if (outPort.Parent == this) await CollectPortSrs(outPort);
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
                                Console.WriteLine($"{ProcessName}: async code for sending IIP: '{content}'");
                                if (iipPort.Writer == null)
                                {
                                    Console.WriteLine(
                                        $"{ProcessName}: before connecting to writer for iip iipPort.ChannelTask: {iipPort.ChannelTask?.IsCompletedSuccessfully}");
                                    iipPort.Writer =
                                        await conMan.Connect<Mas.Schema.Fbp.Channel<Mas.Schema.Fbp.IP>.IWriter>(
                                            iipPort.WriterSturdyRef);
                                }
                                await iipPort.Writer.Write(new Channel<IP>.Msg { Value = new IP { Content = content } });
                                Console.WriteLine($"{ProcessName}: sent IIP to writer");
                            });
                            break;
                        }
                    }
                }

                //start temporary port info channel and send port infos to component
                // Console.WriteLine($"{ProcessName}: Trying to start port info channel");
                // var si = await ChannelStarterService.Start(new StartChannelsService.Params
                // {
                //     Name = $"config_{ProcessName}"
                // });
                // Console.WriteLine($"{ProcessName}: Port info channel started si.Count={si.Item1.Count}, si[0].ReaderSRs.Count={si.Item1[0].ReaderSRs.Count}, si[0].WriterSRs.Count={si.Item1[0].WriterSRs.Count}");
                // if (si.Item1.Count == 0 || si.Item1[0].ReaderSRs.Count == 0 || si.Item1[0].WriterSRs.Count == 0) return;
                ViewMsgReceiveTask = Task.Run(async () =>
                {
                    Console.WriteLine($"{ProcessName}: starting view's receive loop");
                    var leave = false;
                    while (!leave)
                    {
                        Console.WriteLine($"{ProcessName}: trying to read msg");
                        var msg = await reader.Read();
                        Console.WriteLine($"{ProcessName}: read msg: {msg}");
                        switch (msg.which)
                        {
                            case Channel<IP>.Msg.WHICH.Done:
                                Console.WriteLine($"{ProcessName}: received done msg");
                                leave = true;
                                break;
                            case Channel<IP>.Msg.WHICH.Value:
                                Console.WriteLine($"{ProcessName}: received value msg");
                                if (msg.Value.Content is DeserializerState ds)
                                {
                                    var str = CapnpSerializable.Create<string>(ds);
                                    if (str != null)
                                    {
                                        Console.WriteLine($"{ProcessName}: received msg: '{str}'");
                                        ConfigString += str;
                                        ConfigString += "\n";
                                    }
                                    Refresh();
                                }
                                break;
                            case Channel<IP>.Msg.WHICH.NoMsg:
                                Console.WriteLine($"{ProcessName}: received noMsg msg");
                                break;
                            case Channel<IP>.Msg.WHICH.undefined:
                                Console.WriteLine($"{ProcessName}: received undefined msg");
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                    Console.WriteLine($"{ProcessName}: left view's receive loop");
                });

                ProcessStarted = !ViewMsgReceiveTask.IsFaulted;
            }
            else // stop
            {
                ViewMsgReceiveTask.Dispose();
                ProcessStarted = false;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"{ProcessName}: Caught exception: " + e);
        }
    }

    public new void Dispose()
    {
        base.Dispose();
        ViewMsgReceiveTask.Dispose();
    }
}