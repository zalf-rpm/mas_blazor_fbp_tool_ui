namespace BlazorDrawFBP.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Controls;
using BlazorDrawFBP.Models;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Newtonsoft.Json.Linq;

public class FlowDocumentService : IFlowDocumentService
{
    private const string NoRegistryServiceId = "no_service";
    private const int IipIdLength = 10;
    private const int ProcIdLength = 20;

    private readonly IFbpNodeFactory _nodeFactory;

    public FlowDocumentService(IFbpNodeFactory nodeFactory)
    {
        _nodeFactory = nodeFactory;
    }

    public async Task<JObject> ExportFlowJsonAsync(BlazorDiagram diagram, IFbpRuntimeService runtime)
    {
        var (json, _) = await ExportFlowDocumentAsync(diagram, runtime, asMermaid: false);
        return json ?? new JObject();
    }

    public async Task<string> ExportFlowMermaidAsync(BlazorDiagram diagram, IFbpRuntimeService runtime)
    {
        var (_, mermaid) = await ExportFlowDocumentAsync(diagram, runtime, asMermaid: true);
        return mermaid ?? string.Empty;
    }

    public async Task<(JObject? Json, string? Mermaid)> ExportFlowDocumentAsync(
        BlazorDiagram diagram,
        IFbpRuntimeService runtime,
        bool asMermaid
    )
    {
        var dia = asMermaid
            ? null
            : JObject.Parse(await File.ReadAllTextAsync("Data/diagram_template.json"));
        HashSet<string> linkSet = [];
        StringBuilder sb = new();
        if (asMermaid)
        {
            sb.AppendLine(await File.ReadAllTextAsync("Data/diagram_template.mmd"));
        }
        else if (dia != null)
        {
            dia["pan"] = new JObject { { "x", diagram.Pan.X }, { "y", diagram.Pan.Y } };
            dia["zoom"] = diagram.Zoom;

            var servicesObj = dia["services"] as JObject;
            if (servicesObj == null)
            {
                servicesObj = new JObject();
                dia["services"] = servicesObj;
            }

            var channelsObj = new JObject();
            foreach (var p in runtime.ServiceId2ChannelStarterServices)
            {
                if (runtime.ChannelServiceIdToPetNameAndSturdyRef.TryGetValue(p.Key, out var cInfo) && !string.IsNullOrEmpty(cInfo.Item2))
                {
                    channelsObj[p.Key] = cInfo.Item2;
                }
            }
            servicesObj["channels"] = channelsObj;

            var usedComponentServiceIds = diagram.Nodes
                .OfType<CapnpFbpComponentModel>()
                .Select(n => n.ComponentServiceId)
                .Where(id => !string.IsNullOrEmpty(id) && id != NoRegistryServiceId)
                .ToHashSet();

            var componentsObj = new JObject();
            foreach (var p in runtime.ServiceId2Registries)
            {
                if (p.Key == NoRegistryServiceId)
                    continue;

                if (usedComponentServiceIds.Count > 0 && !usedComponentServiceIds.Contains(p.Key))
                    continue;

                if (runtime.RegistryServiceIdToPetNameAndSturdyRef.TryGetValue(p.Key, out var rInfo) && !string.IsNullOrEmpty(rInfo.Item2))
                {
                    componentsObj[p.Key] = rInfo.Item2;
                }
            }
            servicesObj["components"] = componentsObj;
        }

        var procIdCount = 2;
        HashSet<string> shortProcIds = [];
        Dictionary<string, string> uuid2ShortProcId = new();
        var persistedLinks = diagram
            .Links.OfType<RememberCapnpPortsLinkModel>()
            .Where(link =>
                !link.IsInternalProcLink
                && link.OutPortModel.Parent
                    is not CapnpFbpComponentModel { IsInternalProcChild: true }
                && link.InPortModel.Parent
                    is not CapnpFbpComponentModel { IsInternalProcChild: true }
            )
            .ToHashSet();

        string ShortProcId(string oldId, string procName)
        {
            if (uuid2ShortProcId.TryGetValue(oldId, out var value))
                return value;

            var procNameShortened =
                procName.Length > ProcIdLength ? procName.Split('\n')[0][..ProcIdLength] : procName;
            var shortProcId =
                procNameShortened.Length < procName.Length
                    ? $"{procNameShortened}..."
                    : procNameShortened;
            if (shortProcIds.Contains(shortProcId))
                shortProcId = $"{shortProcId} ({procIdCount++})";
            uuid2ShortProcId[oldId] = shortProcId;
            shortProcIds.Add(shortProcId);
            return shortProcId;
        }

        var iipIdCount = 2;
        HashSet<string> shortIipIds = [];
        Dictionary<string, string> uuid2ShortIipId = new();

        string ShortIipId(string oldId, string iipContent)
        {
            if (uuid2ShortIipId.TryGetValue(oldId, out var value))
                return value;

            var iipContentShortened =
                iipContent.Length > IipIdLength
                    ? iipContent.Split('\n')[0][..IipIdLength]
                    : iipContent;
            var shortIipId = $"IIP [{iipContentShortened}...]";
            if (shortIipIds.Contains(shortIipId))
                shortIipId = $"{shortIipId} ({iipIdCount++})";
            uuid2ShortIipId[oldId] = shortIipId;
            shortIipIds.Add(shortIipId);
            return shortIipId;
        }

        static string MermaidEscapeQuotes(string str) => str.Replace("\"", "&quot;");

        static string CreateMermaidId(string id)
        {
            var newId = new StringBuilder();
            foreach (var c in id)
                newId.Append(char.IsLetterOrDigit(c) ? c : '_');
            var newId2 = new StringBuilder();
            var foundUnderscore = false;
            foreach (var c in newId.ToString())
                if (c != '_')
                {
                    newId2.Append(c);
                    foundUnderscore = false;
                }
                else if (!foundUnderscore)
                {
                    newId2.Append(c);
                    foundUnderscore = true;
                }

            var newId2Str = newId2.ToString();
            return newId2Str[^1] == '_' ? newId2Str[..^1] : newId2Str;
        }

        foreach (var node in diagram.Nodes)
        {
            switch (node)
            {
                case CapnpFbpComponentModel fbpNode:
                {
                    var nodeId = ShortProcId(fbpNode.Id, fbpNode.ProcessName);
                    if (asMermaid)
                    {
                        sb.AppendLine(
                            $"{CreateMermaidId(nodeId)}(\"{MermaidEscapeQuotes(fbpNode.ProcessName)}\")"
                        );

                        const string confPortName = "conf";
                        var confLinks = fbpNode.Links.Where(blm =>
                            blm
                                is RememberCapnpPortsLinkModel
                                {
                                    InPortModel: CapnpFbpPortModel { Name: confPortName }
                                }
                        );
                        if (!string.IsNullOrEmpty(fbpNode.ConfigString) && !confLinks.Any())
                        {
                            var tempIipNodeId = Guid.NewGuid().ToString();
                            var iipNodeId = ShortIipId(tempIipNodeId, fbpNode.ConfigString);
                            var mermaidIipId = CreateMermaidId(iipNodeId);
                            sb.AppendLine(
                                $"{mermaidIipId}[[\"{MermaidEscapeQuotes(fbpNode.ConfigString)}\"]]"
                            );
                            sb.AppendLine(
                                $"{mermaidIipId} -- \"{confPortName}\" --> {CreateMermaidId(nodeId)}"
                            );
                        }
                    }
                    else
                    {
                        var config = new JObject();
                        try
                        {
                            config = JObject.Parse(fbpNode.ConfigString);
                        }
                        catch (Exception) { }

                        var jn = new JObject
                        {
                            { "nodeId", nodeId },
                            { "processName", fbpNode.ProcessName },
                            {
                                "location",
                                new JObject
                                {
                                    { "x", fbpNode.Position.X },
                                    { "y", fbpNode.Position.Y },
                                }
                            },
                            { "editable", fbpNode.Editable },
                            { "parallelProcesses", fbpNode.InParallelCount },
                            { "config", config },
                        };
                        if (string.IsNullOrWhiteSpace(fbpNode.ComponentId))
                        {
                            var inputs = fbpNode
                                .Ports.Where(p =>
                                    p is CapnpFbpPortModel cp
                                    && cp.ThePortType == CapnpFbpPortModel.PortType.In
                                )
                                .Select(p => p as CapnpFbpPortModel)
                                .Select(p => new JObject
                                {
                                    { "name", p!.Name },
                                    { "type", p.IsArrayPort ? "array" : "standard" },
                                    { "contentType", p.ContentType },
                                    { "desc", p.Description },
                                });

                            var outputs = fbpNode
                                .Ports.Where(p =>
                                    p is CapnpFbpPortModel cp
                                    && cp.ThePortType == CapnpFbpPortModel.PortType.Out
                                )
                                .Select(p => p as CapnpFbpPortModel)
                                .Select(p => new JObject
                                {
                                    { "name", p!.Name },
                                    { "type", p.IsArrayPort ? "array" : "standard" },
                                });

                            var defaultConfig = new JObject();
                            try
                            {
                                defaultConfig = JObject.Parse(fbpNode.DefaultConfigString);
                            }
                            catch (Exception) { }
                            jn.Add(
                                "component",
                                new JObject
                                {
                                    {
                                        "info",
                                        new JObject
                                        {
                                            { "id", fbpNode.ComponentId },
                                            { "name", fbpNode.ComponentName },
                                            { "description", fbpNode.ShortDescription },
                                        }
                                    },
                                    { "type", "standard" },
                                    { "inPorts", new JArray(inputs) },
                                    { "outPorts", new JArray(outputs) },
                                    { "cmd", fbpNode.Cmd },
                                    { "defaultConfig", defaultConfig },
                                }
                            );
                        }
                        else
                        {
                            jn.Add("componentId", fbpNode.ComponentId);
                            if (
                                !string.IsNullOrWhiteSpace(fbpNode.ComponentServiceId)
                                && fbpNode.ComponentServiceId != NoRegistryServiceId
                            )
                                jn.Add("componentServiceId", fbpNode.ComponentServiceId);
                        }

                        if (dia?["nodes"] is JArray nodes)
                            nodes.Add(jn);
                    }

                    break;
                }
                case CapnpFbpIipComponentModel iipNode:
                {
                    var iipNodeId = ShortIipId(iipNode.Id, iipNode.Content);
                    if (asMermaid)
                    {
                        sb.AppendLine(
                            $"{CreateMermaidId(iipNodeId)}[[\"{MermaidEscapeQuotes(iipNode.Content)}\"]]"
                        );
                    }
                    else
                    {
                        var jn = new JObject
                        {
                            { "nodeId", iipNodeId },
                            { "componentId", iipNode.ComponentId },
                            {
                                "location",
                                new JObject
                                {
                                    { "x", iipNode.Position.X },
                                    { "y", iipNode.Position.Y },
                                }
                            },
                            { "shortDescription", iipNode.ShortDescription ?? "" },
                            { "content", iipNode.Content },
                            { "displayNoOfLines", iipNode.DisplayNoOfLines },
                        };
                        if (dia?["nodes"] is JArray nodes)
                            nodes.Add(jn);
                    }

                    break;
                }
                case CapnpFbpViewComponentModel viewNode:
                {
                    if (asMermaid)
                    {
                        sb.AppendLine($"{CreateMermaidId(viewNode.Id)}>\"View\"]");
                    }
                    else
                    {
                        var jn = new JObject
                        {
                            { "nodeId", viewNode.Id },
                            { "componentId", viewNode.ComponentId },
                            {
                                "location",
                                new JObject
                                {
                                    { "x", viewNode.Position.X },
                                    { "y", viewNode.Position.Y },
                                }
                            },
                        };
                        if (dia?["nodes"] is JArray nodes)
                            nodes.Add(jn);
                    }

                    break;
                }
                default:
                    continue;
            }

            foreach (var pl in node.PortLinks.Concat(node.Links))
            {
                if (
                    !pl.IsAttached
                    || pl is not RememberCapnpPortsLinkModel rcplm
                    || !persistedLinks.Contains(rcplm)
                )
                {
                    continue;
                }

                var outCapnpPort = rcplm.OutPortModel as CapnpFbpOutPortModel;
                var inCapnpPort = rcplm.InPortModel as CapnpFbpInPortModel;

                switch (outCapnpPort)
                {
                    case { Parent: CapnpFbpIipComponentModel outIipModel }
                        when inCapnpPort is { Parent: CapnpFbpComponentModel inCapnpModel }:
                    {
                        var outIipNodeId = ShortIipId(outIipModel.Id, outIipModel.Content);
                        var inNodeId = ShortProcId(inCapnpModel.Id, inCapnpModel.ProcessName);

                        var checkOut = $"{outIipNodeId}.{outCapnpPort.Name}";
                        var checkIn = $"{inNodeId}.{inCapnpPort.Name}";
                        if (
                            linkSet.Contains($"{checkOut}->{checkIn}")
                            || linkSet.Contains($"{checkIn}->{checkOut}")
                        )
                            continue;
                        linkSet.Add($"{checkOut}->{checkIn}");

                        if (asMermaid)
                        {
                            sb.AppendLine(
                                $"{CreateMermaidId(outIipNodeId)} -- \"{inCapnpPort.Name}\" --> {CreateMermaidId(inNodeId)}"
                            );
                        }
                        else
                        {
                            var jl = new JObject
                            {
                                {
                                    "source",
                                    new JObject
                                    {
                                        { "nodeId", outIipNodeId },
                                        { "port", outCapnpPort.Name },
                                    }
                                },
                                {
                                    "target",
                                    new JObject
                                    {
                                        { "nodeId", inNodeId },
                                        { "port", inCapnpPort.Name },
                                    }
                                },
                                { "bufferSize", inCapnpPort.ChannelBufferSize },
                            };
                            if (dia?["links"] is JArray links)
                                links.Add(jl);
                        }

                        break;
                    }
                    case { Parent: CapnpFbpComponentModel outCapnpModel }
                        when inCapnpPort is { Parent: CapnpFbpComponentModel inCapnpModel2 }:
                    {
                        var outNodeId = ShortProcId(outCapnpModel.Id, outCapnpModel.ProcessName);
                        var inNodeId = ShortProcId(inCapnpModel2.Id, inCapnpModel2.ProcessName);

                        var checkOut = $"{outNodeId}.{outCapnpPort.Name}";
                        var checkIn = $"{inNodeId}.{inCapnpPort.Name}";
                        if (
                            linkSet.Contains($"{checkOut}->{checkIn}")
                            || linkSet.Contains($"{checkIn}->{checkOut}")
                        )
                            continue;
                        linkSet.Add($"{checkOut}->{checkIn}");

                        if (asMermaid)
                        {
                            sb.AppendLine(
                                $"{CreateMermaidId(outNodeId)} -- \"{outCapnpPort.Name} : {inCapnpPort.Name}\" --> {CreateMermaidId(inNodeId)}"
                            );
                        }
                        else
                        {
                            var jl = new JObject
                            {
                                {
                                    "source",
                                    new JObject
                                    {
                                        { "nodeId", outNodeId },
                                        { "port", outCapnpPort.Name },
                                    }
                                },
                                {
                                    "target",
                                    new JObject
                                    {
                                        { "nodeId", inNodeId },
                                        { "port", inCapnpPort.Name },
                                    }
                                },
                                { "bufferSize", inCapnpPort.ChannelBufferSize },
                            };
                            if (dia?["links"] is JArray links)
                                links.Add(jl);
                        }

                        break;
                    }
                    case { Parent: CapnpFbpIipComponentModel outIipModel2 }
                        when inCapnpPort is { Parent: CapnpFbpViewComponentModel inViewCapnpModel }:
                    {
                        var outIipNodeId = ShortIipId(outIipModel2.Id, outIipModel2.Content);
                        var inNodeId = inViewCapnpModel.Id;

                        var checkOut = $"{outIipNodeId}.{outCapnpPort.Name}";
                        var checkIn = $"{inNodeId}.{inCapnpPort.Name}";
                        if (
                            linkSet.Contains($"{checkOut}->{checkIn}")
                            || linkSet.Contains($"{checkIn}->{checkOut}")
                        )
                            continue;
                        linkSet.Add($"{checkOut}->{checkIn}");

                        if (asMermaid)
                        {
                            sb.AppendLine(
                                $"{CreateMermaidId(outIipNodeId)} -- \"{inCapnpPort.Name}\" --> {CreateMermaidId(inNodeId)}"
                            );
                        }
                        else
                        {
                            var jl = new JObject
                            {
                                {
                                    "source",
                                    new JObject
                                    {
                                        { "nodeId", outIipNodeId },
                                        { "port", outCapnpPort.Name },
                                    }
                                },
                                {
                                    "target",
                                    new JObject
                                    {
                                        { "nodeId", inNodeId },
                                        { "port", inCapnpPort.Name },
                                    }
                                },
                                { "bufferSize", inCapnpPort.ChannelBufferSize },
                            };
                            if (dia?["links"] is JArray links)
                                links.Add(jl);
                        }

                        break;
                    }
                    case { Parent: CapnpFbpComponentModel outCapnpModel2 }
                        when inCapnpPort is { Parent: CapnpFbpViewComponentModel inViewCapnpModel2 }:
                    {
                        var outNodeId = ShortProcId(outCapnpModel2.Id, outCapnpModel2.ProcessName);
                        var inNodeId = inViewCapnpModel2.Id;

                        var checkOut = $"{outNodeId}.{outCapnpPort.Name}";
                        var checkIn = $"{inNodeId}.{inCapnpPort.Name}";
                        if (
                            linkSet.Contains($"{checkOut}->{checkIn}")
                            || linkSet.Contains($"{checkIn}->{checkOut}")
                        )
                            continue;
                        linkSet.Add($"{checkOut}->{checkIn}");

                        if (asMermaid)
                        {
                            sb.AppendLine(
                                $"{CreateMermaidId(outNodeId)} -- \"{outCapnpPort.Name} : {inCapnpPort.Name}\" --> {CreateMermaidId(inNodeId)}"
                            );
                        }
                        else
                        {
                            var jl = new JObject
                            {
                                {
                                    "source",
                                    new JObject
                                    {
                                        { "nodeId", outNodeId },
                                        { "port", outCapnpPort.Name },
                                    }
                                },
                                {
                                    "target",
                                    new JObject
                                    {
                                        { "nodeId", inNodeId },
                                        { "port", inCapnpPort.Name },
                                    }
                                },
                                { "bufferSize", inCapnpPort.ChannelBufferSize },
                            };
                            if (dia?["links"] is JArray links)
                                links.Add(jl);
                        }

                        break;
                    }
                }
            }
        }

        return (dia, asMermaid ? sb.ToString() : null);
    }

