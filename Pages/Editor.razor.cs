using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Behaviors;
using Blazor.Diagrams.Core.Controls;
using Blazor.Diagrams.Core.Controls.Default;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.PathGenerators;
using Blazor.Diagrams.Core.Routers;
using Blazor.Diagrams.Options;
using BlazorDrawFBP.Behaviors;
using BlazorDrawFBP.Controls;
using BlazorDrawFBP.Models;
using Capnp.Rpc;
using Mas.Infrastructure.BlazorComponents;
using Mas.Infrastructure.Common;
using Mas.Schema.Climate;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Mas.Schema.Registry;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
//using SharedDemo.Demos;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using Restorer = Mas.Infrastructure.Common.Restorer;

namespace BlazorDrawFBP.Pages
{
    public partial class Editor
    {
        private static readonly Random Random = new();
        private BlazorDiagram Diagram { get; set; } = null!;
        //private readonly List<string> events = new List<string>();

        //private JObject _components = null!;
        //private readonly Dictionary<string, JObject> _componentDict = new();
        private readonly Dictionary<string, List<string>> _catId2ComponentIds = new();
        private readonly Dictionary<string, Mas.Schema.Common.IdInformation> _catId2Info = new();
        private readonly Dictionary<string, Mas.Schema.Fbp.Component> _componentId2Component = new();
        private readonly List<Mas.Schema.Registry.IRegistry> _registries = [];
        private List<Mas.Schema.Fbp.PortInfos.NameAndSR> test = [];

        private readonly Restorer _restorer = new() { TcpHost = ConnectionManager.GetLocalIPAddress() };

        private Mas.Schema.Fbp.IStartChannelsService _channelStarterService = null;

        static Component CreateFromJson(JToken jComp)
        {
            if (jComp is not JObject comp) return null;
            var info = comp["info"];
            var compId = info?["id"]?.ToString() ?? "";
            if (compId.Length == 0) return null;
            return new Component()
            {
                Info = new IdInformation
                {
                    Id = compId,
                    Name = info?["name"]?.ToString() ?? compId,
                    Description = info?["description"]?.ToString() ?? ""
                },
                Type = comp["type"]?.ToString() switch {
                    "iip" => Component.ComponentType.iip,
                    "standard" => Component.ComponentType.standard,
                    "subflow" => Component.ComponentType.subflow,
                    _ => Component.ComponentType.standard,
                },
                InPorts = comp["inPorts"]?.Select(p => new Component.Port()
                {
                    Name = p["name"]?.ToString() ?? "no_name",
                    Type = Component.Port.PortType.standard,
                    TheContentType = p["contentType"]?.ToString() ?? "",
                }).ToList() ?? [],
                OutPorts = comp["outPorts"]?.Select(p => new Component.Port()
                {
                    Name = p["name"]?.ToString() ?? "no_name",
                    Type = Component.Port.PortType.standard,
                    TheContentType = p["contentType"]?.ToString() ?? "",
                }).ToList() ?? [],
            };
        }

