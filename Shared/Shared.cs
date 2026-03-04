using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using Capnp;
using Mas.Infrastructure.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Mas.Schema.Registry;
using Exception = System.Exception;

namespace BlazorDrawFBP.Shared;

public class Shared
{
    public static readonly ulong ChannelStarterInterfaceId =
        typeof(IStartChannelsService).GetCustomAttribute<TypeIdAttribute>(false)?.Id ?? 0;

    public static readonly ulong RegistryInterfaceId =
        typeof(IRegistry).GetCustomAttribute<TypeIdAttribute>(false)?.Id ?? 0;

    public static ulong GetInterfaceId<T>()
        where T : class
    {
        var x = typeof(T);
        return typeof(T).GetCustomAttribute<TypeIdAttribute>(false)?.Id ?? 0;
    }

    public static string MakeUniqueKey<T>(Dictionary<string, T> dict, string key)
    {
        var key2 = key;
        while (dict.ContainsKey(key))
        {
            if (!int.TryParse(key2[^1..^1], out var i))
                key += 2;
            if (i < 9)
            {
                key2 = key2[..^1] + (i + 1);
            }
            else
            {
                if (int.TryParse(key2[^2..^1], out var i2))
                    key2 = key2[..^2] + (i2 + 1);
            }
        }

        return key2;
    }

    public static string NodeNameFromPort(PortModel port)
    {
        return port.Parent switch
        {
            CapnpFbpComponentModel m => m.ProcessName,
            CapnpFbpViewComponentModel m2 => m2.ProcessName,
            CapnpFbpIipModel m2 => m2.Id,
            _ => "unknown_process",
        };
    }

    public static string PortName(PortModel port)
    {
        return port switch
        {
            CapnpFbpPortModel p => p.Name,
            CapnpFbpIipPortModel => "IIP",
            _ => "unknown_port",
        };
    }

    public static async Task<(Channel<IP>.IWriter, SturdyRef)> GetNewWriterFromChannel(
        IChannel<IP> channel,
        CancellationToken cancelToken = default
    )
    {
        var w = await channel.Writer(cancelToken);
        var sr = (await w.Save(null, cancelToken)).SturdyRef;
        return (w, sr);
    }

    public static Task CreateChannel(
        ConnectionManager conMan,
        IStartChannelsService css,
        PortModel outPort,
        CapnpFbpPortModel inPort
    )
    {
        if (css == null)
            return Task.CompletedTask;

        var t = Task.Run(async () =>
        {
            if (inPort.Channel == null) // there is no channel for the IN port yet
            {
                var si = await css.Start(
                    new StartChannelsService.Params
                    {
                        Name =
                            $"{NodeNameFromPort(outPort)}.{PortName(outPort)}->"
                            + $"{NodeNameFromPort(inPort)}.{PortName(inPort)}",
                    }
                );
                if (
                    si.Item1.Count <= 0
                    || si.Item1[0].ReaderSRs.Count <= 0
                    || si.Item1[0].WriterSRs.Count <= 0
                )
                    return;
                switch (outPort)
                {
                    case CapnpFbpPortModel sPort:
                        sPort.ReaderWriterSturdyRef = si.Item1[0].WriterSRs[0];
                        sPort.Writer = (
                            si.Item1[0].Writers[0] as Channel<object>.Writer_Proxy
                        )?.Cast<Channel<IP>.IWriter>(false);
                        sPort.RetrieveReaderOrWriterFromChannelTask = null;
                        sPort.Parent.Refresh();
                        break;
                    case CapnpFbpIipPortModel iipPort:
                        iipPort.WriterSturdyRef = si.Item1[0].WriterSRs[0];
                        iipPort.Writer = (
                            si.Item1[0].Writers[0] as Channel<object>.Writer_Proxy
                        )?.Cast<Channel<IP>.IWriter>(false);
                        iipPort.RetrieveWriterFromChannelTask = null;
                        iipPort.Parent?.Refresh();
                        break;
                }

                inPort.ReaderWriterSturdyRef = si.Item1[0].ReaderSRs[0];
                inPort.Reader = (
                    si.Item1[0].Readers[0] as Channel<object>.Reader_Proxy
                )?.Cast<Channel<IP>.IReader>(false);
                // attach channel cap to IN port (target port)
                inPort.Channel = (si.Item1[0].Channel as Channel_Proxy<object>)?.Cast<IChannel<IP>>(
                    false
                );
                // attach stop channel cap to IN port
                inPort.StopChannel = si.Item2;
                inPort.RetrieveReaderOrWriterFromChannelTask = null;
                inPort.Parent?.Refresh();
            }
            else
            {
                Console.WriteLine("CreateChannel: inPort.channel was not null");
                throw new Exception("CreateChannel: inPort.channel was null");
                var writerSr = (await inPort.Channel.Writer().Result.Save(null)).SturdyRef;
                switch (outPort)
                {
                    case CapnpFbpPortModel sPort:
                        sPort.ReaderWriterSturdyRef = writerSr;
                        break;
                    case CapnpFbpIipPortModel iipPort:
                        iipPort.WriterSturdyRef = writerSr;
                        break;
                }

                Debug.Assert(inPort.ReaderWriterSturdyRef != null);
            }
        });
        switch (outPort)
        {
            case CapnpFbpPortModel sPort:
                sPort.RetrieveReaderOrWriterFromChannelTask = t;
                break;
            case CapnpFbpIipPortModel iipPort:
                iipPort.RetrieveWriterFromChannelTask = t;
                break;
        }

        inPort.RetrieveReaderOrWriterFromChannelTask = t;
        return t;
    }

    public static void RestoreDefaultPortVisibility(Diagram diagram, BaseLinkModel baseLinkModel)
    {
        if (baseLinkModel.Source.Model is NodeModel sourceNode)
        {
            foreach (var p in sourceNode.Ports)
            {
                if (
                    p is CapnpFbpPortModel { ThePortType: CapnpFbpPortModel.PortType.Out } ocp
                    && ocp.Name == baseLinkModel.Labels.First().Content
                )
                {
                    ocp.Visibility = CapnpFbpPortModel.VisibilityState.Visible;
                }
            }

            if (sourceNode is CapnpFbpIipModel { Links.Count: 1 } iipModel)
            {
                foreach (var p in iipModel.Ports)
                {
                    p.Visible = true;
                }
            }

            sourceNode.RefreshAll();
        }

        if (baseLinkModel.Target.Model is NodeModel targetNode)
        {
            var noOfLinksToInPort = diagram.Links.Count(l =>
                l.Target.Model == targetNode
                && l.Labels.Last().Content == baseLinkModel.Labels.Last().Content
            );

            if (noOfLinksToInPort == 1)
            {
                foreach (var p in targetNode.Ports)
                {
                    var inLabel = baseLinkModel
                        .Labels.FindAll(blm => blm is not ChannelLinkLabelModel)
                        .LastOrDefault();
                    if (
                        p is CapnpFbpPortModel { ThePortType: CapnpFbpPortModel.PortType.In } ocp
                        && ocp.Name == inLabel?.Content
                    )
                    {
                        ocp.Visibility = CapnpFbpPortModel.VisibilityState.Visible;
                        //don't delete channels on link removal
                        //as they are attached to the ports and
                        //should be deleted when a component gets deleted
                        //ocp.FreeRemoteChannelResources();
                    }
                }
            }

            targetNode.RefreshAll();
        }
    }
}
