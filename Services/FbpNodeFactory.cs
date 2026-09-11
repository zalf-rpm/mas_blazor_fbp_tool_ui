namespace BlazorDrawFBP.Services;

using System;
using System.Linq;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Controls;
using BlazorDrawFBP.Models;
using Capnp.Rpc;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Newtonsoft.Json.Linq;

public class FbpNodeFactory : IFbpNodeFactory
{
    private const string NoRegistryServiceId = "no_service";

    public Component? CreateComponentFromJson(JToken jComp)
    {
        if (jComp is not JObject comp)
            return null;
        var info = comp["info"];
        var compId = info?["id"]?.ToString() ?? "";
        if (compId.Length == 0)
            return null;
        return new Component
        {
            Info = new IdInformation
            {
                Id = compId,
                Name = info?["name"]?.ToString() ?? compId,
                Description = info?["description"]?.ToString() ?? "",
            },
            Type = comp["type"]?.ToString() switch
            {
                "iip" => Component.ComponentType.iip,
                "standard" => Component.ComponentType.standard,
                "subflow" => Component.ComponentType.subflow,
                "view" => Component.ComponentType.view,
                _ => Component.ComponentType.standard,
            },
            InPorts =
                comp["inPorts"]
                    ?.Select(p => new Component.Port
                    {
                        Name = p["name"]?.ToString() ?? "no_name",
                        Type =
                            p["type"]?.ToString() == "array"
                                ? Component.Port.PortType.array
                                : Component.Port.PortType.standard,
                        ContentType = p["contentType"]?.ToString() ?? "?",
                    })
                    .ToList()
                ?? [],
            OutPorts =
                comp["outPorts"]
                    ?.Select(p => new Component.Port
                    {
                        Name = p["name"]?.ToString() ?? "no_name",
                        Type =
                            p["type"]?.ToString() == "array"
                                ? Component.Port.PortType.array
                                : Component.Port.PortType.standard,
                        ContentType = p["contentType"]?.ToString() ?? "?",
                    })
                    .ToList()
                ?? [],
        };
    }