        protected override void OnInitialized()
        {
            var options = new BlazorDiagramOptions
            {
                AllowMultiSelection = true,
                Zoom = { Enabled = true, Inverse = true },
                Links =
                {
                    DefaultRouter = new NormalRouter(),
                    DefaultPathGenerator = new SmoothPathGenerator()
                },
                Groups = { Enabled = true }
            };

            Diagram = new BlazorDiagram(options);
            var ksb = Diagram.GetBehavior<KeyboardShortcutsBehavior>();
            ksb?.RemoveShortcut("Delete", false, false, false);
            ksb?.SetShortcut("Delete", false, true, false, KeyboardShortcutsDefaults.DeleteSelection);
            
            var defComps = JObject.Parse(File.ReadAllText("Data/default_components.json"));
            foreach (var cat in defComps["categories"] ?? new JArray())
            {
                var catId = cat["id"]?.ToString() ?? "";
                if (catId.Length == 0) continue;
                _catId2Info[catId] = new IdInformation
                {
                    Id = catId,
                    Name = cat["name"]?.ToString() ?? catId,
                    Description = cat["description"]?.ToString() ?? ""
                };
            }
            foreach (var entry in defComps["entries"] ?? new JArray())
            {
                var catId = entry["categoryId"]?.ToString() ?? "";
                if (catId.Length == 0) continue;
                if (entry["component"] is not JObject comp) continue;

                if (!_catId2ComponentIds.ContainsKey(catId)) _catId2ComponentIds[catId] = [];
                if (!_catId2Info.ContainsKey(catId)) _catId2Info[catId] = new IdInformation
                {
                    Id = catId, Name = catId
                };

                var component = CreateFromJson(entry["component"]);
                if (component == null) continue;
                _catId2ComponentIds[catId].Add(component.Info.Id);
                _componentId2Component[component.Info.Id] = component;
            }

            Diagram.RegisterComponent<CapnpFbpComponentModel, CapnpFbpComponentWidget>();
            Diagram.RegisterComponent<CapnpFbpIipModel, CapnpFbpIipWidget>();
            Diagram.RegisterComponent<UpdatePortNameNode, UpdatePortNameNodeWidget>();
            Diagram.RegisterComponent<PortOptionsNode, PortOptionsNodeWidget>();
            Diagram.RegisterComponent<NodeInformationControl, NodeInformationControlWidget>();
            Diagram.RegisterComponent<LinkInformationControl, LinkInformationControlWidget>();
            Diagram.RegisterComponent<AddPortControl, AddPortControlWidget>();
            Diagram.RegisterComponent<ToggleEditNodeControl, ToggleEditNodeControlWidget>();
            Diagram.RegisterComponent<RemoveProcessControl, RemoveProcessControlWidget>();
            Diagram.RegisterComponent<RemoveLinkControl, RemoveLinkControlWidget>();
            Diagram.RegisterComponent<LinkModel, FbpLinkWidget>(true);
            Diagram.RegisterComponent<ChannelLinkLabelModel, ChannelLinkLabelWidget>();
            RegisterEvents();
            
            //var oldDragNewLinkBehavior = Diagram.GetBehavior<DragNewLinkBehavior>()!;
            Diagram.UnregisterBehavior<DragNewLinkBehavior>();
            Diagram.RegisterBehavior(new FbpDragNewLinkBehavior(Diagram));

            ConMan.Restorer = _restorer;
            ConMan.Bind(IPAddress.Any, 0, _restorer);
            _restorer.TcpPort = ConMan.Port;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            Console.WriteLine($"Editor: OnAfterRenderAsync firstRender: {firstRender}");
            if (!firstRender) return;
            if (!await LocalStorage.ContainKeyAsync("sturdy-ref-store")) return;
            var allBookmarks = await StoredSRData.GetAllData(LocalStorage);
            allBookmarks.Add(new StoredSRData()
            {
                AutoConnect = true,
                InterfaceId = typeof(Mas.Schema.Registry.IRegistry).GetCustomAttribute<Capnp.TypeIdAttribute>(false)?.Id ?? 0,
                PetName = "Local components service",
                SturdyRef = "capnp://10.10.28.250:9988/local_components",
            });
            var channelStarterInterfaceId = typeof(Mas.Schema.Fbp.IStartChannelsService)
                .GetCustomAttribute<Capnp.TypeIdAttribute>(false)?.Id ?? 0;
            allBookmarks.Add(new StoredSRData()
            {
                AutoConnect = true,
                InterfaceId = channelStarterInterfaceId,
                PetName = "Channels Starter Service",
                SturdyRef = "capnp://10.10.28.250:9989/channel_starter",
            });
            allBookmarks.Sort();

            // iterate over all bookmarks
            foreach (var ssrd in allBookmarks)
            {
                if (ssrd.InterfaceId == channelStarterInterfaceId)
                {
                    try
                    {
                        _channelStarterService =
                            await ConMan.Connect<Mas.Schema.Fbp.IStartChannelsService>(ssrd.SturdyRef);
                        Console.WriteLine("Connected to channel starter service @ " + ssrd.SturdyRef);
                    }
                    catch (Capnp.Rpc.RpcException)
                    {
                        Console.WriteLine("Couldn't connect to channel starter service @ " + ssrd.SturdyRef);
                    }
                }
                else
                {
                    Mas.Schema.Registry.IRegistry reg = null;
                    try
                    {
                        reg = await ConMan.Connect<Mas.Schema.Registry.IRegistry>(ssrd.SturdyRef);
                        Console.WriteLine("Connected to components registry @ " + ssrd.SturdyRef);
                    }
                    catch (Capnp.Rpc.RpcException)
                    {
                        Console.WriteLine("Couldn't connect to components registry @ " + ssrd.SturdyRef);
                        continue;
                    }
                    if (reg == null) continue;
                    try
                    {
                        _registries.Add(Capnp.Rpc.Proxy.Share(reg));
                        var categories = await reg.SupportedCategories();
                        foreach (var cat in categories)
                        {
                            if (!_catId2Info.ContainsKey(cat.Id))
                                _catId2Info[cat.Id] = new IdInformation
                                {
                                    Id = cat.Id, Name = cat.Name ?? cat.Id,
                                    Description = cat.Description ?? cat.Name ?? cat.Id
                                };
                        }
                        Console.WriteLine("Loaded supported categories from " + ssrd.SturdyRef);
                    }
                    catch (Capnp.Rpc.RpcException)
                    {
                        Console.WriteLine("Error loading supported categories from " + ssrd.SturdyRef);
                    }

                    try
                    {
                        var entries = await reg.Entries(null);
                        foreach (var e in entries)
                        {
                            if (!_catId2ComponentIds.ContainsKey(e.CategoryId)) _catId2ComponentIds[e.CategoryId] = [];
                            _catId2ComponentIds[e.CategoryId].Add(e.Id);
                            if (e.Ref is not Proxy p) continue;
                            var holder = p.Cast<Mas.Schema.Common.IIdentifiableHolder<Mas.Schema.Fbp.Component>>(true);
                            try
                            {
                                _componentId2Component.Add(e.Id, await holder.Value());
                            }
                            catch (System.Exception ex)
                            {
                                Console.WriteLine(ex);
                            }
                        }
                        Console.WriteLine("Loaded entries from " + ssrd.SturdyRef);
                    }
                    catch (Capnp.Rpc.RpcException)
                    {
                        Console.WriteLine("Error loading entries from " + ssrd.SturdyRef);
                    }
                }
            }
            StateHasChanged();
        }

        protected override async Task OnInitializedAsync()
        {
        }

        private void CreateChannel(PortModel outPort, CapnpFbpPortModel inPort)
        {
            Shared.Shared.CreateChannel(ConMan, _channelStarterService, outPort, inPort);
        }

