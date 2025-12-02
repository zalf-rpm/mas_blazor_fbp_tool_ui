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

        private readonly Restorer _restorer = new() { TcpHost = ConnectionManager.GetLocalIPAddress() };

        private const string NoRegistryServiceId = "no_service";

        private Dictionary<string, Mas.Schema.Registry.IRegistry> ServiceId2Registries { get; } = [];
        public Dictionary<string, (string, string)> RegistryServiceIdToPetNameAndSturdyRef { get; } = [];

        private Dictionary<string, Mas.Schema.Fbp.IStartChannelsService> ServiceId2ChannelStarterServices { get; } = [];
        private Dictionary<string, (string, string)> ChannelServiceIdToPetNameAndSturdyRef { get; } = [];

        private Dictionary<ulong, System.Type> InterfaceIdToType { get; } = new () {
            { Shared.Shared.RegistryInterfaceId, typeof(Mas.Schema.Registry.IRegistry) },
            { Shared.Shared.ChannelStarterInterfaceId, typeof(Mas.Schema.Fbp.IStartChannelsService) },
        };

        private Dictionary<string, Proxy> SturdyRef2Services { get; } = [];

        public Mas.Schema.Fbp.IStartChannelsService CurrentChannelStarterService =>
            ServiceId2ChannelStarterServices.FirstOrDefault(new KeyValuePair<string, IStartChannelsService>("none", null)).Value;

        private readonly Dictionary<string, HashSet<(string, string)>> CatId2CompServiceIdAndComponentIds = new();
        private readonly Dictionary<string, Mas.Schema.Common.IdInformation> CatId2Info = new();
        private readonly Dictionary<(string, string), Mas.Schema.Fbp.Component> ServiceIdAndComponentId2Component = new();


        private async Task<IStartChannelsService> ConnectToStartChannelsService(
            ConnectionManager conMan,
            string petName,
            string sturdyRef)
        {
            try
            {
                var service = await conMan.Connect<Mas.Schema.Fbp.IStartChannelsService>(sturdyRef);
                if (service == null) return null;
                var info = await service.Info();
                Console.WriteLine("Connected to channel starter service @ " + sturdyRef);
                //var iid = GetInterfaceId<IStartChannelsService>();
                var petName2 = Shared.Shared.MakeUniqueKey(ServiceId2ChannelStarterServices, petName ?? "chan_start_serv");
                ServiceId2ChannelStarterServices[info.Id] = Capnp.Rpc.Proxy.Share(service);
                SturdyRef2Services[sturdyRef] = Capnp.Rpc.Proxy.Share(service) as Proxy;
                ChannelServiceIdToPetNameAndSturdyRef[info.Id] = (petName2, sturdyRef);
                return service;
            }
            catch (Capnp.Rpc.RpcException)
            {
                Console.WriteLine("Couldn't connect to channel starter service @ " + sturdyRef);
            }
            return null;
        }

        private async Task<Mas.Schema.Registry.IRegistry> ConnectToRegistryService(
            ConnectionManager conMan,
            string petName,
            string sturdyRef)
        {
            Mas.Schema.Registry.IRegistry reg = null;
            try
            {
                reg = await conMan.Connect<Mas.Schema.Registry.IRegistry>(sturdyRef);
                if  (reg == null) return null;
                var info = await reg.Info();
                Console.WriteLine("Connected to components registry @ " + sturdyRef);
                //var iid = GetInterfaceId<IRegistry>();
                var petName2 = Shared.Shared.MakeUniqueKey(ServiceId2Registries, petName ?? "reg_serv");
                ServiceId2Registries[info.Id] = Capnp.Rpc.Proxy.Share(reg);
                SturdyRef2Services[sturdyRef] = Capnp.Rpc.Proxy.Share(reg) as Proxy;
                RegistryServiceIdToPetNameAndSturdyRef[info.Id] = (petName2, sturdyRef);
                Console.WriteLine("added petName2: " + petName2 + " and sturdyRef: " + sturdyRef);
            }
            catch (Capnp.Rpc.RpcException)
            {
                Console.WriteLine("Couldn't connect to components registry @ " + sturdyRef);
                return null;
            }

            await LoadComponentsFromRegistry(reg, sturdyRef);
            return reg;
        }

        private async Task LoadComponentsFromRegistry(Mas.Schema.Registry.IRegistry reg, string sturdyRef)
        {
            if (reg ==  null) return;
            try
            {
                var categories = await reg.SupportedCategories();
                foreach (var cat in categories)
                {
                    if (!CatId2Info.ContainsKey(cat.Id))
                        CatId2Info[cat.Id] = new IdInformation
                        {
                            Id = cat.Id, Name = cat.Name ?? cat.Id,
                            Description = cat.Description ?? cat.Name ?? cat.Id
                        };
                }
                Console.WriteLine("Loaded supported categories from " + sturdyRef);
            }
            catch (Capnp.Rpc.RpcException)
            {
                Console.WriteLine("Error loading supported categories from " + sturdyRef);
            }

            try
            {
                var info = await reg.Info();
                var entries = await reg.Entries(null);
                foreach (var e in entries)
                {
                    if (!CatId2CompServiceIdAndComponentIds.ContainsKey(e.CategoryId)) CatId2CompServiceIdAndComponentIds[e.CategoryId] = [];
                    CatId2CompServiceIdAndComponentIds[e.CategoryId].Add((info.Id, e.Id));
                    if (e.Ref is not Proxy p) continue;
                    var holder = p.Cast<Mas.Schema.Common.IIdentifiableHolder<Mas.Schema.Fbp.Component>>(true);
                    try
                    {
                        ServiceIdAndComponentId2Component.Add((info.Id, e.Id), await holder.Value());
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
                Console.WriteLine("Loaded entries from " + sturdyRef);
            }
            catch (Capnp.Rpc.RpcException)
            {
                Console.WriteLine("Error loading entries from " + sturdyRef);
            }
        }


        private static Component CreateFromJson(JToken jComp)
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
                    "view" => Component.ComponentType.view,
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
            RegistryServiceIdToPetNameAndSturdyRef[NoRegistryServiceId] = ("No service", null);
            foreach (var cat in defComps["categories"] ?? new JArray())
            {
                var catId = cat["id"]?.ToString() ?? "";
                if (catId.Length == 0) continue;
                CatId2Info[catId] = new IdInformation
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

                if (!CatId2CompServiceIdAndComponentIds.TryGetValue(catId, out var value))
                {
                    value = [];
                    CatId2CompServiceIdAndComponentIds[catId] = value;
                }
                if (!CatId2Info.ContainsKey(catId)) CatId2Info[catId] = new IdInformation
                {
                    Id = catId, Name = catId
                };

                var component = CreateFromJson(entry["component"]);
                if (component == null) continue;
                value.Add((NoRegistryServiceId, component.Info.Id));
                ServiceIdAndComponentId2Component[(NoRegistryServiceId, component.Info.Id)] = component;
            }

            Diagram.RegisterComponent<CapnpFbpComponentModel, CapnpFbpComponentWidget>();
            Diagram.RegisterComponent<CapnpFbpViewComponentModel, CapnpFbpViewComponentWidget>();
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
            var allBookmarks = await StoredSrData.GetAllData(LocalStorage);
            allBookmarks.Sort();

            // iterate over all bookmarks and connect to all auto connect sturdy refs
            foreach (var ssrd in allBookmarks.Where(ssrd => ssrd.AutoConnect))
            {
                if (ssrd.InterfaceId == BlazorDrawFBP.Shared.Shared.ChannelStarterInterfaceId)
                {
                    await ConnectToStartChannelsService(ConMan, ssrd.PetName, ssrd.SturdyRef);
                }
                else if (ssrd.InterfaceId == BlazorDrawFBP.Shared.Shared.RegistryInterfaceId)
                {
                    await ConnectToRegistryService(ConMan, ssrd.PetName, ssrd.SturdyRef);
                }
            }
            StateHasChanged();
        }

        protected override async Task OnInitializedAsync()
        {
        }

        private void CreateChannel(PortModel outPort, CapnpFbpPortModel inPort)
        {
            if (CurrentChannelStarterService == null) return;
            BlazorDrawFBP.Shared.Shared.CreateChannel(ConMan, CurrentChannelStarterService, outPort, inPort);
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
                                l.Labels.Add(new LinkLabelModel(l, targetPort?.Name ?? "out", 0.8));
                                l.SourceMarker = LinkMarker.Arrow;
                                l.TargetChanged += (link, oldTarget, newTarget) =>
                                {
                                    if (newTarget.Model is not CapnpFbpPortModel outPort) return;
                                    var nl = new RememberCapnpPortsLinkModel(outPort.Parent, sourcePort.Parent)
                                    {
                                        OutPortModel = outPort,
                                        InPortModel = sourcePort,
                                        Color = outPort.Reader == null && outPort.Writer == null ? "#ff0000" : "#1ac12e",
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
                                l.Labels.Add(new LinkLabelModel(l, targetPort?.Name ?? "in", 0.8));
                                l.TargetMarker = LinkMarker.Arrow;
                                l.TargetChanged += (link, oldTarget, newTarget) =>
                                {
                                    if (newTarget.Model is not CapnpFbpPortModel inPort) return;
                                    var nl = new RememberCapnpPortsLinkModel(sourcePort.Parent,
                                        inPort.Parent)
                                    {
                                        OutPortModel = sourcePort,
                                        InPortModel = inPort,
                                        Color = inPort.Reader == null && inPort.Writer == null ? "#ff0000" : "#1ac12e",
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
                        l.Labels.Add(new LinkLabelModel(l, targetPort?.Name ?? "in", 0.8));
                        l.TargetMarker = LinkMarker.Arrow;
                        l.TargetChanged += (link, oldTarget, newTarget) =>
                        {
                            if (newTarget.Model is not CapnpFbpPortModel inPort) return;
                            var nl = new RememberCapnpPortsLinkModel(iipPortModel.Parent,
                                inPort.Parent)
                            {
                                OutPortModel = iipPortModel,
                                InPortModel = inPort,
                                Color = inPort.Reader == null && inPort.Writer == null ? "#ff0000" : "#1ac12e",
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

        private void AddNode(double x, double y)
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

            Diagram.SuspendRefresh = true;
            foreach (var node in dia["nodes"] ?? new JArray())
            {
                if (node is not JObject nodeObj) continue;

                var position = new Point(
                    nodeObj["location"]?["x"]?.Value<double>() ?? 0,
                    nodeObj["location"]?["y"]?.Value<double>() ?? 0);

                var compId = nodeObj["componentId"]?.ToString() ?? nodeObj["component_id"]?.ToString() ?? "";
                var compServiceId = nodeObj["componentServiceId"]?.ToString() ?? NoRegistryServiceId;
                Component component;
                var cmd = "";
                if (string.IsNullOrEmpty(compId) && nodeObj.TryGetValue("component", out var compDesc))
                {
                    component = CreateFromJson(compDesc);
                    cmd = compDesc["cmd"]?.ToString() ?? "";
                }
                else if (!ServiceIdAndComponentId2Component.TryGetValue((compServiceId, compId), out component))
                {
                    component = nodeObj.ContainsKey("content")
                        ? ServiceIdAndComponentId2Component[(NoRegistryServiceId, "iip")]
                        : ServiceIdAndComponentId2Component[(NoRegistryServiceId, "empty_component")];
                }
                var diaNode = AddFbpNode(position, component, nodeObj, cmd);
                oldNodeIdToNewNode.Add(nodeObj["nodeId"]?.ToString() ?? nodeObj["node_id"]?.ToString() ?? "", diaNode);
            }

            foreach (var link in dia["links"] ?? new JArray())
            {
                if (link["source"] is not JObject source || link["target"] is not JObject target) continue;
                
                var sourcePortName = source["port"]?.ToString();
                var targetPortName = target["port"]?.ToString();
                if (sourcePortName == null || targetPortName == null) continue;
                
                var sourceNode = oldNodeIdToNewNode[source["nodeId"]?.ToString() ?? source["node_id"]?.ToString() ?? ""];
                var targetNode = oldNodeIdToNewNode[target["nodeId"]?.ToString() ?? target["node_id"]?.ToString() ?? ""];
                if (sourceNode == null || sourceNode.Ports.Count == 0 || 
                    targetNode == null || targetNode.Ports.Count == 0) continue;

                var sourcePort = sourceNode.Ports.Where(p => 
                        p is CapnpFbpPortModel capnpPort && capnpPort.Name == sourcePortName)
                    .DefaultIfEmpty(null).First();
                var noOfSourcePorts = sourceNode.Ports.Count(p => p is CapnpFbpPortModel { ThePortType: CapnpFbpPortModel.PortType.Out });
                var targetPort = targetNode.Ports.Where(p => 
                        p is CapnpFbpPortModel capnpPort && capnpPort.Name == targetPortName)
                    .DefaultIfEmpty(null).First();
                var noOfTargetPorts = targetNode.Ports.Count(p => p is CapnpFbpPortModel { ThePortType: CapnpFbpPortModel.PortType.In });
                // might be an IIP
                sourcePort ??= sourceNode.Ports.Where(p =>
                        p is CapnpFbpIipPortModel iipPort && iipPort.Alignment.ToString() == sourcePortName)
                    .DefaultIfEmpty(null).First();
                if (sourcePort == null && sourceNode is CapnpFbpComponentModel sn)
                {
                    sourcePort = AddPortControl.CreateAndAddPort(sn, CapnpFbpPortModel.PortType.Out, noOfSourcePorts, sourcePortName);
                }
                if (targetPort == null  && targetNode is CapnpFbpComponentModel tn)
                {
                    targetPort = AddPortControl.CreateAndAddPort(tn, CapnpFbpPortModel.PortType.In, noOfTargetPorts, targetPortName);
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
            Diagram.SuspendRefresh = true;
            Diagram.Refresh();

            InvokeAsync(async () =>
            {
                await Task.Delay(500);
                Diagram.SuspendRefresh = true;
                Diagram.SetPan(
                    dia["pan"]?["x"]?.Value<double>() ?? 0.0,
                    dia["pan"]?["y"]?.Value<double>() ?? 0.0);
                Diagram.SetZoom(dia["zoom"]?.Value<double>() ?? 1.0);
                Diagram.SuspendRefresh = false;
                Diagram.Refresh();
            });
        }

        private const int IipIdLength = 10;
        private const int ProcIdLength = 20;
        protected async Task SaveFlow(bool asMermaid)
        {
            var dia = asMermaid ? null
                : JObject.Parse(await File.ReadAllTextAsync("Data/diagram_template.json"));
            HashSet<string> linkSet = [];
            StringBuilder sb = new();
            if (asMermaid)
            {
                sb.AppendLine(await File.ReadAllTextAsync("Data/diagram_template.mmd"));
            }
            else
            {
                dia["pan"] = new JObject { { "x", Diagram.Pan.X }, { "y", Diagram.Pan.Y } };
                dia["zoom"] = Diagram.Zoom;

                // store references to used services
                if (dia["services"]?["channels"] is JObject channels)
                {
                    foreach (var p in ServiceId2ChannelStarterServices)
                    {
                        channels[p.Key] = ChannelServiceIdToPetNameAndSturdyRef[p.Key].Item2;
                    }
                }
                if (dia["services"]?["components"] is JObject components)
                {
                    foreach (var p in ServiceId2Registries)
                    {
                        components[p.Key] = RegistryServiceIdToPetNameAndSturdyRef[p.Key].Item2;
                    }
                }
            }

            var procIdCount = 2;
            HashSet<string> shortProcIds = [];
            Dictionary<string, string> uuid2ShortProcId = new();
            string ShortProcId(string oldId, string procName)
            {
                if (uuid2ShortProcId.TryGetValue(oldId, out var value)) return value;

                var procNameShortened = procName.Length > ProcIdLength
                    ? procName.Split('\n')[0][..ProcIdLength] : procName;
                var shortProcId = procNameShortened.Length < procName.Length ? $"{procNameShortened}..." : procNameShortened;
                if (shortProcIds.Contains(shortProcId)) shortProcId = $"{shortProcId} ({procIdCount++})";
                uuid2ShortProcId[oldId] = shortProcId;
                shortProcIds.Add(shortProcId);
                return shortProcId;
            }

            var iipIdCount = 2;
            HashSet<string> shortIipIds = [];
            Dictionary<string, string> uuid2ShortIipId = new();
            string ShortIipId(string oldId, string iipContent)
            {
                if (uuid2ShortIipId.TryGetValue(oldId, out var value)) return value;

                var iipContentShortened = iipContent.Length > IipIdLength
                    ? iipContent.Split('\n')[0][..IipIdLength] : iipContent;
                var shortIipId = $"IIP [{iipContentShortened}...]";
                if (shortIipIds.Contains(shortIipId)) shortIipId = $"{shortIipId} ({iipIdCount++})";
                uuid2ShortIipId[oldId] = shortIipId;
                shortIipIds.Add(shortIipId);
                return shortIipId;
            }

            string MermaidEscapeQuotes(string str) => str.Replace("\"", "&quot;");

            string CreateMermaidId(string id)
            {
                var newId = new StringBuilder();
                foreach (var c in id) newId.Append(char.IsLetterOrDigit(c) ? c : '_');
                //now remove all repeated _ from name
                var newId2 = new StringBuilder();
                var foundUnderscore = false;
                foreach (var c in newId.ToString())
                {
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
                }
                var newId2Str = newId2.ToString();
                return newId2Str[^1] == '_' ? newId2Str[..^1] : newId2Str;
            }

            foreach(var node in Diagram.Nodes)
            {
                switch (node)
                {
                    case CapnpFbpComponentModel fbpNode:
                    {
                        var nodeId = ShortProcId(fbpNode.Id, fbpNode.ProcessName);
                        if (asMermaid)
                        {
                            sb.AppendLine($"{CreateMermaidId(nodeId)}(\"{MermaidEscapeQuotes(fbpNode.ProcessName)}\")");

                            // create an artificially create an IIP node for the config,
                            // if the config port is not connected
                            // hardcoding the port name is bad and should probably be an own in port type
                            const string confPortName = "conf";
                            var confLinks = fbpNode.Links.Where(blm => blm is RememberCapnpPortsLinkModel
                            {
                                InPortModel: CapnpFbpPortModel
                                {
                                    Name: confPortName,
                                }
                            });
                            if (!string.IsNullOrEmpty(fbpNode.ConfigString) && !confLinks.Any())
                            {

                                var tempIipNodeId = Guid.NewGuid().ToString();
                                var iipNodeId = ShortIipId(tempIipNodeId, fbpNode.ConfigString);
                                var mermaidIipId = CreateMermaidId(iipNodeId);
                                sb.AppendLine(
                                    $"{mermaidIipId}[[\"{MermaidEscapeQuotes(fbpNode.ConfigString)}\"]]");
                                sb.AppendLine($"{mermaidIipId} -- \"" +
                                              $"{confPortName}\" --> {CreateMermaidId(nodeId)}");
                            }
                        }
                        else
                        {
                            var jn = new JObject()
                            {
                                { "nodeId", nodeId },
                                { "processName", fbpNode.ProcessName },
                                {
                                    "location",
                                    new JObject() { { "x", fbpNode.Position.X }, { "y", fbpNode.Position.Y } }
                                },
                                { "editable", fbpNode.Editable },
                                { "parallelProcesses", fbpNode.InParallelCount },
                                { "config", fbpNode.ConfigString },
                                { "displayNoOfConfigLines", fbpNode.DisplayNoOfConfigLines }
                            };
                            if (string.IsNullOrWhiteSpace(fbpNode.ComponentId))
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
                                    { "defaultConfig", fbpNode.DefaultConfigString },
                                });
                            }
                            else
                            {
                                jn.Add("componentId", fbpNode.ComponentId);
                                if (!string.IsNullOrWhiteSpace(fbpNode.ComponentServiceId)
                                    && fbpNode.ComponentServiceId != NoRegistryServiceId)
                                {
                                    jn.Add("componentServiceId", fbpNode.ComponentServiceId);
                                }
                            }

                            if (dia["nodes"] is JArray nodes) nodes.Add(jn);
                        }
                        break;
                    }
                    case CapnpFbpIipModel iipNode:
                    {
                        var iipNodeId = ShortIipId(iipNode.Id, iipNode.Content);
                        if (asMermaid)
                        {
                            sb.AppendLine($"{CreateMermaidId(iipNodeId)}[[\"{MermaidEscapeQuotes(iipNode.Content)}\"]]");
                        }
                        else
                        {
                            var jn = new JObject()
                            {
                                { "nodeId", iipNodeId },
                                { "componentId", iipNode.ComponentId },
                                {
                                    "location",
                                    new JObject() { { "x", iipNode.Position.X }, { "y", iipNode.Position.Y } }
                                },
                                { "shortDescription", iipNode.ShortDescription ?? ""},
                                { "content", iipNode.Content },
                                { "displayNoOfLines", iipNode.DisplayNoOfLines }
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

                    if (outIipPort is { Parent: CapnpFbpIipModel outIipModel }
                        && inCapnpPort is { Parent: CapnpFbpComponentModel inCapnpModel })
                    {
                        var outIipNodeId = ShortIipId(outIipModel.Id, outIipModel.Content);
                        var inNodeId = ShortProcId(inCapnpModel.Id, inCapnpModel.ProcessName);

                        // make sure the link is only stored once
                        var checkOut = $"{outIipNodeId}.{outIipPort.Alignment.ToString()}";
                        var checkIn = $"{inNodeId}.{inCapnpPort.Name}";
                        if (linkSet.Contains($"{checkOut}->{checkIn}") ||
                            linkSet.Contains($"{checkIn}->{checkOut}")) continue;
                        linkSet.Add($"{checkOut}->{checkIn}");

                        if (asMermaid)
                        {
                            sb.AppendLine($"{CreateMermaidId(outIipNodeId)} -- \"" +
                                          $"{inCapnpPort.Name}\" --> {CreateMermaidId(inNodeId)}");
                        }
                        else
                        {
                            var jl = new JObject()
                            {
                                {
                                    "source", new JObject()
                                    {
                                        { "nodeId", outIipNodeId },
                                        { "port", outIipPort.Alignment.ToString() }
                                    }
                                },
                                {
                                    "target", new JObject()
                                    {
                                        { "nodeId", inNodeId },
                                        { "port", inCapnpPort.Name }
                                    }
                                }
                            };
                            if (dia["links"] is JArray links) links.Add(jl);
                        }
                    } 
                    else if (outCapnpPort is { Parent: CapnpFbpComponentModel outCapnpModel }
                             && inCapnpPort is { Parent: CapnpFbpComponentModel inCapnpModel2 })
                    {
                        var outNodeId = ShortProcId(outCapnpModel.Id, outCapnpModel.ProcessName);
                        var inNodeId = ShortProcId(inCapnpModel2.Id, inCapnpModel2.ProcessName);

                        // make sure the link is only stored once
                        var checkOut = $"{outNodeId}.{outCapnpPort.Name}";
                        var checkIn = $"{inNodeId}.{inCapnpPort.Name}";
                        if (linkSet.Contains($"{checkOut}->{checkIn}") ||
                            linkSet.Contains($"{checkIn}->{checkOut}")) continue;
                        linkSet.Add($"{checkOut}->{checkIn}");

                        if (asMermaid)
                        {
                            sb.AppendLine($"{CreateMermaidId(outNodeId)} -- " +
                                          $"\"{outCapnpPort.Name} : {inCapnpPort.Name}\" " +
                                          $"--> {CreateMermaidId(inNodeId)}");
                        }
                        else
                        {
                            var jl = new JObject()
                            {
                                {
                                    "source", new JObject()
                                    {
                                        { "nodeId", outNodeId },
                                        { "port", outCapnpPort.Name }
                                    }
                                },
                                {
                                    "target", new JObject()
                                    {
                                        { "nodeId", inNodeId },
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

        private async Task ClearDiagram()
        {
            foreach(var node in Diagram.Nodes)
            {
                if (node is IDisposable disposable) disposable.Dispose();
            }
            Diagram.Nodes.Clear();
            Diagram.Refresh();
        }

        private async Task ExecuteFlow()
        {
            foreach(var node in Diagram.Nodes)
            {
                switch (node)
                {
                    case CapnpFbpComponentModel compNode:
                        await compNode.StartProcess(ConMan, true);
                        break;
                    case CapnpFbpViewComponentModel viewNode:
                        await viewNode.StartProcess(ConMan, true);
                        break;
                    default:
                        continue;
                }
                node.Refresh();
            }
            StateHasChanged();
        }
        
        private Mas.Schema.Fbp.Component _draggedComponent;
        private string _draggedComponentServiceId;
        
        private void OnNodeDragStart(Mas.Schema.Fbp.Component component, string componentServiceId)//(JObject component)//string nodeType, string nodeName)
        {
            _draggedComponent = component;
            _draggedComponentServiceId = componentServiceId;
        }

        private void OnNodeDrop(DragEventArgs e)
        {
            if (_draggedComponent == null) return;
            var position = Diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);
            AddFbpNode(position, _draggedComponent,
                new JObject {{"componentServiceId", _draggedComponentServiceId}});
            _draggedComponent = null;
        }
        
        private NodeModel AddFbpNode(Point position, Mas.Schema.Fbp.Component component, JObject initNode = null,
            string cmd = "")
        {
            switch (component.Type)
            {
                case Component.ComponentType.standard:
                {
                    var componentId = component.Info.Id;
                    var componentServiceId =
                        initNode?.GetValue("componentServiceId")?.Value<string>() ?? NoRegistryServiceId;
                    var unavailableService = false;
                    if (!RegistryServiceIdToPetNameAndSturdyRef.ContainsKey(componentServiceId))
                    {
                        unavailableService = true;
                        RegistryServiceIdToPetNameAndSturdyRef.Add(componentServiceId, ($"Service '{componentServiceId[..3]}..{componentServiceId[^3..]}' unavailable!", null));
                    }
                    var initNodeComponentId = initNode?["componentId"]?.Value<string>() ?? "";
                    //preserve componentId from flow file, even if no service is connected right now
                    if (!string.IsNullOrEmpty(initNodeComponentId))
                    {
                        componentId = initNodeComponentId;
                    }
                    var procName = initNode?["processName"]?.ToString() ?? initNode?["process_name"]?.ToString();
                    
                    var node = new CapnpFbpComponentModel(
                        initNode?.GetValue("nodeId")?.Value<string>() ?? initNode?.GetValue("node_id")?.Value<string>() ?? Guid.NewGuid().ToString(),
                        new Point(position.X, position.Y))
                    {
                        Editor = this,
                        ComponentId = componentId,
                        ComponentServiceId = componentServiceId,
                        ComponentName = unavailableService ? "" : component.Info.Name ?? componentId,
                        ProcessName = procName ?? $"{component.Info.Name ?? "new"} {CapnpFbpComponentModel.ProcessNo++}",
                        Cmd = cmd,
                        ShortDescription = unavailableService ? "" : component.Info.Description ?? "",
                        DefaultConfigString = unavailableService ? "" : component.DefaultConfig ?? "",
                        ConfigString = initNode?.GetValue("config")?.Value<string>() ?? "",
                        DisplayNoOfConfigLines = initNode?["displayNoOfConfigLines"]?.Value<int>() ?? 3,
                        Editable = initNode?.GetValue("editable")?.Value<bool>() ?? component.RunFactory == null,
                        InParallelCount = initNode?.GetValue("parallelProcesses")?.Value<int>() ?? initNode?.GetValue("parallel_processes")?.Value<int>() ?? 1,
                    };
                    if (component.RunFactory != null) node.RunnableFactory = Proxy.Share(component.RunFactory);

                    Diagram.Controls.AddFor(node).Add(new AddPortControl(0.2, 0, -33, -50)
                    {
                        Label = "in",
                        PortType = CapnpFbpPortModel.PortType.In,
                        NodeModel = node,
                    });
                    Diagram.Controls.AddFor(node).Add(new AddPortControl(0.8, 0, -41, -50)
                    {
                        Label = "out",
                        PortType = CapnpFbpPortModel.PortType.Out,
                        NodeModel = node,
                    });
                    Diagram.Controls.AddFor(node).Add(new RemoveProcessControl(0.5, 0, -20, -50));
                    Diagram.Controls.AddFor(node).Add(new ToggleEditNodeControl(1.1, 0, -20, -50)
                    {
                        NodeModel = node
                    });
                    
                    //var portLocations = initNode?["location"]?["ports"] as JObject;

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
                        ShortDescription = initNode?["shortDescription"]?.ToString() ?? "",
                        Content = initNode?["content"]?.ToString() ?? "",
                        DisplayNoOfLines = initNode?["displayNoOfLines"]?.Value<int>() ?? 3,
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
                case Component.ComponentType.subflow:
                    break;
                case Component.ComponentType.view:
                {
                    var componentId = component.Info.Id;
                    var initNodeComponentId = initNode?["componentId"]?.Value<string>() ?? "";
                    //preserve componentId from flow file, even if no service is connected right now
                    if (!string.IsNullOrEmpty(initNodeComponentId))
                    {
                        componentId = initNodeComponentId;
                    }
                    var procName = initNode?["processName"]?.ToString() ?? initNode?["process_name"]?.ToString();

                    var node = new CapnpFbpViewComponentModel(
                        initNode?.GetValue("nodeId")?.Value<string>() ?? initNode?.GetValue("node_id")?.Value<string>() ?? Guid.NewGuid().ToString(),
                        new Point(position.X, position.Y))
                    {
                        Editor = this,
                        ComponentId = componentId,
                        ComponentName = component.Info.Name ?? componentId,
                        ProcessName = procName ?? $"{component.Info.Name ?? "new"} {CapnpFbpComponentModel.ProcessNo++}",
                    };

                    // Diagram.Controls.AddFor(node).Add(new AddPortControl(0.2, 0, -33, -50)
                    // {
                    //     Label = "in",
                    //     PortType = CapnpFbpPortModel.PortType.In,
                    //     NodeModel = node,
                    // });
                    // Diagram.Controls.AddFor(node).Add(new AddPortControl(0.8, 0, -41, -50)
                    // {
                    //     Label = "out",
                    //     PortType = CapnpFbpPortModel.PortType.Out,
                    //     NodeModel = node,
                    // });
                    Diagram.Controls.AddFor(node).Add(new RemoveProcessControl(0.5, 0, -20, -50));
                    // Diagram.Controls.AddFor(node).Add(new ToggleEditNodeControl(1.1, 0, -20, -50)
                    // {
                    //     NodeModel = node
                    // });

                    //var portLocations = initNode?["location"]?["ports"] as JObject;

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
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return null;
        }
    }
}