    public NodeModel AddFbpNode(
        BlazorDiagram diagram,
        IFbpRuntimeService runtime,
        Point position,
        Component component,
        JObject? initNode = null,
        string cmd = "",
        Action<NodeModel>? onNodeLayoutChanged = null
    )
    {
        switch (component.Type)
        {
            case Component.ComponentType.standard or Component.ComponentType.process:
            {
                var componentId = component.Info.Id;
                var componentServiceId =
                    initNode?.GetValue("componentServiceId")?.Value<string>()
                    ?? NoRegistryServiceId;
                var unavailableService = false;
                if (!runtime.RegistryServiceIdToPetNameAndSturdyRef.TryGetValue(componentServiceId, out var serviceInfo))
                {
                    unavailableService = true;
                    var shortPrefix = componentServiceId[..Math.Min(3, componentServiceId.Length)];
                    var shortSuffix = componentServiceId[^Math.Min(3, componentServiceId.Length)..];
                    runtime.RegistryServiceIdToPetNameAndSturdyRef[componentServiceId] =
                        ($"Service '{shortPrefix}..{shortSuffix}' unavailable!", null);
                }
                else if (componentServiceId != NoRegistryServiceId && serviceInfo.Item2 == null)
                {
                    unavailableService = true;
                }

                var initNodeComponentId = initNode?["componentId"]?.Value<string>() ?? "";
                if (!string.IsNullOrEmpty(initNodeComponentId))
                    componentId = initNodeComponentId;

                var procName =
                    initNode?["processName"]?.ToString() ?? initNode?["process_name"]?.ToString();

                var config = initNode?.GetValue("config");
                var configStr = (config?.Type ?? JTokenType.Null) switch
                {
                    JTokenType.Object => config?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "",
                    JTokenType.String => config?.ToString() ?? "",
                    _ => "",
                };

                CapnpFbpComponentModel node;
                switch (component.Type)
                {
                    case Component.ComponentType.standard:
                    {
                        var rnode = new CapnpFbpRunnableComponentModel(
                            initNode?.GetValue("nodeId")?.Value<string>()
                                ?? initNode?.GetValue("node_id")?.Value<string>()
                                ?? Guid.NewGuid().ToString(),
                            new Point(position.X, position.Y)
                        )
                        {
                            RuntimeService = runtime,
                            Diagram = diagram,
                            ComponentId = componentId,
                            ComponentServiceId = componentServiceId,
                            ComponentName = unavailableService ? "" : component.Info.Name ?? componentId,
                            ProcessName =
                                procName
                                ?? $"{component.Info.Name ?? "new"} {CapnpFbpComponentModel.ProcessNo++}",
                            Cmd = cmd,
                            ShortDescription = unavailableService ? "" : component.Info.Description ?? "",
                            DefaultConfigString = unavailableService ? "" : component.DefaultConfig?.Value ?? "",
                            ConfigString = configStr,
                            DisplayNoOfConfigLines = initNode?["displayNoOfConfigLines"]?.Value<int>() ?? 3,
                            Editable =
                                initNode?.GetValue("editable")?.Value<bool>()
                                ?? (component.Factory?.which ?? Component.factory.WHICH.None)
                                    == Component.factory.WHICH.None,
                            InParallelCount =
                                initNode?.GetValue("parallelProcesses")?.Value<int>()
                                ?? initNode?.GetValue("parallel_processes")?.Value<int>()
                                ?? 1,
                        };

                        FbpLayoutHelper.SetDefaultComponentSize(rnode);
                        if (component.Factory?.which == Component.factory.WHICH.Runnable)
                        {
                            rnode.RunnableFactory = Proxy.Share(component.Factory!.Runnable);
                        }

                        node = rnode;
                        break;
                    }
                    case Component.ComponentType.process:
                    {
                        var pnode = new CapnpFbpProcessComponentModel(
                            initNode?.GetValue("nodeId")?.Value<string>()
                                ?? initNode?.GetValue("node_id")?.Value<string>()
                                ?? Guid.NewGuid().ToString(),
                            new Point(position.X, position.Y)
                        )
                        {
                            RuntimeService = runtime,
                            Diagram = diagram,
                            ComponentId = componentId,
                            ComponentServiceId = componentServiceId,
                            ComponentName = unavailableService ? "" : component.Info.Name ?? componentId,
                            ProcessName =
                                procName
                                ?? $"{component.Info.Name ?? "new"} {CapnpFbpComponentModel.ProcessNo++}",
                            Cmd = cmd,
                            ShortDescription = unavailableService ? "" : component.Info.Description ?? "",
                            DefaultConfigString = unavailableService ? "" : component.DefaultConfig?.Value ?? "",
                            ConfigString = configStr,
                            DisplayNoOfConfigLines = initNode?["displayNoOfConfigLines"]?.Value<int>() ?? 3,
                            Editable =
                                initNode?.GetValue("editable")?.Value<bool>()
                                ?? (component.Factory?.which ?? Component.factory.WHICH.None)
                                    == Component.factory.WHICH.None,
                            InParallelCount =
                                initNode?.GetValue("parallelProcesses")?.Value<int>()
                                ?? initNode?.GetValue("parallel_processes")?.Value<int>()
                                ?? 1,
                        };
                        FbpLayoutHelper.SetDefaultComponentSize(pnode);
                        if (component.Factory?.which == Component.factory.WHICH.Process)
                        {
                            pnode.ProcessFactory = Proxy.Share(component.Factory!.Process);
                        }

                        node = pnode;
                        break;
                    }
                    default:
                        throw new InvalidOperationException($"Unsupported component type: {component.Type}");
                }

                var controlsContainer = diagram.Controls.AddFor(node);
                controlsContainer.Add(new RemoveProcessControl(0.5, 0, -20, -50));

                foreach (var (i, input) in (component.InPorts ?? []).Select((inp, i) => (i, inp)))
                    AddPortControl.CreateAndAddPort(
                        node,
                        CapnpFbpPortModel.PortType.In,
                        i,
                        input.Name,
                        input.ContentType,
                        input.Desc,
                        input.Type == Component.Port.PortType.array
                    );

                foreach (var (i, output) in (component.OutPorts ?? []).Select((outp, i) => (i, outp)))
                    AddPortControl.CreateAndAddPort(
                        node,
                        CapnpFbpPortModel.PortType.Out,
                        i,
                        output.Name,
                        output.ContentType,
                        output.Desc,
                        output.Type == Component.Port.PortType.array
                    );

                CapnpFbpPortLayout.Apply(node, refreshPorts: false);
                AttachLayoutEvents(node, onNodeLayoutChanged);
                diagram.Nodes.Add(node);
                return node;
            }
            case Component.ComponentType.iip:
            {
                var compId = component.Info.Id;
                var node = new CapnpFbpIipComponentModel(new Point(position.X, position.Y))
                {
                    RuntimeService = runtime,
                    Diagram = diagram,
                    ComponentId = compId,
                    ShortDescription = initNode?["shortDescription"]?.ToString() ?? "",
                    Content = initNode?["content"]?.ToString() ?? "",
                    DisplayNoOfLines = initNode?["displayNoOfLines"]?.Value<int>() ?? 3,
                };
                FbpLayoutHelper.SetDefaultComponentSize(node);
                AddPortControl.CreateAndAddPort(node, CapnpFbpPortModel.PortType.Out, 0, "IIP");
                AttachLayoutEvents(node, onNodeLayoutChanged);
                diagram.Nodes.Add(node);
                diagram.Controls.AddFor(node).Add(new RemoveProcessControl(0.5, 0, -20, -50));
                return node;
            }
            case Component.ComponentType.subflow:
                throw new NotSupportedException("Subflow components are not supported.");
            case Component.ComponentType.view:
            {
                var componentId = component.Info.Id;
                var initNodeComponentId = initNode?["componentId"]?.Value<string>() ?? "";
                if (!string.IsNullOrEmpty(initNodeComponentId))
                    componentId = initNodeComponentId;

                var procName =
                    initNode?["processName"]?.ToString() ?? initNode?["process_name"]?.ToString();

                var node = new CapnpFbpViewComponentModel(
                    initNode?.GetValue("nodeId")?.Value<string>()
                        ?? initNode?.GetValue("node_id")?.Value<string>()
                        ?? Guid.NewGuid().ToString(),
                    new Point(position.X, position.Y)
                )
                {
                    RuntimeService = runtime,
                    Diagram = diagram,
                    ComponentId = componentId,
                    ComponentName = component.Info.Name ?? componentId,
                    ProcessName =
                        procName
                        ?? $"{component.Info.Name ?? "new"} {CapnpFbpComponentModel.ProcessNo++}",
                };
                node.Size = new Size(FbpLayoutHelper.ViewNodeWidth, FbpLayoutHelper.ViewNodeHeight);

                diagram.Controls.AddFor(node).Add(new RemoveProcessControl(0.5, 0, -20, -50));

                foreach (var (i, input) in (component.InPorts ?? []).Select((inp, i) => (i, inp)))
                    AddPortControl.CreateAndAddPort(
                        node,
                        CapnpFbpPortModel.PortType.In,
                        i,
                        input.Name,
                        input.ContentType,
                        input.Desc,
                        input.Type == Component.Port.PortType.array
                    );

                foreach (var (i, output) in (component.OutPorts ?? []).Select((outp, i) => (i, outp)))
                    AddPortControl.CreateAndAddPort(
                        node,
                        CapnpFbpPortModel.PortType.Out,
                        i,
                        output.Name,
                        output.ContentType,
                        output.Desc,
                        output.Type == Component.Port.PortType.array
                    );

                CapnpFbpPortLayout.Apply(node, refreshPorts: false);
                AttachLayoutEvents(node, onNodeLayoutChanged);
                diagram.Nodes.Add(node);
                return node;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void AttachLayoutEvents(NodeModel node, Action<NodeModel>? onNodeLayoutChanged)
    {
        if (onNodeLayoutChanged == null)
            return;

        node.Moved += movedModel =>
        {
            if (movedModel is NodeModel movedNode)
                onNodeLayoutChanged(movedNode);
        };
        node.SizeChanged += onNodeLayoutChanged;
    }
}