        private void RegisterEvents()
        {
            // Diagram.Changed += () =>
            // {
            //     events.Add("Changed");
            //     StateHasChanged();
            // };

            // Diagram.Nodes.Added += (n) => events.Add($"NodesAdded, NodeId={n.Id}");
            // Diagram.Nodes.Removed += (n) => events.Add($"NodesRemoved, NodeId={n.Id}");

            // Diagram.SelectionChanged += (m) =>
            // {
            //     events.Add($"SelectionChanged, Id={m.Id}, Type={m.GetType().Name}, Selected={m.Selected}");
            //     StateHasChanged();
            // };

            Diagram.Links.Added += async (l) =>
            {
                Diagram.Controls.AddFor(l).Add(new RemoveLinkControl(0.5, 0.5));
                switch (l.Source.Model)
                {
                    case CapnpFbpPortModel sourcePort:
                    {
                        var targetPort = l.Target.Model as CapnpFbpPortModel;
                        switch (sourcePort.ThePortType)
                        {
                            case CapnpFbpPortModel.PortType.In:
                                l.Labels.Add(new LinkLabelModel(l, sourcePort.Name, 0.2));
                                l.Labels.Add(new LinkLabelModel(l, targetPort?.Name ?? "OUT", 0.8));
                                l.SourceMarker = LinkMarker.Arrow;
                                l.TargetChanged += (link, oldTarget, newTarget) =>
                                {
                                    if (newTarget.Model is not CapnpFbpPortModel outPort) return;
                                    var nl = new RememberCapnpPortsLinkModel(outPort.Parent, sourcePort.Parent)
                                    {
                                        OutPortModel = outPort,
                                        InPortModel = sourcePort,
                                    };
                                    nl.Labels.Add(new LinkLabelModel(nl, outPort.Name, 0.2));
                                    var cllm = new ChannelLinkLabelModel(nl, "Channel", 0.5);
                                    if (InteractiveMode) CreateChannel(outPort, sourcePort);
                                    InteractiveModeChanged += cllm.ToggleInteractiveMode;
                                    nl.Labels.Add(cllm);
                                    nl.Labels.Add(new LinkLabelModel(nl, sourcePort.Name, 0.8));
                                    nl.TargetMarker = LinkMarker.Arrow;
                                    Diagram.Links.Add(nl);
                                    Diagram.Links.Remove(l);
                                    outPort.Visibility = CapnpFbpPortModel.VisibilityState.Hidden;
                                    sourcePort.Visibility = CapnpFbpPortModel.VisibilityState.Dashed;
                                    sourcePort.Refresh();
                                    outPort.Refresh();
                                };
                                break;
                            case CapnpFbpPortModel.PortType.Out:
                                l.Labels.Add(new LinkLabelModel(l, sourcePort.Name, 0.2));
                                l.Labels.Add(new LinkLabelModel(l, targetPort?.Name ?? "IN", 0.8));
                                l.TargetMarker = LinkMarker.Arrow;
                                l.TargetChanged += (link, oldTarget, newTarget) =>
                                {
                                    if (newTarget.Model is not CapnpFbpPortModel inPort) return;
                                    var nl = new RememberCapnpPortsLinkModel(sourcePort.Parent,
                                        inPort.Parent)
                                    {
                                        OutPortModel = sourcePort,
                                        InPortModel = inPort,
                                    };
                                    nl.Labels.Add(new LinkLabelModel(nl, sourcePort.Name, 0.2));
                                    var cllm = new ChannelLinkLabelModel(nl, "Channel", 0.5);
                                    if (InteractiveMode) CreateChannel(sourcePort, inPort);
                                    InteractiveModeChanged += cllm.ToggleInteractiveMode;
                                    nl.Labels.Add(cllm);
                                    nl.Labels.Add(new LinkLabelModel(nl, inPort.Name, 0.8));
                                    nl.TargetMarker = LinkMarker.Arrow;
                                    Diagram.Links.Add(nl);
                                    Diagram.Links.Remove(l);
                                    sourcePort.Visibility = CapnpFbpPortModel.VisibilityState.Hidden;
                                    inPort.Visibility = CapnpFbpPortModel.VisibilityState.Dashed;
                                    sourcePort.Refresh();
                                    inPort.Refresh();
                                };
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        break;
                    }
                    case CapnpFbpIipPortModel iipPortModel:
                    {
                        var targetPort = l.Target.Model as CapnpFbpPortModel;
                        l.Labels.Add(new LinkLabelModel(l, targetPort?.Name ?? "IN", 0.8));
                        l.TargetMarker = LinkMarker.Arrow;
                        l.TargetChanged += (link, oldTarget, newTarget) =>
                        {
                            if (newTarget.Model is not CapnpFbpPortModel inPort) return;
                            var nl = new RememberCapnpPortsLinkModel(iipPortModel.Parent,
                                inPort.Parent)
                            {
                                OutPortModel = iipPortModel,
                                InPortModel = inPort,
                            };
                            nl.Labels.Add(new LinkLabelModel(nl, inPort.Name, 0.8));
                            var cllm = new ChannelLinkLabelModel(nl, "Channel", 0.5);
                            if (InteractiveMode) CreateChannel(iipPortModel, inPort);
                            InteractiveModeChanged += cllm.ToggleInteractiveMode;
                            nl.Labels.Add(cllm);
                            nl.TargetMarker = LinkMarker.Arrow;
                            Diagram.Links.Add(nl);
                            Diagram.Links.Remove(l);
                            inPort.Visibility = CapnpFbpPortModel.VisibilityState.Dashed;
                            inPort.Refresh();
                            iipPortModel.Parent.RefreshAll();
                        };
                        break;
                    }
                }

                // Console.WriteLine($"Links.Added, LinkId={l.Id}, Source={l.Source}, Target={l.Target}");
                // events.Add($"Links.Added, LinkId={l.Id}");
            };

            // Diagram.Links.Removed += (l) => events.Add($"Links.Removed, LinkId={l.Id}");

            // Diagram.PointerDown += (m, e) =>
            // {
            //     //Console.WriteLine($"MouseDown, Type={m?.GetType().Name}, ModelId={m?.Id}, Position=({e.ClientX}/{e.ClientY}");
            //     events.Add($"MouseDown, Type={m?.GetType().Name}, ModelId={m?.Id}");
            //     StateHasChanged();
            // };

            // Diagram.PointerUp += (m, e) =>
            // {
            //     events.Add($"MouseUp, Type={m?.GetType().Name}, ModelId={m?.Id}");
            //     StateHasChanged();
            // };

            // Diagram.PointerEnter += (m, e) =>
            // {
            //     //Console.WriteLine($"TouchStart, Type={m?.GetType().Name}, ModelId={m?.Id}, Position=({e.ClientX}/{e.ClientY}");
            //     events.Add($"TouchStart, Type={m?.GetType().Name}, ModelId={m?.Id}");
            //     StateHasChanged();
            // };

            // Diagram.PointerLeave += (m, e) =>
            // {
            //     events.Add($"TouchEnd, Type={m?.GetType().Name}, ModelId={m?.Id}");
            //     StateHasChanged();
            // };

            Diagram.PointerClick += (m, e) =>
            {
                if (m is CapnpFbpPortModel port)
                {
                    var relativePt = Diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);

                    // find closest port, assuming the user will click on the label he actually wants to change
                    var node = new PortOptionsNode(relativePt)
                    {
                        Label = $"Change {port.Name}",
                        PortModel = port,
                        NodeModel = port.Parent as CapnpFbpComponentModel,
                        Container = Diagram
                    };
                    Diagram.Nodes.Add(node);
                }
                
                //Console.WriteLine($"MouseClick, Type={m?.GetType().Name}, ModelId={m?.Id}, Position=({e.ClientX}/{e.ClientY}");
                //events.Add($"MouseClick, Type={m?.GetType().Name}, ModelId={m?.Id}");
                StateHasChanged();
            };

            Diagram.PointerDoubleClick += (m, e) =>
            {
                if (m is LinkModel link)
                {
                    if (link.Source.Model is CapnpFbpPortModel source &&
                        link.Target.Model is CapnpFbpPortModel target)
                    {
                        var relativePt = Diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);

                        // find closest port, assuming the user will click on the label he actually wants to change
                        var sourceToPoint = relativePt.DistanceTo(source.MiddlePosition);
                        var targetToPoint = relativePt.DistanceTo(target.MiddlePosition);
                        var labelIndex = sourceToPoint < targetToPoint ? 0 : 1; 
                        var node = new UpdatePortNameNode(relativePt)
                        {
                            Label = $"Change {link.Labels[labelIndex].Content}",
                            LabelModel = link.Labels[labelIndex],
                            PortModel = sourceToPoint < targetToPoint ? source : target,
                            Container = Diagram
                        };
                        Diagram.Nodes.Add(node);
                    }
                }

                // Console.WriteLine(
                //     $"MouseDoubleClick, Type={m?.GetType().Name}, ModelId={m?.Id}, Position=({e.ClientX}/{e.ClientY}");
                // events.Add($"MouseDoubleClick, Type={m?.GetType().Name}, ModelId={m?.Id}");
                StateHasChanged();
            };
        }