    public async Task LoadFlowFromJsonAsync(
        BlazorDiagram diagram,
        IFbpRuntimeService runtime,
        JObject dia,
        Func<Task>? onZoomToFit = null,
        Action<NodeModel>? onNodeLayoutChanged = null
    )
    {
        var oldNodeIdToNewNode = new Dictionary<string, NodeModel>();

        await runtime.CheckConnectedServicesHealthAsync();

        if (dia["services"]?["channels"] is JObject channelsObj)
        {
            foreach (var prop in channelsObj.Properties())
            {
                var sturdyRef = prop.Value?.ToString();
                if (string.IsNullOrWhiteSpace(sturdyRef))
                    continue;

                var isConnected = runtime.ChannelServiceIdToPetNameAndSturdyRef
                    .Any(entry => entry.Value.Item2 == sturdyRef && runtime.ServiceId2ChannelStarterServices.ContainsKey(entry.Key));

                if (!isConnected)
                {
                    var chan = await runtime.ConnectToStartChannelsServiceAsync(prop.Name, sturdyRef);
                    if (chan == null)
                    {
                        runtime.NotifyServiceConnectionDropped(
                            "Channel starter service",
                            prop.Name,
                            prop.Name,
                            sturdyRef
                        );
                    }
                }
            }
        }

        if (dia["services"]?["components"] is JObject componentsObj)
        {
            foreach (var prop in componentsObj.Properties())
            {
                var sturdyRef = prop.Value?.ToString();
                if (string.IsNullOrWhiteSpace(sturdyRef))
                    continue;

                var isConnected = runtime.RegistryServiceIdToPetNameAndSturdyRef
                    .Any(entry => entry.Key != NoRegistryServiceId && entry.Value.Item2 == sturdyRef && runtime.ServiceId2Registries.ContainsKey(entry.Key));

                if (!isConnected)
                {
                    var reg = await runtime.ConnectToRegistryServiceAsync(prop.Name, sturdyRef);
                    if (reg == null)
                    {
                        runtime.NotifyServiceConnectionDropped(
                            "Component registry service",
                            prop.Name,
                            prop.Name,
                            sturdyRef
                        );
                    }
                }
            }
        }

        await runtime.CheckConnectedServicesHealthAsync();

        diagram.SuspendRefresh = true;
        try
        {
            foreach (var node in dia["nodes"] ?? new JArray())
            {
                if (node is not JObject nodeObj)
                    continue;

                var position = new Point(
                    nodeObj["location"]?["x"]?.Value<double>() ?? 0,
                    nodeObj["location"]?["y"]?.Value<double>() ?? 0
                );

                var compId =
                    nodeObj["componentId"]?.ToString() ?? nodeObj["component_id"]?.ToString() ?? "";
                var compServiceId =
                    nodeObj["componentServiceId"]?.ToString() ?? NoRegistryServiceId;
                Component? component = null;
                var cmd = "";
                if (
                    string.IsNullOrEmpty(compId)
                    && nodeObj.TryGetValue("component", out var compDesc)
                )
                {
                    component = _nodeFactory.CreateComponentFromJson(compDesc);
                    cmd = compDesc["cmd"]?.ToString() ?? "";
                }
                else if (
                    !runtime.ServiceIdAndComponentId2Component.TryGetValue(
                        (compServiceId, compId),
                        out component
                    )
                )
                {
                    foreach (var (key, value) in runtime.ServiceIdAndComponentId2Component)
                    {
                        if (key.Item2 != compId)
                            continue;
                        component = value;
                        nodeObj["componentServiceId"] = key.Item1;
                        break;
                    }
                }

                if (component == null)
                {
                    component = nodeObj.ContainsKey("content")
                        ? runtime.ServiceIdAndComponentId2Component.GetValueOrDefault((NoRegistryServiceId, "iip"))
                        : runtime.ServiceIdAndComponentId2Component.GetValueOrDefault((NoRegistryServiceId, "empty_component"))
                            ?? new Component
                            {
                                Info = new IdInformation { Id = compId, Name = compId },
                                Type = Component.ComponentType.standard,
                                InPorts = [new Component.Port { Name = "in", ContentType = "?" }],
                                OutPorts = [new Component.Port { Name = "out", ContentType = "?" }],
                            };
                }

                if (component != null)
                {
                    var diaNode = _nodeFactory.AddFbpNode(
                        diagram,
                        runtime,
                        position,
                        component,
                        nodeObj,
                        cmd,
                        onNodeLayoutChanged
                    );
                    var oldNodeId = nodeObj["nodeId"]?.ToString() ?? nodeObj["node_id"]?.ToString() ?? "";
                    oldNodeIdToNewNode[oldNodeId] = diaNode;
                }
            }

            foreach (var link in dia["links"] ?? new JArray())
            {
                if (link["source"] is not JObject source || link["target"] is not JObject target)
                    continue;

                var sourcePortName = source["port"]?.ToString();
                var targetPortName = target["port"]?.ToString();
                if (sourcePortName == null || targetPortName == null)
                    continue;

                if (
                    !oldNodeIdToNewNode.TryGetValue(
                        source["nodeId"]?.ToString() ?? source["node_id"]?.ToString() ?? "",
                        out var sourceNode
                    )
                    || !oldNodeIdToNewNode.TryGetValue(
                        target["nodeId"]?.ToString() ?? target["node_id"]?.ToString() ?? "",
                        out var targetNode
                    )
                )
                {
                    continue;
                }

                if (
                    sourceNode == null
                    || sourceNode.Ports.Count == 0
                    || targetNode == null
                    || targetNode.Ports.Count == 0
                )
                    continue;

                var sourcePort = sourceNode
                    .Ports.Where(p =>
                        p is CapnpFbpOutPortModel capnpPort && capnpPort.Name == sourcePortName
                    )
                    .DefaultIfEmpty(null)
                    .First();
                var noOfSourcePorts = sourceNode.Ports.Count(p =>
                    p is CapnpFbpOutPortModel { ThePortType: CapnpFbpPortModel.PortType.Out }
                );
                var targetPort = targetNode
                    .Ports.Where(p =>
                        p is CapnpFbpInPortModel capnpPort && capnpPort.Name == targetPortName
                    )
                    .DefaultIfEmpty(null)
                    .First();
                var noOfTargetPorts = targetNode.Ports.Count(p =>
                    p is CapnpFbpInPortModel { ThePortType: CapnpFbpPortModel.PortType.In }
                );
                if (sourcePort == null && sourceNode is CapnpFbpIipComponentModel)
                {
                    sourcePort = sourceNode.Ports.OfType<CapnpFbpOutPortModel>().FirstOrDefault();
                }
                if (sourcePort == null && sourceNode is CapnpFbpComponentModel sn)
                    sourcePort = AddPortControl.CreateAndAddPort(
                        sn,
                        CapnpFbpPortModel.PortType.Out,
                        noOfSourcePorts,
                        sourcePortName
                    );
                if (targetPort == null && targetNode is CapnpFbpComponentModel tn)
                    targetPort = AddPortControl.CreateAndAddPort(
                        tn,
                        CapnpFbpPortModel.PortType.In,
                        noOfTargetPorts,
                        targetPortName
                    );

                if (sourcePort is CapnpFbpOutPortModel scp)
                {
                    scp.SyncVisibility();
                }
                else
                {
                    continue;
                }
                if (targetPort is CapnpFbpInPortModel tcp)
                {
                    tcp.SyncVisibility();
                    tcp.SetKnownChannelBufferSize(
                        link["bufferSize"]?.Value<ulong>()
                            ?? link["buffer_size"]?.Value<ulong>()
                            ?? tcp.ChannelBufferSize,
                        refreshLinks: false
                    );
                }
                else
                {
                    continue;
                }

                var l = new RememberCapnpPortsLinkModel(scp, tcp);
                CapnpFbpPortColors.ApplyLinkColor(l);
                var cllm = new ChannelLinkLabelModel(l, "Channel", 0.5);
                l.Labels.Add(cllm);
                diagram.Links.Add(l);
            }
        }
        finally
        {
            diagram.SuspendRefresh = false;
        }

        await FbpLayoutHelper.SyncProcStructureAsync(diagram.Nodes.OfType<CapnpFbpComponentModel>());
        diagram.Refresh();
        if (onZoomToFit != null)
        {
            try
            {
                await onZoomToFit();
            }
            catch (Exception) { }
        }
    }
}
