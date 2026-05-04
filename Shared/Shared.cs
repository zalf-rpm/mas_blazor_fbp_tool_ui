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
using BlazorDrawFBP.Pages;
using Capnp;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Mas.Schema.Registry;
using Microsoft.AspNetCore.Components;
using MudBlazor.Extensions;
using Exception = System.Exception;

namespace BlazorDrawFBP.Shared;

public static class Shared
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
            CapnpFbpIipComponentModel m2 => m2.Id,
            _ => "unknown_process",
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
        CapnpFbpOutPortModel outPort,
        CapnpFbpInPortModel inPort
    ) => CreateChannel(conMan, css, outPort, inPort, null);

    public static Task CreateChannel(
        ConnectionManager conMan,
        IStartChannelsService css,
        RememberCapnpPortsLinkModel link
    ) => CreateChannel(conMan, css, link.OutPortModel, link.InPortModel, link);

    private static Task CreateChannel(
        ConnectionManager conMan,
        IStartChannelsService css,
        CapnpFbpOutPortModel outPort,
        CapnpFbpInPortModel inPort,
        RememberCapnpPortsLinkModel link
    )
    {
        if (css == null)
            return Task.CompletedTask;

        if (inPort.Channel != null)
            return Task.CompletedTask;

        var t = Task.Run(async () =>
        {
            try
            {
                var si = await css.Start(
                    new StartChannelsService.Params
                    {
                        Name =
                            $"{NodeNameFromPort(outPort)}.{outPort.Name}->"
                            + $"{NodeNameFromPort(inPort)}.{inPort.Name}",
                    }
                );
                if (
                    si.Item1.Count <= 0
                    || si.Item1[0].ReaderSRs.Count <= 0
                    || si.Item1[0].WriterSRs.Count <= 0
                )
                    return;

                var writerSturdyRef = si.Item1[0].WriterSRs[0];
                var writer = (
                    si.Item1[0].Writers[0] as Channel<object>.Writer_Proxy
                )?.Cast<Channel<IP>.IWriter>(false);

                if (link != null)
                {
                    link.SetWriter(writer, writerSturdyRef);
                }
                else
                {
                    outPort.WriterSturdyRef = writerSturdyRef;
                    outPort.Writer = writer;
                }
                outPort.Parent?.Refresh();

                inPort.ReaderSturdyRef = si.Item1[0].ReaderSRs[0];
                inPort.Reader = (
                    si.Item1[0].Readers[0] as Channel<object>.Reader_Proxy
                )?.Cast<Channel<IP>.IReader>(false);
                // attach channel cap to IN port (target port)
                inPort.Channel = (si.Item1[0].Channel as Channel_Proxy<object>)?.Cast<IChannel<IP>>(
                    false
                );
                if (outPort.Parent != null && inPort.Parent != null && inPort.Channel != null)
                {
                    await inPort.Channel.SetAutoCloseSemantics(Channel<IP>.CloseSemantics.no);
                }
                // attach stop channel cap to IN port
                inPort.StopChannel = si.Item2;
                inPort.Parent?.Refresh();

                // var ms = Random.Shared.Next(2) * 1000;
                // and receive status information from channel
                await inPort.ReceiveChannelStats(2000); //(uint)ms);
            }
            finally
            {
                if (link != null)
                    link.RetrieveWriterFromChannelTask = null;
                else
                    outPort.RetrieveWriterFromChannelTask = null;

                inPort.RetrieveReaderFromChannelTask = null;
            }
        });

        if (link != null)
            link.RetrieveWriterFromChannelTask = t;
        else
            outPort.RetrieveWriterFromChannelTask = t;

        inPort.RetrieveReaderFromChannelTask = t;
        return t;
    }

    public static void RestoreDefaultPortVisibility(Diagram diagram, BaseLinkModel baseLinkModel)
    {
        var outPort = GetOutPort(baseLinkModel);
        var inPort = GetInPort(baseLinkModel);

        outPort?.SyncVisibility(baseLinkModel);
        inPort?.SyncVisibility(baseLinkModel);

        if (outPort?.Parent is CapnpFbpIipComponentModel iipModel)
        {
            foreach (var p in iipModel.Ports)
            {
                p.Visible = true;
            }
        }

        outPort?.Parent?.RefreshAll();
        inPort?.Parent?.RefreshAll();
    }

    public static async Task ResetNodeLifecycleAsync(Model node)
    {
        switch (node)
        {
            case CapnpFbpComponentModel compNode:
                await compNode.ResetExecution();
                break;
            case CapnpFbpViewComponentModel viewNode:
                await viewNode.ResetExecution();
                break;
            case CapnpFbpIipComponentModel iipNode:
                await iipNode.ResetExecution();
                break;
        }
    }

    public static Task ConnectLinkToRunningProcessesAsync(RememberCapnpPortsLinkModel link) =>
        ResolveEditor(link) is { } editor
            ? ConnectLinkToRunningProcessesAsync(
                editor.ConnectionManager,
                editor.CurrentChannelStarterService,
                link
            )
            : Task.CompletedTask;

    public static async Task ConnectLinkToRunningProcessesAsync(
        ConnectionManager conMan,
        IStartChannelsService css,
        RememberCapnpPortsLinkModel link,
        CancellationToken cancelToken = default
    )
    {
        if (!HasLiveProcessEndpoint(link) || css == null)
            return;

        var inPort = link.InPortModel;
        if (inPort.ReaderSturdyRef == null && inPort.RetrieveReaderFromChannelTask == null)
        {
            await CreateChannel(conMan, css, link);
        }
        else if (inPort.RetrieveReaderFromChannelTask != null)
        {
            await inPort.RetrieveReaderFromChannelTask;
        }

        if (inPort.ReaderSturdyRef == null)
            return;

        await link.EnsureWriterFromChannelAsync(cancelToken);

        if (
            inPort.Parent is CapnpFbpProcessComponentModel targetProcess
            && targetProcess.SupportsLivePortChanges
            && inPort.ProcessDisconnect == null
        )
        {
            await targetProcess.ConnectInputPortAsync(inPort, cancelToken);
        }

        if (
            link.OutPortModel.Parent is CapnpFbpProcessComponentModel sourceProcess
            && sourceProcess.SupportsLivePortChanges
            && link.ProcessOutDisconnect == null
        )
        {
            await sourceProcess.ConnectOutputPortAsync(link, cancelToken);
        }

        CapnpFbpPortColors.ApplyLinkColor(link);
        link.OutPortModel.Parent?.RefreshAll();
        link.InPortModel.Parent?.RefreshAll();
    }

    public static async Task RemoveLinkAndCleanupAsync(Diagram diagram, BaseLinkModel baseLinkModel)
    {
        if (baseLinkModel is not RememberCapnpPortsLinkModel removedLink)
        {
            RestoreDefaultPortVisibility(diagram, baseLinkModel);
            diagram.Links.Remove(baseLinkModel);
            return;
        }
        await RemoveRememberedLinkAndCleanupAsync(diagram, removedLink);
    }

    public static Task RemoveAttachedLinksAndCleanupAsync(
        IReadOnlyCollection<BaseLinkModel> links,
        Diagram diagram,
        Model excludedNode = null
    ) =>
        RemoveAttachedLinksAndCleanupAsyncCore(
            links,
            diagram,
            excludedNode == null ? null : [excludedNode]
        );

    public static Task RemoveAttachedLinksAndCleanupAsync(
        IReadOnlyCollection<BaseLinkModel> links,
        Diagram diagram,
        IReadOnlyCollection<Model> excludedNodes
    ) => RemoveAttachedLinksAndCleanupAsyncCore(links, diagram, excludedNodes);

    private static async Task RemoveAttachedLinksAndCleanupAsyncCore(
        IReadOnlyCollection<BaseLinkModel> links,
        Diagram diagram,
        IReadOnlyCollection<Model> excludedNodes
    )
    {
        var excludedNodeSet = excludedNodes?.Where(node => node != null).ToHashSet() ?? [];

        foreach (var blm in new List<BaseLinkModel>(links))
        {
            if (!diagram.Links.Any(link => ReferenceEquals(link, blm)))
                continue;

            if (blm is not RememberCapnpPortsLinkModel removedLink)
            {
                await RemoveLinkAndCleanupAsync(diagram, blm);
                continue;
            }
            await RemoveRememberedLinkAndCleanupAsync(diagram, removedLink, excludedNodeSet);
        }
    }

    private static async Task RemoveRememberedLinkAndCleanupAsync(
        Diagram diagram,
        RememberCapnpPortsLinkModel removedLink,
        IReadOnlyCollection<Model> excludedNodes = null
    )
    {
        var excludedNodeSet = excludedNodes?.Where(node => node != null).ToHashSet() ?? [];
        var affectedLinks = diagram.Links
            .OfType<RememberCapnpPortsLinkModel>()
            .Where(link => ReferenceEquals(link.InPortModel, removedLink.InPortModel))
            .ToList();
        var affectedOutPorts = affectedLinks
            .Select(link => link.OutPortModel)
            .Append(removedLink.OutPortModel)
            .Distinct()
            .ToList();
        var remainingLinks = affectedLinks
            .Where(link =>
                !ReferenceEquals(link, removedLink)
                && !excludedNodeSet.Contains(link.OutPortModel.Parent as Model)
                && !excludedNodeSet.Contains(link.InPortModel.Parent as Model)
            )
            .ToList();
        var lastWriterRemoved = remainingLinks.Count == 0;
        IEnumerable<Model> candidateNodesToReset = lastWriterRemoved
            ? affectedOutPorts.Select(port => port.Parent).Append(removedLink.InPortModel.Parent).OfType<Model>()
            : new Model[] { removedLink.OutPortModel.Parent as Model };
        var nodesToReset = candidateNodesToReset
            .Where(node => node != null && !excludedNodeSet.Contains(node))
            .Distinct()
            .Where(RequiresLifecycleResetOnChannelRemoval)
            .ToList();
        IReadOnlyCollection<RememberCapnpPortsLinkModel> linksToDisconnect =
            lastWriterRemoved ? affectedLinks : new[] { removedLink };

        foreach (var node in nodesToReset)
        {
            await ResetNodeLifecycleAsync(node);
        }

        if (lastWriterRemoved)
        {
            await removedLink.InPortModel.DisconnectProcessAsync();
        }

        foreach (var link in linksToDisconnect)
        {
            await link.DisconnectProcessOutPortAsync();
        }

        if (lastWriterRemoved)
        {
            await removedLink.InPortModel.DisconnectChannelAsync(stopChannel: true);
        }

        foreach (var link in linksToDisconnect)
        {
            await link.DisconnectWriterAsync();
        }

        diagram.Links.Remove(removedLink);

        removedLink.InPortModel.SyncVisibility();
        removedLink.InPortModel.Refresh();
        foreach (var outPort in affectedOutPorts)
        {
            outPort.SyncVisibility();
            outPort.SyncProcessConnectionState();
            outPort.Refresh();
        }

        if (lastWriterRemoved)
        {
            foreach (var link in remainingLinks)
            {
                link.Stats = new Channel<IP>.StatsCallback.Stats();
                CapnpFbpPortColors.ApplyLinkColor(link);
            }

            foreach (var link in remainingLinks)
            {
                await ConnectLinkToRunningProcessesAsync(link);
            }
        }

        removedLink.OutPortModel.Parent?.RefreshAll();
        removedLink.InPortModel.Parent?.RefreshAll();
    }

    public static Task RemoveAttachedLinksAndCleanupAsync(
        NodeModel node,
        Diagram diagram,
        Model excludedNode = null
    ) => RemoveAttachedLinksAndCleanupAsync(AttachedLinks(node).ToList(), diagram, excludedNode);

    public static Task RemoveAttachedLinksAndCleanupAsync(
        NodeModel node,
        Diagram diagram,
        IReadOnlyCollection<Model> excludedNodes
    ) => RemoveAttachedLinksAndCleanupAsync(AttachedLinks(node).ToList(), diagram, excludedNodes);

    private static bool HasLiveProcessEndpoint(RememberCapnpPortsLinkModel link) =>
        link.InPortModel.Parent is CapnpFbpProcessComponentModel targetProcess
            && targetProcess.SupportsLivePortChanges
        || link.OutPortModel.Parent is CapnpFbpProcessComponentModel sourceProcess
            && sourceProcess.SupportsLivePortChanges;

    private static bool RequiresLifecycleResetOnChannelRemoval(Model node) =>
        node switch
        {
            CapnpFbpProcessComponentModel => false,
            CapnpFbpViewComponentModel => true,
            CapnpFbpIipComponentModel => true,
            CapnpFbpComponentModel => true,
            _ => false,
        };

    private static Editor ResolveEditor(RememberCapnpPortsLinkModel link) =>
        ResolveEditor(link.OutPortModel.Parent) ?? ResolveEditor(link.InPortModel.Parent);

    private static Editor ResolveEditor(Model model) =>
        model switch
        {
            CapnpFbpComponentModel component => component.Editor,
            CapnpFbpViewComponentModel view => view.Editor,
            CapnpFbpIipComponentModel iip => iip.Editor,
            _ => null,
        };

    private static CapnpFbpOutPortModel GetOutPort(BaseLinkModel link) =>
        link switch
        {
            RememberCapnpPortsLinkModel { OutPortModel: { } outPort } => outPort,
            { Source.Model: CapnpFbpOutPortModel outPort } => outPort,
            { Target.Model: CapnpFbpOutPortModel outPort } => outPort,
            _ => null,
        };

    private static CapnpFbpInPortModel GetInPort(BaseLinkModel link) =>
        link switch
        {
            RememberCapnpPortsLinkModel { InPortModel: { } inPort } => inPort,
            { Source.Model: CapnpFbpInPortModel inPort } => inPort,
            { Target.Model: CapnpFbpInPortModel inPort } => inPort,
            _ => null,
        };

    public static IEnumerable<BaseLinkModel> AttachedLinks(NodeModel node) =>
        node.PortLinks.Concat(node.Links).Distinct();

    public static int AttachedLinkCount(NodeModel node) => AttachedLinks(node).Count();

    public static string FormatStructuredTextType(StructuredText.Type sst)
    {
        return sst switch
        {
            StructuredText.Type.unstructured => "as (structured) plain text",
            StructuredText.Type.json => "as JSON",
            StructuredText.Type.xml => "as XML",
            StructuredText.Type.toml => "as TOML",
            StructuredText.Type.sturdyRef => "as SturdyRef",
            _ => "is unknown text type",
        };
    }

    public const int CardWidth = 250;
    public const int CardHeight = 200;

    public static MarkupString MakePortToolTipText(CapnpFbpPortModel port)
    {
        var ct = string.IsNullOrWhiteSpace(port.ContentType) ? "?" : port.ContentType;
        var cts = ct.Split('|');
        List<string> cts2 = [];
        foreach (var ct_ in cts)
        {
            if (!ct_.Contains(':'))
            {
                cts2.Add($"<b>{ct_}</b>");
                continue;
            }

            var x = ct_.Split(':');
            switch (x.Length)
            {
                case 2:
                    cts2.Add($"<small>{x[0]}:</small><b>{x[1]}</b>");
                    break;
                case >= 1:
                    cts2.Add(ct_);
                    break;
            }
        }
        ct = cts2.Aggregate(
            "",
            (acc, s) => $"{acc}{(acc.Length == 0 ? "" : " or ")}<b><em>{s}</em></b>"
        );
        var ms =
            port.ThePortType == CapnpFbpPortModel.PortType.In
                ? $"<b>{port.Name}</b> receives [{ct}]"
                : $"<b>{port.Name}</b> sends [{ct}]";
        if (!string.IsNullOrWhiteSpace(port.Description))
        {
            ms += $"<br/><small>{port.Description}</small>";
        }
        return new MarkupString(ms);
    }

    public static async Task RestoreDefaultPortVisibilityOfAttachedComponent(
        IReadOnlyCollection<BaseLinkModel> links,
        Diagram diagram,
        Model excludedNode = null
    ) => await RemoveAttachedLinksAndCleanupAsync(links, diagram, excludedNode);

    public static Task RestoreDefaultPortVisibilityOfAttachedComponent(
        NodeModel node,
        Diagram diagram,
        Model excludedNode = null
    ) => RemoveAttachedLinksAndCleanupAsync(AttachedLinks(node).ToList(), diagram, excludedNode);
}