        protected void AddNode()
        {
            var x = Random.Next(0, (int)Diagram.Container.Width - 120);
            var y = Random.Next(0, (int)Diagram.Container.Height - 100);
            AddNode(x, y);
        }

        protected void AddNode(double x, double y)
        {
            var node = new CapnpFbpComponentModel(new Point(x, y));
            Diagram.Nodes.Add(node);
        }

        protected void AddDefaultNode()
        {
            var x = Random.Next(0, (int)Diagram.Container.Width - 120);
            var y = Random.Next(0, (int)Diagram.Container.Height - 100);
            Diagram.Nodes.Add(new NodeModel(new Point(x, y)));
        }

        protected void RemoveNode()
        {
            var node = Diagram.Nodes.FirstOrDefault(n => n.Selected);
            if (node == null) return;
            Diagram.Nodes.Remove(node);
        }

        protected async Task LoadFlow(IBrowserFile file)
        {
            var s = file.OpenReadStream();
            if (s.Length > 1*1024*1024) return; // 1 MB
            var dia = JObject.Parse(await new StreamReader(s).ReadToEndAsync());
            var oldNodeIdToNewNode = new Dictionary<string, NodeModel>();
            foreach (var node in dia["nodes"] ?? new JArray())
            {
                if (node is not JObject nodeObj) continue;

                var position = new Point(
                    nodeObj["location"]?["x"]?.Value<double>() ?? 0,
                    nodeObj["location"]?["y"]?.Value<double>() ?? 0);

                var compId = nodeObj["component_id"]?.ToString() ?? "";
                Component component;
                if (string.IsNullOrEmpty(compId) && nodeObj.TryGetValue("component", out var compDesc))
                {
                    component = CreateFromJson(compDesc);
                }
                else if (!_componentId2Component.TryGetValue(compId, out component))
                {
                    component = nodeObj.ContainsKey("content") ? _componentId2Component["iip"] : _componentId2Component["empty_component"];
                }
                var diaNode = AddFbpNode(position, component, nodeObj);
                oldNodeIdToNewNode.Add(nodeObj["node_id"]?.ToString() ?? "", diaNode);
            }


            foreach (var link in dia["links"] ?? new JArray())
            {
                if (link["source"] is not JObject source || link["target"] is not JObject target) continue;
                
                var sourcePortName = source["port"]?.ToString();
                var targetPortName = target["port"]?.ToString();
                if (sourcePortName == null || targetPortName == null) continue;
                
                var sourceNode = oldNodeIdToNewNode[source["node_id"]?.ToString() ?? ""];
                var targetNode = oldNodeIdToNewNode[target["node_id"]?.ToString() ?? ""];
                if (sourceNode == null || sourceNode.Ports.Count == 0 || 
                    targetNode == null || targetNode.Ports.Count == 0) continue;

                var sourcePort = sourceNode.Ports.Where(p => 
                        p is CapnpFbpPortModel capnpPort && capnpPort.Name == sourcePortName)
                    .DefaultIfEmpty(null).First();
                var noOfSourcePorts = sourceNode.Ports.Count(p => p is CapnpFbpPortModel { ThePortType: CapnpFbpPortModel.PortType.Out });
                var targetPort = targetNode.Ports.Where(p => 
                        p is CapnpFbpPortModel capnpPort && capnpPort.Name == targetPortName)
                    .DefaultIfEmpty(null).First();
                var noOfTargetPorts = sourceNode.Ports.Count(p => p is CapnpFbpPortModel { ThePortType: CapnpFbpPortModel.PortType.In });
                // might be an IIP
                sourcePort ??= sourceNode.Ports.Where(p =>
                        p is CapnpFbpIipPortModel iipPort && iipPort.Alignment.ToString() == sourcePortName)
                    .DefaultIfEmpty(null).First();
                if (sourcePort == null && sourceNode is CapnpFbpComponentModel sn)
                {
                    sourcePort = AddPortControl.CreateAndAddPort(sn, CapnpFbpPortModel.PortType.Out, noOfSourcePorts+1, sourcePortName);
                }
                if (targetPort == null  && targetNode is CapnpFbpComponentModel tn)
                {
                    targetPort = AddPortControl.CreateAndAddPort(tn, CapnpFbpPortModel.PortType.In, noOfTargetPorts+1, targetPortName);
                }
                //if (sourcePort == null || targetPort == null) continue;
                //Diagram.Links.Add(new LinkModel(sourcePort, targetPort));

                if (sourcePort is CapnpFbpPortModel scp) scp.Visibility = CapnpFbpPortModel.VisibilityState.Hidden;
                if (targetPort is CapnpFbpPortModel tcp) tcp.Visibility = CapnpFbpPortModel.VisibilityState.Dashed;
                
                var l = new RememberCapnpPortsLinkModel(sourceNode, targetNode) {
                    OutPortModel = sourcePort,
                    InPortModel = targetPort
                };
                if (sourceNode is not CapnpFbpIipModel)
                {
                    l.Labels.Add(new LinkLabelModel(l, sourcePortName, 0.2));
                }
                var cllm = new ChannelLinkLabelModel(l, "Channel", 0.5);
                InteractiveModeChanged += cllm.ToggleInteractiveMode;
                l.Labels.Add(cllm);
                l.Labels.Add(new LinkLabelModel(l, targetPortName, 0.8));
                l.TargetMarker = LinkMarker.Arrow;
                Diagram.Links.Add(l);
            }
        }

        protected async Task SaveFlow(bool asMermaid)
        {
            var dia = asMermaid ? null
                : JObject.Parse(await File.ReadAllTextAsync("Data/diagram_template.json"));
            HashSet<string> linkSet = [];
            StringBuilder sb = new();
            if (asMermaid) sb.AppendLine(await File.ReadAllTextAsync("Data/diagram_template.mmd"));
            var procIdCount = 1;
            var iipIdCount = 1;
            Dictionary<string, int> uuid2ShortProcId = new();
            Dictionary<string, int> uuid2ShortIipId = new();
            int ShortProcId(string oldId)
            {
                if (!uuid2ShortProcId.ContainsKey(oldId)) uuid2ShortProcId.Add(oldId, procIdCount++);
                return uuid2ShortProcId[oldId];
            }
            int ShortIipId(string oldId)
            {
                if (!uuid2ShortIipId.ContainsKey(oldId)) uuid2ShortIipId.Add(oldId, iipIdCount++);
                return uuid2ShortIipId[oldId];
            }

            foreach(var node in Diagram.Nodes)
            {
                switch (node)
                {
                    case CapnpFbpComponentModel fbpNode:
                    {
                        var nodeId = $"P{ShortProcId(fbpNode.Id)}";
                        if (asMermaid) sb.AppendLine($"{nodeId}(\"{fbpNode.ProcessName}\")");
                        else
                        {
                            // var exampleConfig = new JObject();
                            // foreach (var line in fbpNode.DefaultConfigString.Split('\n'))
                            // {
                            //     var kv = line.Split('=');
                            //     var k = kv[0].Trim();
                            //     var v = kv.Length == 2 ? kv[1].Trim() : "";
                            //     if (k.Length > 0 && v.Length > 0) exampleConfig.Add(k, v);
                            // }

                            var jn = new JObject()
                            {
                                { "node_id", nodeId },
                                { "process_name", fbpNode.ProcessName },
                                {
                                    "location",
                                    new JObject() { { "x", fbpNode.Position.X }, { "y", fbpNode.Position.Y } }
                                },
                                { "editable", fbpNode.Editable },
                                { "parallel_processes", fbpNode.InParallelCount },
                            };
                            if (string.IsNullOrEmpty(fbpNode.ComponentId) ||
                                !_componentId2Component.ContainsKey(fbpNode.ComponentId))
                            {
                                // create inputs
                                var inputs = fbpNode.Ports.Where(p => p is CapnpFbpPortModel cp
                                                                      && cp.ThePortType ==
                                                                      CapnpFbpPortModel.PortType.In)
                                    .Select(p => p as CapnpFbpPortModel)
                                    .Select(p => new JObject() { { "name", p!.Name } });

                                //create outputs
                                var outputs = fbpNode.Ports.Where(p => p is CapnpFbpPortModel cp
                                                                       && cp.ThePortType ==
                                                                       CapnpFbpPortModel.PortType.Out)
                                    .Select(p => p as CapnpFbpPortModel)
                                    .Select(p => new JObject() { { "name", p!.Name } });

                                jn.Add("component", new JObject()
                                {
                                    {
                                        "info", new JObject()
                                        {
                                            { "id", fbpNode.ComponentId },
                                            { "name", fbpNode.ComponentName },
                                            { "description", fbpNode.ShortDescription }
                                        }
                                    },
                                    { "type", "standard" },
                                    { "inPorts", new JArray(inputs) },
                                    { "outPorts", new JArray(outputs) },
                                    { "cmd", fbpNode.Cmd },
                                });
                            }
                            else
                            {
                                jn.Add("component_id", fbpNode.ComponentId);
                            }

                            if (dia["nodes"] is JArray nodes) nodes.Add(jn);
                        }
                        break;
                    }
                    case CapnpFbpIipModel iipNode:
                    {
                        var iipNodeId = $"IIP{ShortIipId(iipNode.Id)}";
                        if (asMermaid) sb.AppendLine($"{iipNodeId}[[\"{iipNode.Content}\"]]");
                        else
                        {
                            var jn = new JObject()
                            {
                                { "node_id", iipNodeId },
                                { "component_id", iipNode.ComponentId },
                                {
                                    "location",
                                    new JObject() { { "x", iipNode.Position.X }, { "y", iipNode.Position.Y } }
                                },
                                { "content", iipNode.Content }
                            };
                            if (dia["nodes"] is JArray nodes) nodes.Add(jn);
                        }
                        break;
                    }
                    default:
                        continue;
                }
                
                foreach (var pl in node.PortLinks.Concat(node.Links))
                {
                    if (!pl.IsAttached || pl is not RememberCapnpPortsLinkModel rcplm) continue;

                    CapnpFbpIipPortModel outIipPort = null;
                    CapnpFbpPortModel outCapnpPort = null;
                    CapnpFbpPortModel inCapnpPort = null;
                    switch (rcplm.InPortModel) {
                        case CapnpFbpPortModel inCapnpPort2
                            when rcplm.OutPortModel is CapnpFbpIipPortModel outIipPort2:
                        {
                            outIipPort = outIipPort2;
                            inCapnpPort = inCapnpPort2;
                            break;
                        }
                        case CapnpFbpPortModel inCapnpPort2
                            when rcplm.OutPortModel is CapnpFbpPortModel outCapnpPort2:
                            outCapnpPort = outCapnpPort2;
                            inCapnpPort = inCapnpPort2;
                            break;
                    }

                    if (outIipPort != null && inCapnpPort != null)
                    {
                        var outIipNodeId = $"P{ShortIipId(outIipPort.Parent.Id)}";
                        var inNodeId = $"P{ShortProcId(inCapnpPort.Parent.Id)}";
                        if (asMermaid)
                        {
                            sb.AppendLine($"{outIipNodeId} -- \"" +
                                          $"{inCapnpPort.Name}\" --> {inNodeId}");
                        }
                        else
                        {
                            // make sure the link is only stored once
                            var checkOut = $"{outIipNodeId}.{outIipPort.Alignment.ToString()}";
                            var checkIn = $"{inNodeId}.{inCapnpPort.Name}";
                            if (linkSet.Contains($"{checkOut}->{checkIn}") ||
                                linkSet.Contains($"{checkIn}->{checkOut}")) continue;
                            linkSet.Add($"{checkOut}->{checkIn}");

                            var jl = new JObject()
                            {
                                {
                                    "source", new JObject()
                                    {
                                        { "node_id", outIipNodeId },
                                        { "port", outIipPort.Alignment.ToString() }
                                    }
                                },
                                {
                                    "target", new JObject()
                                    {
                                        { "node_id", inNodeId },
                                        { "port", inCapnpPort.Name }
                                    }
                                }
                            };
                            if (dia["links"] is JArray links) links.Add(jl);
                        }
                    } 
                    else if (outCapnpPort != null && inCapnpPort != null)
                    {
                        var outNodeId = $"P{ShortProcId(outCapnpPort.Parent.Id)}";
                        var inNodeId = $"P{ShortProcId(inCapnpPort.Parent.Id)}";
                        if (asMermaid)
                        {
                            sb.AppendLine($"{outNodeId} -- \"{outCapnpPort.Name} : {inCapnpPort.Name}\" " +
                                          $"--> {inNodeId}");
                        }
                        else
                        {
                            // make sure the link is only stored once
                            var checkOut = $"{outNodeId}.{outCapnpPort.Name}";
                            var checkIn = $"{inNodeId}.{inCapnpPort.Name}";
                            if (linkSet.Contains($"{checkOut}->{checkIn}") ||
                                linkSet.Contains($"{checkIn}->{checkOut}")) continue;
                            linkSet.Add($"{checkOut}->{checkIn}");

                            var jl = new JObject()
                            {
                                {
                                    "source", new JObject()
                                    {
                                        { "node_id", outNodeId },
                                        { "port", outCapnpPort.Name }
                                    }
                                },
                                {
                                    "target", new JObject()
                                    {
                                        { "node_id", inNodeId },
                                        { "port", inCapnpPort.Name }
                                    }
                                }
                            };
                            if (dia["links"] is JArray links) links.Add(jl);
                        }
                    }
                }
            }
            
            //File.WriteAllText("Data/diagram_new.json", dia.ToString());
            await JsRuntime.InvokeVoidAsync("saveAsBase64", "flow." + (asMermaid ? "mmd" : "json"),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(asMermaid ? sb.ToString() : dia?.ToString())));
        }

        protected async Task SaveMermaidFlow()
        {
            var dia = await File.ReadAllTextAsync("Data/diagram_template.mmd");
            HashSet<string> linkSet = new();
            StringBuilder sb = new();
            sb.AppendLine(dia);
            foreach(var node in Diagram.Nodes)
            {
                switch (node)
                {
                    case CapnpFbpComponentModel fbpNode:
                    {
                        sb.AppendLine($"{fbpNode.Id}(\"{fbpNode.ProcessName}\")");
                        // var jn = new JObject()
                        // {
                        //     { "node_id", fbpNode.Id },
                        //     { "process_name", fbpNode.ProcessName },
                        //     { "location", new JObject() { { "x", fbpNode.Position.X }, { "y", fbpNode.Position.Y } } },
                        //     { "editable", fbpNode.Editable },
                        //     { "parallel_processes", fbpNode.InParallelCount },
                        // };
                        // if (string.IsNullOrEmpty(fbpNode.ComponentId) ||
                        //     !_componentId2Component.ContainsKey(fbpNode.ComponentId))
                        // {
                        //     // create inputs
                        //     var inputs = fbpNode.Ports.Where(p => p is CapnpFbpPortModel cp
                        //                                           && cp.ThePortType == CapnpFbpPortModel.PortType.In)
                        //         .Select(p => p as CapnpFbpPortModel)
                        //         .Select(p => new JObject() { { "name", p!.Name } });
                        //
                        //     //create outputs
                        //     var outputs = fbpNode.Ports.Where(p => p is CapnpFbpPortModel cp
                        //                                            && cp.ThePortType == CapnpFbpPortModel.PortType.Out)
                        //         .Select(p => p as CapnpFbpPortModel)
                        //         .Select(p => new JObject() { { "name", p!.Name } });
                        //
                        //     jn.Add("component", new JObject()
                        //     {
                        //         { "info", new JObject()
                        //         {
                        //             { "id", fbpNode.ComponentId },
                        //             { "name", fbpNode.ComponentName },
                        //             { "description", fbpNode.ShortDescription }
                        //         }},
                        //         { "type", "standard" },
                        //         { "inPorts", new JArray(inputs) },
                        //         { "outPorts", new JArray(outputs) },
                        //         { "cmd", fbpNode.Cmd },
                        //     });
                        // }
                        // else
                        // {
                        //     jn.Add("component_id", fbpNode.ComponentId);
                        // }
                        //
                        // if (dia["nodes"] is JArray nodes) nodes.Add(jn);
                        break;
                    }
                    case CapnpFbpIipModel iipNode:
                    {
                        sb.AppendLine($"{iipNode.Id}[[\"{iipNode.Content}\"]]");
                        break;
                    }
                    default:
                        continue;
                }

                foreach (var pl in node.PortLinks.Concat(node.Links))
                {
                    if (!pl.IsAttached || pl is not RememberCapnpPortsLinkModel rcplm) continue;

                    CapnpFbpIipPortModel outIipPort = null;
                    CapnpFbpPortModel outCapnpPort = null;
                    CapnpFbpPortModel inCapnpPort = null;
                    switch (rcplm.InPortModel) {
                        case CapnpFbpPortModel inCapnpPort2
                            when rcplm.OutPortModel is CapnpFbpIipPortModel outIipPort2:
                        {
                            outIipPort = outIipPort2;
                            inCapnpPort = inCapnpPort2;
                            break;
                        }
                        case CapnpFbpPortModel inCapnpPort2
                            when rcplm.OutPortModel is CapnpFbpPortModel outCapnpPort2:
                            outCapnpPort = outCapnpPort2;
                            inCapnpPort = inCapnpPort2;
                            break;
                    }

                    if (outIipPort != null)
                    {
                        // make sure the link is only stored once
                        var checkOut = $"{outIipPort.Parent.Id}.{outIipPort.Alignment.ToString()}";
                        var checkIn = $"{inCapnpPort.Parent.Id}.{inCapnpPort.Name}";
                        if (linkSet.Contains($"{checkOut}->{checkIn}") ||
                            linkSet.Contains($"{checkIn}->{checkOut}")) continue;
                        linkSet.Add($"{checkOut}->{checkIn}");
                        sb.AppendLine($"{outIipPort.Parent.Id} -- \"{inCapnpPort.Name}\" --> {inCapnpPort.Parent.Id}");
                    }
                    else if (outCapnpPort != null)
                    {
                        // make sure the link is only stored once
                        var checkOut = $"{outCapnpPort.Parent.Id}.{outCapnpPort.Name}";
                        var checkIn = $"{inCapnpPort.Parent.Id}.{inCapnpPort.Name}";
                        if (linkSet.Contains($"{checkOut}->{checkIn}") ||
                            linkSet.Contains($"{checkIn}->{checkOut}")) continue;
                        linkSet.Add($"{checkOut}->{checkIn}");
                        sb.AppendLine($"{outCapnpPort.Parent.Id} -- \"{outCapnpPort.Name} : {inCapnpPort.Name}\" --> {inCapnpPort.Parent.Id}");
                    }
                }
            }
            await JsRuntime.InvokeVoidAsync("saveAsBase64", "diagram.mmd",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public async Task ClearDiagram()
        {
            foreach(var node in Diagram.Nodes)
            {
                if (node is IDisposable disposable) disposable.Dispose();
            }
            Diagram.Nodes.Clear();
            Diagram.Refresh();
        }

        public async Task ExecuteFlow()
        {
            foreach(var node in Diagram.Nodes)
            {
                if (node is not CapnpFbpComponentModel fbpNode) continue;
                await fbpNode.StartProcess(ConMan, true);
                fbpNode.Refresh();
            }
            StateHasChanged();
        }
        
        //private JObject _draggedComponent;
        private Mas.Schema.Fbp.Component _draggedComponent;
        
        private void OnNodeDragStart(Mas.Schema.Fbp.Component component)//(JObject component)//string nodeType, string nodeName)
        {
            _draggedComponent = component;
        }

        private void OnNodeDrop(DragEventArgs e)
        {
            if (_draggedComponent == null) return;
            var position = Diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);
            AddFbpNode(position, _draggedComponent);
            _draggedComponent = null;
        }
        
        private NodeModel AddFbpNode(Point position, Mas.Schema.Fbp.Component component, JObject initNode = null)
        {
            switch (component.Type)
            {
                case Component.ComponentType.standard:
                {
                    // var defaultConfig = new StringBuilder();
                    // var compDefaultConfig = component["default_config"] as JObject ?? new JObject();
                    // var paramNames = ((IDictionary<string, JToken>)compDefaultConfig).Keys.ToHashSet();
                    // foreach(var paramName in paramNames)
                    // {
                    //     defaultConfig.Append(paramName);
                    //     defaultConfig.Append('=');
                    //     //if(initCmdParams[paramName])
                    //     defaultConfig.Append(compDefaultConfig.GetValue(paramName) ?? compDefaultConfig["default"] ?? "");
                    //     defaultConfig.AppendLine();
                    // }

                    var cmd = "";//component["cmd"]?.ToString() ?? "";
                    var compId = component.Info.Id;
                    var procName = initNode?.GetValue("process_name")?.Type switch
                    {
                        JTokenType.Null => null,
                        _ => initNode?.GetValue("process_name")?.ToString()
                    };
                    
                    var node = new CapnpFbpComponentModel(new Point(position.X, position.Y))
                    {
                        ComponentId = compId,
                        ComponentName = component.Info.Name ?? compId,
                        ProcessName = procName ?? $"{component.Info.Name ?? "new"} {CapnpFbpComponentModel.ProcessNo++}",
                        Cmd = cmd,
                        ShortDescription = component.Info.Description ?? "",
                        DefaultConfigString = "",//component["default_config"]?.ToString() ?? "", //defaultConfig.ToString(),
                        Editable = initNode?.GetValue("editable")?.Value<bool>() ?? component.Run == null,
                        InParallelCount = initNode?.GetValue("parallel_processes")?.Value<int>() ?? 1,
                        ChannelStarterService = _channelStarterService != null ? Capnp.Rpc.Proxy.Share(_channelStarterService) : null,
                    };
                    if (component.Run != null) node.Runnable = Proxy.Share(component.Run);
                    // var pcbRegistrar = new PcbRegistrar(node);
                    // var res = _restorer.SaveStr(BareProxy.FromImpl(pcbRegistrar));
                    // var pcbRegistrarSr = res.Item1;
                    // node.PortCallbackRegistarSr = pcbRegistrarSr;
                    // _pcbRegistrars.Add(pcbRegistrar);
                    
                    Diagram.Controls.AddFor(node).Add(new AddPortControl(0.2, 0, -33, -50)
                    {
                        Label = "IN",
                        PortType = CapnpFbpPortModel.PortType.In,
                        NodeModel = node,
                    });
                    Diagram.Controls.AddFor(node).Add(new AddPortControl(0.8, 0, -41, -50)
                    {
                        Label = "OUT",
                        PortType = CapnpFbpPortModel.PortType.Out,
                        NodeModel = node,
                    });
                    Diagram.Controls.AddFor(node).Add(new RemoveProcessControl(0.5, 0, -20, -50));
                    Diagram.Controls.AddFor(node).Add(new ToggleEditNodeControl(1.1, 0, -20, -50)
                    {
                        NodeModel = node
                    });
                    
                    var portLocations = initNode?["location"]?["ports"] as JObject;

                    foreach(var (i, input) in component.InPorts.Select((inp, i) => (i, inp)))
                    {
                        AddPortControl.CreateAndAddPort(node, CapnpFbpPortModel.PortType.In, i, input.Name);
                    }
                    
                    foreach(var (i, output) in component.OutPorts.Select((outp, i) => (i, outp)))
                    {
                        AddPortControl.CreateAndAddPort(node, CapnpFbpPortModel.PortType.Out, i, output.Name);
                    }
                    Diagram.Nodes.Add(node);
                    return node;
                }
                case Component.ComponentType.iip:
                {
                    var compId = component.Info.Id;
                    var node = new CapnpFbpIipModel(new Point(position.X, position.Y))
                    {
                        ComponentId = compId,
                        Content = initNode?["content"]?.ToString() ?? ""
                    };
                    Diagram.Nodes.Add(node);
                    Diagram.Controls.AddFor(node).Add(new RemoveProcessControl(0.5, 0, -20, -50));
                    node.AddPort(new CapnpFbpIipPortModel(node, PortAlignment.Top));
                    node.AddPort(new CapnpFbpIipPortModel(node, PortAlignment.Bottom));
                    node.AddPort(new CapnpFbpIipPortModel(node, PortAlignment.Left));
                    node.AddPort(new CapnpFbpIipPortModel(node, PortAlignment.Right));
                    node.RefreshAll();
                    return node;
                }
            }

            return null;
        }
    }
}