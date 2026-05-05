using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Behaviors;
using Blazor.Diagrams.Core.Controls;
using Blazor.Diagrams.Core.Extensions;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Blazor.Diagrams.Core.PathGenerators;
using Blazor.Diagrams.Core.Routers;
using Blazor.Diagrams.Options;
using BlazorDrawFBP.Behaviors;
using BlazorDrawFBP.Controls;
using BlazorDrawFBP.Models;
using Capnp.Rpc;
using Mas.Infrastructure.BlazorComponents;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Registry;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Newtonsoft.Json.Linq;
//using SharedDemo.Demos;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using Exception = System.Exception;
using Restorer = Mas.Infrastructure.Common.Restorer;

namespace BlazorDrawFBP.Pages;

public partial class Editor
{
    private const string NoRegistryServiceId = "no_service";
    private const string LoadFlowInputId = "load-flow-input";
    private const double ZoomToFitMargin = 80;
    private const int ViewNodeWidth = 350;
    private const int ViewNodeHeight = 200;
    private static readonly TimeSpan ExecuteFlowSettleTimeout = TimeSpan.FromSeconds(15);
    private const int ExecuteFlowSettlePollIntervalMs = 100;

    private const int IipIdLength = 10;
    private const int ProcIdLength = 20;
    private static readonly (string Background, string Foreground)[] ComponentServicePalette =
    [
        ("#64748B", "#FFFFFF"),
        ("#0072B2", "#FFFFFF"),
        ("#D55E00", "#FFFFFF"),
        ("#009E73", "#FFFFFF"),
        ("#CC79A7", "#FFFFFF"),
        ("#E69F00", "#111827"),
        ("#56B4E9", "#111827"),
        ("#F0E442", "#111827"),
        ("#785EF0", "#FFFFFF"),
        ("#DC267F", "#FFFFFF"),
    ];
    private static readonly Random Random = new();

    //private readonly List<string> events = new List<string>();

    private readonly Restorer _restorer = new() { TcpHost = ConnectionManager.GetLocalIPAddress() };

    private readonly Dictionary<
        string,
        HashSet<(string, string)>
    > CatId2CompServiceIdAndComponentIds = new();

    private readonly Dictionary<string, IdInformation> CatId2Info = new();

    private readonly Dictionary<(string, string), Component> ServiceIdAndComponentId2Component =
        new();

    private Component _draggedComponent;
    private string _draggedComponentServiceId;
    private bool _executingFlow;
    public BlazorDiagram Diagram { get; set; } = null!;

    private Dictionary<string, IRegistry> ServiceId2Registries { get; } = [];

    public Dictionary<string, (string, string)> RegistryServiceIdToPetNameAndSturdyRef { get; } =
    [];

    private Dictionary<string, IStartChannelsService> ServiceId2ChannelStarterServices { get; } =
    [];

    private Dictionary<string, (string, string)> ChannelServiceIdToPetNameAndSturdyRef { get; } =
    [];

    private Dictionary<ulong, Type> InterfaceIdToType { get; } =
        new()
        {
            { Shared.Shared.RegistryInterfaceId, typeof(IRegistry) },
            { Shared.Shared.ChannelStarterInterfaceId, typeof(IStartChannelsService) },
        };

    private Dictionary<string, Proxy> SturdyRef2Services { get; } = [];

    public ConnectionManager ConnectionManager => ConMan;

    public IStartChannelsService CurrentChannelStarterService =>
        ServiceId2ChannelStarterServices
            .FirstOrDefault(new KeyValuePair<string, IStartChannelsService>("none", null))
            .Value;

    public string GetComponentServiceName(string serviceId) =>
        RegistryServiceIdToPetNameAndSturdyRef
            .GetValueOrDefault(serviceId, ("Unknown service!", null))
            .Item1;

    public string GetComponentServiceBadgeStyle(string serviceId)
    {
        var (background, foreground) = GetComponentServiceColors(serviceId);
        return $"background-color: {background} !important; color: {foreground} !important;";
    }

    private string GetComponentServiceHandleStyle(string serviceId)
    {
        var (background, foreground) = GetComponentServiceColors(serviceId);
        return $"background-color: {background}; color: {foreground};";
    }

    private (string Background, string Foreground) GetComponentServiceColors(string serviceId)
    {
        var index = 0;
        foreach (var key in RegistryServiceIdToPetNameAndSturdyRef.Keys)
        {
            if (key == serviceId)
                return ComponentServicePalette[index % ComponentServicePalette.Length];
            index++;
        }

        return ComponentServicePalette[0];
    }

    private async Task<IStartChannelsService> ConnectToStartChannelsService(
        ConnectionManager conMan,
        string petName,
        string sturdyRef
    )
    {
        try
        {
            var service = await conMan.Connect<IStartChannelsService>(sturdyRef);
            if (service == null)
                return null;
            var info = await service.Info();
            Console.WriteLine("Connected to channel starter service @ " + sturdyRef);
            //var iid = GetInterfaceId<IStartChannelsService>();
            var petName2 = Shared.Shared.MakeUniqueKey(
                ServiceId2ChannelStarterServices,
                petName ?? "chan_start_serv"
            );
            ServiceId2ChannelStarterServices[info.Id] = Proxy.Share(service);
            SturdyRef2Services[sturdyRef] = Proxy.Share(service) as Proxy;
            ChannelServiceIdToPetNameAndSturdyRef[info.Id] = (petName2, sturdyRef);
            return service;
        }
        catch (RpcException)
        {
            Console.WriteLine("Couldn't connect to channel starter service @ " + sturdyRef);
        }

        return null;
    }

    private async Task<IRegistry> ConnectToRegistryService(
        ConnectionManager conMan,
        string petName,
        string sturdyRef
    )
    {
        IRegistry reg = null;
        try
        {
            reg = await conMan.Connect<IRegistry>(sturdyRef);
            if (reg == null)
                return null;
            var info = await reg.Info();
            Console.WriteLine("Connected to components registry @ " + sturdyRef);
            //var iid = GetInterfaceId<IRegistry>();
            var petName2 = Shared.Shared.MakeUniqueKey(ServiceId2Registries, petName ?? "reg_serv");
            ServiceId2Registries[info.Id] = Proxy.Share(reg);
            SturdyRef2Services[sturdyRef] = Proxy.Share(reg) as Proxy;
            RegistryServiceIdToPetNameAndSturdyRef[info.Id] = (petName2, sturdyRef);
            Console.WriteLine("added petName2: " + petName2 + " and sturdyRef: " + sturdyRef);
        }
        catch (RpcException)
        {
            Console.WriteLine("Couldn't connect to components registry @ " + sturdyRef);
            return null;
        }

        await LoadComponentsFromRegistry(reg, sturdyRef);
        return reg;
    }

    private async Task HandleSturdyRefConnectedAsync((ulong, string, string) connection)
    {
        var (interfaceId, sturdyRef, petName) = connection;
        if (!SturdyRef2Services.TryGetValue(sturdyRef, out var value))
            return;

        var updatedConnections = false;
        if (interfaceId == Shared.Shared.ChannelStarterInterfaceId)
        {
            if (value is IStartChannelsService service)
            {
                var info = await service.Info();
                var petName2 = Shared.Shared.MakeUniqueKey(ServiceId2ChannelStarterServices, petName);
                ServiceId2ChannelStarterServices[info.Id] = service;
                ChannelServiceIdToPetNameAndSturdyRef[info.Id] = (petName2, sturdyRef);
                updatedConnections = true;
            }
        }
        else if (interfaceId == Shared.Shared.RegistryInterfaceId && value is IRegistry reg)
        {
            var info = await reg.Info();
            var petName2 = Shared.Shared.MakeUniqueKey(ServiceId2Registries, petName);
            ServiceId2Registries[info.Id] = Proxy.Share(reg);
            RegistryServiceIdToPetNameAndSturdyRef[info.Id] = (petName2, sturdyRef);
            await LoadComponentsFromRegistry(Proxy.Share(reg), sturdyRef);
            updatedConnections = true;
        }

        if (updatedConnections)
            await InvokeAsync(StateHasChanged);
    }

    private async Task HandleSturdyRefDisconnectedAsync((ulong, string) connection)
    {
        var (interfaceId, sturdyRef) = connection;
        if (interfaceId == Shared.Shared.ChannelStarterInterfaceId)
        {
            DisconnectChannelStarterService(sturdyRef);
        }
        else if (interfaceId == Shared.Shared.RegistryInterfaceId)
        {
            DisconnectRegistryService(sturdyRef);
        }

        if (SturdyRef2Services.Remove(sturdyRef, out var proxy))
            proxy.Dispose();

        await InvokeAsync(StateHasChanged);
    }

    private void DisconnectChannelStarterService(string sturdyRef)
    {
        var serviceIds = ChannelServiceIdToPetNameAndSturdyRef
            .Where(entry => entry.Value.Item2 == sturdyRef)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var serviceId in serviceIds)
        {
            if (ServiceId2ChannelStarterServices.Remove(serviceId, out var service))
                service.Dispose();
            ChannelServiceIdToPetNameAndSturdyRef.Remove(serviceId);
        }
    }

    private void DisconnectRegistryService(string sturdyRef)
    {
        var serviceIds = RegistryServiceIdToPetNameAndSturdyRef
            .Where(entry => entry.Key != NoRegistryServiceId && entry.Value.Item2 == sturdyRef)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var serviceId in serviceIds)
        {
            if (ServiceId2Registries.Remove(serviceId, out var registry))
                registry.Dispose();
            RegistryServiceIdToPetNameAndSturdyRef.Remove(serviceId);
            RemoveRegistryPaletteEntries(serviceId);
        }
    }

    private void RemoveRegistryPaletteEntries(string serviceId)
    {
        foreach (
            var componentKey in ServiceIdAndComponentId2Component.Keys
                .Where(componentKey => componentKey.Item1 == serviceId)
                .ToList()
        )
        {
            ServiceIdAndComponentId2Component.Remove(componentKey);
        }

        foreach (var categoryId in CatId2CompServiceIdAndComponentIds.Keys.ToList())
        {
            CatId2CompServiceIdAndComponentIds[categoryId].RemoveWhere(
                componentKey => componentKey.Item1 == serviceId
            );

            if (
                categoryId != DefaultCatId
                && CatId2CompServiceIdAndComponentIds[categoryId].Count == 0
            )
            {
                CatId2CompServiceIdAndComponentIds.Remove(categoryId);
                CatId2Info.Remove(categoryId);
            }
        }
    }

    private async Task LoadComponentsFromRegistry(IRegistry reg, string sturdyRef)
    {
        if (reg == null)
            return;
        try
        {
            var categories = await reg.SupportedCategories();
            foreach (var cat in categories)
                if (!CatId2Info.ContainsKey(cat.Id))
                    CatId2Info[cat.Id] = new IdInformation
                    {
                        Id = cat.Id,
                        Name = cat.Name ?? cat.Id,
                        Description = cat.Description ?? cat.Name ?? cat.Id,
                    };
            Console.WriteLine("Loaded supported categories from " + sturdyRef);
        }
        catch (RpcException)
        {
            Console.WriteLine("Error loading supported categories from " + sturdyRef);
        }

        try
        {
            var info = await reg.Info();
            var entries = await reg.Entries(null);
            foreach (var e in entries)
            {
                if (!CatId2CompServiceIdAndComponentIds.ContainsKey(e.CategoryId))
                    CatId2CompServiceIdAndComponentIds[e.CategoryId] = [];
                CatId2CompServiceIdAndComponentIds[e.CategoryId].Add((info.Id, e.Id));
                if (e.Ref is not Proxy p)
                    continue;
                var holder = p.Cast<IIdentifiableHolder<Component>>(true);
                try
                {
                    ServiceIdAndComponentId2Component.Add((info.Id, e.Id), await holder.Value());
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            Console.WriteLine("Loaded entries from " + sturdyRef);
        }
        catch (RpcException)
        {
            Console.WriteLine("Error loading entries from " + sturdyRef);
        }
    }

    private static Component CreateFromJson(JToken jComp)
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
                        Type = p["type"]?.ToString() == "array"
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
                        Type = p["type"]?.ToString() == "array"
                            ? Component.Port.PortType.array
                            : Component.Port.PortType.standard,
                        ContentType = p["contentType"]?.ToString() ?? "?",
                    })
                .ToList()
                ?? [],
        };
    }

    protected override void OnInitialized()
    {
        CleanupService.Editor = this;

        var options = new BlazorDiagramOptions
        {
            AllowMultiSelection = true,
            Zoom = { Enabled = true, Inverse = true },
            Links =
            {
                DefaultRouter = new NormalRouter(),
                DefaultPathGenerator = new SmoothPathGenerator(),
                Factory = (diagram, source, targetAnchor) =>
                {
                    if (source is not PortModel sourcePort)
                        throw new InvalidOperationException(
                            $"FBP links can only start from ports, got {source?.GetType().Name ?? "null"}."
                        );

                    return new LinkModel(
                        new SinglePortAnchor(sourcePort)
                        {
                            MiddleIfNoMarker = true,
                            UseShapeAndAlignment = false,
                        },
                        targetAnchor
                    );
                },
                TargetAnchorFactory = (diagram, link, model) =>
                {
                    if (model is not PortModel targetPort)
                        throw new InvalidOperationException(
                            $"FBP links can only target ports, got {model?.GetType().Name ?? "null"}."
                        );

                    return new SinglePortAnchor(targetPort)
                    {
                        MiddleIfNoMarker = true,
                        UseShapeAndAlignment = false,
                    };
                },
            },
            Groups = { Enabled = true },
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
            if (catId.Length == 0)
                continue;
            CatId2Info[catId] = new IdInformation
            {
                Id = catId,
                Name = cat["name"]?.ToString() ?? catId,
                Description = cat["description"]?.ToString() ?? "",
            };
        }

        foreach (var entry in defComps["entries"] ?? new JArray())
        {
            var catId = entry["categoryId"]?.ToString() ?? "";
            if (catId.Length == 0)
                continue;
            if (entry["component"] is not JObject comp)
                continue;

            if (!CatId2CompServiceIdAndComponentIds.TryGetValue(catId, out var value))
            {
                value = [];
                CatId2CompServiceIdAndComponentIds[catId] = value;
            }

            if (!CatId2Info.ContainsKey(catId))
                CatId2Info[catId] = new IdInformation { Id = catId, Name = catId };

            var component = CreateFromJson(entry["component"]);
            if (component == null)
                continue;
            value.Add((NoRegistryServiceId, component.Info.Id));
            ServiceIdAndComponentId2Component[(NoRegistryServiceId, component.Info.Id)] = component;
        }

        Diagram.RegisterComponent<CapnpFbpRunnableComponentModel, CapnpFbpComponentWidget>();
        Diagram.RegisterComponent<CapnpFbpProcessComponentModel, CapnpFbpComponentWidget>();
        // Diagram.RegisterComponent<CapnpFbpComponentContentModel, CapnpFbpComponentContentWidget>();
        Diagram.RegisterComponent<CapnpFbpViewComponentModel, CapnpFbpViewComponentWidget>();
        Diagram.RegisterComponent<CapnpFbpIipComponentModel, CapnpFbpIipComponentWidget>();
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
        // Console.WriteLine($"Editor: OnAfterRenderAsync firstRender: {firstRender}");
        if (!firstRender)
            return;
        if (!await LocalStorage.ContainKeyAsync("sturdy-ref-store"))
            return;
        var allBookmarks = await StoredSrData.GetAllData(LocalStorage);
        allBookmarks.Sort();

        // iterate over all bookmarks and connect to all auto connect sturdy refs
        foreach (var ssrd in allBookmarks.Where(ssrd => ssrd.AutoConnect))
            if (ssrd.InterfaceId == Shared.Shared.ChannelStarterInterfaceId)
                await ConnectToStartChannelsService(ConMan, ssrd.PetName, ssrd.SturdyRef);
            else if (ssrd.InterfaceId == Shared.Shared.RegistryInterfaceId)
                await ConnectToRegistryService(ConMan, ssrd.PetName, ssrd.SturdyRef);

        StateHasChanged();
    }

    protected override async Task OnInitializedAsync() { }

    private void CreateChannel(CapnpFbpOutPortModel outPort, CapnpFbpInPortModel inPort)
    {
        if (CurrentChannelStarterService == null)
            return;
        Shared.Shared.CreateChannel(ConMan, CurrentChannelStarterService, outPort, inPort);
    }

    private static void RefreshPortLayout(NodeModel node)
    {
        foreach (var relatedNode in GetNodesAffectingPortLayout(node))
        {
            CapnpFbpPortLayout.Apply(relatedNode, refreshPorts: false);
            relatedNode.RefreshAll();
        }
    }

    private static void RefreshPortLayout(BaseLinkModel link)
    {
        foreach (var node in GetNodesAffectingPortLayout(link))
        {
            CapnpFbpPortLayout.Apply(node, refreshPorts: false);
            node.RefreshAll();
        }
    }

    private static IEnumerable<NodeModel> GetNodesAffectingPortLayout(NodeModel node)
    {
        var nodes = new List<NodeModel> { node };
        foreach (var link in Shared.Shared.AttachedLinks(node))
            nodes.AddRange(GetNodesAffectingPortLayout(link));
        return nodes.Distinct();
    }

    private static IEnumerable<NodeModel> GetNodesAffectingPortLayout(BaseLinkModel link)
    {
        var nodes = new List<NodeModel>();
        if (link.Source.Model is PortModel { Parent: { } sourceParent })
            nodes.Add(sourceParent);
        if (link.Target.Model is PortModel { Parent: { } targetParent })
            nodes.Add(targetParent);
        return nodes.Distinct();
    }

    private static void RegisterNodeLayoutEvents(NodeModel node)
    {
        node.Moved += OnNodeMoved;
        node.SizeChanged += OnNodeSizeChanged;
    }

    private static void OnNodeMoved(MovableModel movedModel)
    {
        if (movedModel is NodeModel movedNode)
            RefreshPortLayout(movedNode);
    }

    private static void OnNodeSizeChanged(NodeModel node)
    {
        RefreshPortLayout(node);
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

        Diagram.Links.Added += async l =>
        {
            Diagram.Controls.AddFor(l).Add(new RemoveLinkControl(0.5, 0.5));
            if (l is RememberCapnpPortsLinkModel rememberedLink)
            {
                await Shared.Shared.ConnectLinkToRunningProcessesAsync(
                    ConMan,
                    CurrentChannelStarterService,
                    rememberedLink
                );
                RefreshPortLayout(l);
                return;
            }

            switch (l.Source.Model)
            {
                case CapnpFbpInPortModel sourceInPort:
                {
                    l.TargetChanged += (link, oldTarget, newTarget) =>
                    {
                        if (newTarget.Model is not CapnpFbpOutPortModel outPort)
                            return;
                        var nl = new RememberCapnpPortsLinkModel(outPort, sourceInPort);
                        CapnpFbpPortColors.ApplyLinkColor(nl);
                        var cllm = new ChannelLinkLabelModel(nl, "Channel", 0.5);
                        // if the input port has already a channel attached, get a new writer for that channel
                        // to attach to the IIP's out port
                        if (sourceInPort.Channel != null)
                            _ = nl.EnsureWriterFromChannelAsync();
                        nl.Labels.Add(cllm);
                        Diagram.Links.Add(nl);
                        Diagram.Links.Remove(l);
                        outPort.SyncVisibility();
                        sourceInPort.SyncVisibility();
                        sourceInPort.Refresh();
                        outPort.Refresh();
                        RefreshPortLayout(nl);
                    };
                    break;
                }
                case CapnpFbpOutPortModel sourceOutPort:
                {
                    l.TargetChanged += (link, oldTarget, newTarget) =>
                    {
                        if (newTarget.Model is not CapnpFbpInPortModel inPort)
                            return;
                        var nl = new RememberCapnpPortsLinkModel(sourceOutPort, inPort);
                        CapnpFbpPortColors.ApplyLinkColor(nl);
                        var cllm = new ChannelLinkLabelModel(nl, "Channel", 0.5);
                        // if the input port has already a channel attached, get a new writer for that channel
                        // to attach to the IIP's out port
                        if (inPort.Channel != null)
                            _ = nl.EnsureWriterFromChannelAsync();
                        nl.Labels.Add(cllm);
                        Diagram.Links.Add(nl);
                        Diagram.Links.Remove(l);
                        sourceOutPort.SyncVisibility();
                        inPort.SyncVisibility();
                        sourceOutPort.Refresh();
                        inPort.Refresh();
                        RefreshPortLayout(nl);
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
                var ct =
                    port.ThePortType == CapnpFbpPortModel.PortType.In
                        ? "expects [content type]"
                        : "sends [content type]";
                // find closest port, assuming the user will click on the label he actually wants to change
                var node = new PortOptionsNode(relativePt)
                {
                    NameLabel = $"Change {port.Name}",
                    ContentTypeLabel = $"{port.Name} {ct}",
                    DescriptionLabel = $"Description",
                    PortModel = port,
                    NodeModel = port.Parent as CapnpFbpComponentModel,
                    Container = Diagram,
                };
                Diagram.Nodes.Add(node);
            }
            // else if (m is CapnpFbpComponentModel compModel)
            // {
            //     var relativePt = Diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);
            //
            //     // find closest port, assuming the user will click on the label he actually wants to change
            //     var node = new CapnpFbpComponentContentModel(relativePt)
            //     {
            //         Label = $"xxxxxChange {compModel.ComponentName}",
            //         ComponentModel = compModel,
            //         Container = Diagram,
            //     };
            //     Diagram.Nodes.Add(node);
            // }

            //Console.WriteLine($"MouseClick, Type={m?.GetType().Name}, ModelId={m?.Id}, Position=({e.ClientX}/{e.ClientY}");
            //events.Add($"MouseClick, Type={m?.GetType().Name}, ModelId={m?.Id}");
            StateHasChanged();
        };

        Diagram.PointerDoubleClick += (m, e) =>
        {
            if (m is LinkModel link)
            {
                if (
                    link.Source.Model is CapnpFbpPortModel source
                    && link.Target.Model is CapnpFbpPortModel target
                )
                {
                    var relativePt = Diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);

                    // find the closest port to the click location
                    var sourceToPoint = relativePt.DistanceTo(source.MiddlePosition);
                    var targetToPoint = relativePt.DistanceTo(target.MiddlePosition);
                    var portModel = sourceToPoint < targetToPoint ? source : target;

                    var node = new UpdatePortNameNode(relativePt)
                    {
                        Label = $"Change {portModel.Name}",
                        PortName = portModel.Name,
                        PortModel = portModel,
                        Container = Diagram,
                    };
                    Diagram.Nodes.Add(node);
                }
            }
            else if (m == null)
            {
                _ = InvokeAsync(ZoomToFitFlow);
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
        if (node == null)
            return;
        Diagram.Nodes.Remove(node);
    }

    protected async Task LoadFlow(IBrowserFile file)
    {
        var s = file.OpenReadStream();
        if (s.Length > 1 * 1024 * 1024)
            return; // 1 MB
        var dia = JObject.Parse(await new StreamReader(s).ReadToEndAsync());
        var oldNodeIdToNewNode = new Dictionary<string, NodeModel>();

        Diagram.SuspendRefresh = true;
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
            var compServiceId = nodeObj["componentServiceId"]?.ToString() ?? NoRegistryServiceId;
            Component component;
            var cmd = "";
            if (string.IsNullOrEmpty(compId) && nodeObj.TryGetValue("component", out var compDesc))
            {
                component = CreateFromJson(compDesc);
                cmd = compDesc["cmd"]?.ToString() ?? "";
            }
            else if (
                !ServiceIdAndComponentId2Component.TryGetValue(
                    (compServiceId, compId),
                    out component
                )
            )
            {
                //there is no component service with the given component id
                //let's try to find some service which offers that component
                foreach (var (key, value) in ServiceIdAndComponentId2Component)
                {
                    if (key.Item2 != compId)
                        continue;
                    component = value;
                    nodeObj["componentServiceId"] = key.Item1;
                    break;
                }

                //no service with the correct component id available, make it an empty_component
                if (component == null)
                {
                    component = nodeObj.ContainsKey("content")
                        ? ServiceIdAndComponentId2Component[(NoRegistryServiceId, "iip")]
                        : ServiceIdAndComponentId2Component[
                            (NoRegistryServiceId, "empty_component")
                        ];
                }
            }

            var diaNode = AddFbpNode(position, component, nodeObj, cmd);
            oldNodeIdToNewNode.Add(
                nodeObj["nodeId"]?.ToString() ?? nodeObj["node_id"]?.ToString() ?? "",
                diaNode
            );
        }

        foreach (var link in dia["links"] ?? new JArray())
        {
            if (link["source"] is not JObject source || link["target"] is not JObject target)
                continue;

            var sourcePortName = source["port"]?.ToString();
            var targetPortName = target["port"]?.ToString();
            if (sourcePortName == null || targetPortName == null)
                continue;

            var sourceNode = oldNodeIdToNewNode[
                source["nodeId"]?.ToString() ?? source["node_id"]?.ToString() ?? ""
            ];
            var targetNode = oldNodeIdToNewNode[
                target["nodeId"]?.ToString() ?? target["node_id"]?.ToString() ?? ""
            ];
            if (
                sourceNode == null
                || sourceNode.Ports.Count == 0
                || targetNode == null
                || targetNode.Ports.Count == 0
            )
                continue;

            var sourcePort = sourceNode
                .Ports.Where(p =>
                    p is CapnpFbpPortModel capnpPort && capnpPort.Name == sourcePortName
                )
                .DefaultIfEmpty(null)
                .First();
            var noOfSourcePorts = sourceNode.Ports.Count(p =>
                p is CapnpFbpPortModel { ThePortType: CapnpFbpPortModel.PortType.Out }
            );
            var targetPort = targetNode
                .Ports.Where(p =>
                    p is CapnpFbpPortModel capnpPort && capnpPort.Name == targetPortName
                )
                .DefaultIfEmpty(null)
                .First();
            var noOfTargetPorts = targetNode.Ports.Count(p =>
                p is CapnpFbpPortModel { ThePortType: CapnpFbpPortModel.PortType.In }
            );
            if (sourcePort == null && sourceNode is CapnpFbpIipComponentModel)
            {
                // Older flow files stored one of the former hard-coded IIP alignments.
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
            //if (sourcePort == null || targetPort == null) continue;
            //Diagram.Links.Add(new LinkModel(sourcePort, targetPort));

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
            Diagram.Links.Add(l);
        }

        Diagram.SuspendRefresh = false;
        Diagram.Refresh();

        await InvokeAsync(ZoomToFitFlow);
    }

    private void ZoomToFitFlow()
    {
        if (Diagram.Nodes.Count == 0 || Diagram.Container == null)
            return;

        Diagram.UnselectAll();
        var bounds = DiagramExtensions.GetBounds(Diagram.Nodes);
        Diagram.ZoomToFit(ZoomToFitMargin);
        var extraHeight =
            Diagram.Container.Height - ((bounds.Height + 2 * ZoomToFitMargin) * Diagram.Zoom);
        if (extraHeight > 0)
            Diagram.UpdatePan(0, extraHeight / 2);

        Diagram.Refresh();
    }

    protected async Task LoadFlowSelected(InputFileChangeEventArgs args)
    {
        try
        {
            if (args.FileCount > 0)
            {
                await LoadFlow(args.File);
            }
        }
        finally
        {
            _loadFlowInputVersion++;
        }
    }

    protected async Task SaveFlow(bool asMermaid)
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
        else
        {
            dia["pan"] = new JObject { { "x", Diagram.Pan.X }, { "y", Diagram.Pan.Y } };
            dia["zoom"] = Diagram.Zoom;

            // store references to used services
            if (dia["services"]?["channels"] is JObject channels)
                foreach (var p in ServiceId2ChannelStarterServices)
                    channels[p.Key] = ChannelServiceIdToPetNameAndSturdyRef[p.Key].Item2;

            if (dia["services"]?["components"] is JObject components)
                foreach (var p in ServiceId2Registries)
                    components[p.Key] = RegistryServiceIdToPetNameAndSturdyRef[p.Key].Item2;
        }

        var procIdCount = 2;
        HashSet<string> shortProcIds = [];
        Dictionary<string, string> uuid2ShortProcId = new();

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

        string MermaidEscapeQuotes(string str)
        {
            return str.Replace("\"", "&quot;");
        }

        string CreateMermaidId(string id)
        {
            var newId = new StringBuilder();
            foreach (var c in id)
                newId.Append(char.IsLetterOrDigit(c) ? c : '_');
            //now remove all repeated _ from name
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

        foreach (var node in Diagram.Nodes)
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

                        // create an artificially create an IIP node for the config,
                        // if the config port is not connected
                        // hardcoding the port name is bad and should probably be an own in port type
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
                                $"{mermaidIipId} -- \""
                                    + $"{confPortName}\" --> {CreateMermaidId(nodeId)}"
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
                            //{ "displayNoOfConfigLines", fbpNode.DisplayNoOfConfigLines },
                        };
                        if (string.IsNullOrWhiteSpace(fbpNode.ComponentId))
                        {
                            // create inputs
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

                            //create outputs
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

                        if (dia["nodes"] is JArray nodes)
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
                        if (dia["nodes"] is JArray nodes)
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
                        if (dia["nodes"] is JArray nodes)
                            nodes.Add(jn);
                    }

                    break;
                }
                default:
                    continue;
            }

            foreach (var pl in node.PortLinks.Concat(node.Links))
            {
                if (!pl.IsAttached || pl is not RememberCapnpPortsLinkModel rcplm)
                    continue;

                var outCapnpPort = rcplm.OutPortModel as CapnpFbpOutPortModel;
                var inCapnpPort = rcplm.InPortModel as CapnpFbpInPortModel;
                ;

                switch (outCapnpPort)
                {
                    case { Parent: CapnpFbpIipComponentModel outIipModel }
                        when inCapnpPort is { Parent: CapnpFbpComponentModel inCapnpModel }:
                    {
                        var outIipNodeId = ShortIipId(outIipModel.Id, outIipModel.Content);
                        var inNodeId = ShortProcId(inCapnpModel.Id, inCapnpModel.ProcessName);

                        // make sure the link is only stored once
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
                                $"{CreateMermaidId(outIipNodeId)} -- \""
                                    + $"{inCapnpPort.Name}\" --> {CreateMermaidId(inNodeId)}"
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
                            if (dia["links"] is JArray links)
                                links.Add(jl);
                        }

                        break;
                    }
                    case { Parent: CapnpFbpComponentModel outCapnpModel }
                        when inCapnpPort is { Parent: CapnpFbpComponentModel inCapnpModel2 }:
                    {
                        var outNodeId = ShortProcId(outCapnpModel.Id, outCapnpModel.ProcessName);
                        var inNodeId = ShortProcId(inCapnpModel2.Id, inCapnpModel2.ProcessName);

                        // make sure the link is only stored once
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
                                $"{CreateMermaidId(outNodeId)} -- "
                                    + $"\"{outCapnpPort.Name} : {inCapnpPort.Name}\" "
                                    + $"--> {CreateMermaidId(inNodeId)}"
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
                            if (dia["links"] is JArray links)
                                links.Add(jl);
                        }

                        break;
                    }
                    case { Parent: CapnpFbpIipComponentModel outIipModel2 }
                        when inCapnpPort is { Parent: CapnpFbpViewComponentModel inViewCapnpModel }:
                    {
                        var outIipNodeId = ShortIipId(outIipModel2.Id, outIipModel2.Content);
                        var inNodeId = inViewCapnpModel.Id;

                        // make sure the link is only stored once
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
                                $"{CreateMermaidId(outIipNodeId)} -- \""
                                    + $"{inCapnpPort.Name}\" --> {CreateMermaidId(inNodeId)}"
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
                            if (dia["links"] is JArray links)
                                links.Add(jl);
                        }

                        break;
                    }
                    case { Parent: CapnpFbpComponentModel outCapnpModel2 }
                        when inCapnpPort
                            is { Parent: CapnpFbpViewComponentModel inViewCapnpModel2 }:
                    {
                        var outNodeId = ShortProcId(outCapnpModel2.Id, outCapnpModel2.ProcessName);
                        var inNodeId = inViewCapnpModel2.Id;

                        // make sure the link is only stored once
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
                                $"{CreateMermaidId(outNodeId)} -- "
                                    + $"\"{outCapnpPort.Name} : {inCapnpPort.Name}\" "
                                    + $"--> {CreateMermaidId(inNodeId)}"
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
                            if (dia["links"] is JArray links)
                                links.Add(jl);
                        }

                        break;
                    }
                }
            }
        }

        //File.WriteAllText("Data/diagram_new.json", dia.ToString());
        await JsRuntime.InvokeVoidAsync(
            "saveAsBase64",
            "flow." + (asMermaid ? "mmd" : "json"),
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(asMermaid ? sb.ToString() : dia?.ToString())
            )
        );
    }

    public async Task ClearDiagram()
    {
        var nodes = Diagram.Nodes.ToList();
        if (Diagram.Links.Count > 0)
        {
            await Shared.Shared.RemoveAttachedLinksAndCleanupAsync(
                Diagram.Links.ToList(),
                Diagram,
                nodes.Cast<Model>().ToList()
            );
        }

        foreach (var node in nodes)
        {
            if (node is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }

        Diagram.Nodes.Clear();
        Diagram.Refresh();
    }

    private async Task ExecuteNode(Model node)
    {
        switch (node)
        {
            case CapnpFbpComponentModel compNode when compNode.CanStart:
                await compNode.StartProcess(ConMan);
                break;
            case CapnpFbpViewComponentModel viewNode when viewNode.CanStart:
                await viewNode.StartProcess(ConMan);
                break;
            case CapnpFbpIipComponentModel iipNode when iipNode.CanStart:
                await iipNode.SendIip(ConMan);
                break;
        }
    }

    private static bool IsExecutableFlowNode(Model node) =>
        node is CapnpFbpComponentModel or CapnpFbpViewComponentModel or CapnpFbpIipComponentModel;

    private static bool IsLifecycleBusy(Model node) =>
        node switch
        {
            CapnpFbpComponentModel compNode => compNode.IsLifecycleBusy,
            CapnpFbpViewComponentModel viewNode => viewNode.IsLifecycleBusy,
            CapnpFbpIipComponentModel iipNode => iipNode.IsLifecycleBusy,
            _ => false,
        };

    private static string GetFlowNodeName(Model node) =>
        node switch
        {
            CapnpFbpComponentModel { ProcessName: { Length: > 0 } processName } => processName,
            CapnpFbpViewComponentModel { ProcessName: { Length: > 0 } processName } => processName,
            CapnpFbpIipComponentModel { ComponentId: { Length: > 0 } componentId } => componentId,
            _ => node.Id,
        };

    private IReadOnlyList<Model> GetFlowStartupOrder()
    {
        var nodes = Diagram.Nodes.Where(IsExecutableFlowNode).Cast<Model>().ToList();
        var originalOrder = nodes.Select((node, index) => (node, index)).ToDictionary(x => x.node, x => x.index);
        var outgoing = nodes.ToDictionary(node => node, _ => new HashSet<Model>());
        var indegree = nodes.ToDictionary(node => node, _ => 0);

        foreach (var link in Diagram.Links.OfType<RememberCapnpPortsLinkModel>())
        {
            var source = link.OutPortModel.Parent as Model;
            var target = link.InPortModel.Parent as Model;
            if (
                source == null
                || target == null
                || ReferenceEquals(source, target)
                || !outgoing.ContainsKey(source)
                || !outgoing.ContainsKey(target)
            )
            {
                continue;
            }

            // Start downstream nodes first so readers/process inputs are ready before sources emit data.
            if (outgoing[target].Add(source))
                indegree[source]++;
        }

        var ready = nodes.Where(node => indegree[node] == 0).OrderBy(node => originalOrder[node]).ToList();
        var ordered = new List<Model>(nodes.Count);

        while (ready.Count > 0)
        {
            var node = ready[0];
            ready.RemoveAt(0);
            ordered.Add(node);

            foreach (var next in outgoing[node].OrderBy(next => originalOrder[next]))
            {
                indegree[next]--;
                if (indegree[next] == 0)
                    ready.Add(next);
            }

            ready.Sort((left, right) => originalOrder[left].CompareTo(originalOrder[right]));
        }

        if (ordered.Count == nodes.Count)
            return ordered;

        foreach (var node in nodes.Where(node => !ordered.Contains(node)).OrderBy(node => originalOrder[node]))
            ordered.Add(node);

        return ordered;
    }

    private async Task<bool> WaitForNodesToSettleAsync(IEnumerable<Model> nodes)
    {
        var trackedNodes = nodes.Distinct().Where(IsExecutableFlowNode).ToList();
        if (trackedNodes.Count == 0)
            return true;

        var deadline = DateTime.UtcNow + ExecuteFlowSettleTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (trackedNodes.All(node => !IsLifecycleBusy(node)))
                return true;

            await Task.Delay(ExecuteFlowSettlePollIntervalMs);
            await InvokeAsync(StateHasChanged);
        }

        return trackedNodes.All(node => !IsLifecycleBusy(node));
    }

    private async Task ExecuteFlow()
    {
        if (!CanExecuteFlow)
        {
            Console.WriteLine($"Editor.razor.cs::ExecuteFlow: {ExecuteFlowButtonTitle}");
            return;
        }

        _executingFlow = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            var startupOrder = GetFlowStartupOrder();
            var startupNodes = startupOrder
                .Where(node => node is not CapnpFbpIipComponentModel)
                .ToList();
            var iipNodes = startupOrder.OfType<CapnpFbpIipComponentModel>().Cast<Model>().ToList();

            foreach (var node in startupNodes)
            {
                await ExecuteNode(node);
                await WaitForNodesToSettleAsync([node]);
                node.Refresh();
                await InvokeAsync(StateHasChanged);
            }

            if (!await WaitForNodesToSettleAsync(startupNodes))
            {
                var busyNodes = string.Join(
                    ", ",
                    startupNodes.Where(IsLifecycleBusy).Select(GetFlowNodeName)
                );
                Console.WriteLine(
                    $"Editor.razor.cs::ExecuteFlow: timed out waiting for startup to settle before dispatching IIPs. Busy nodes: {busyNodes}"
                );
                return;
            }

            foreach (var node in iipNodes)
            {
                await ExecuteNode(node);
                node.Refresh();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Editor.razor.cs::ExecuteFlow: Caught exception: {ex}");
        }
        finally
        {
            _executingFlow = false;
        }

        await InvokeAsync(StateHasChanged);
    }

    private void OnNodeDragStart(Component component, string componentServiceId) //(JObject component)//string nodeType, string nodeName)
    {
        _draggedComponent = component;
        _draggedComponentServiceId = componentServiceId;
    }

    private void OnNodeDragEnd()
    {
        _draggedComponent = null;
        _draggedComponentServiceId = null;
    }

    private void OnNodeDrop(DragEventArgs e)
    {
        if (_draggedComponent == null)
            return;
        var position = Diagram.GetRelativeMousePoint(e.ClientX - 125, e.ClientY - 100);
        AddFbpNode(
            position,
            _draggedComponent,
            new JObject { { "componentServiceId", _draggedComponentServiceId } }
        );
        OnNodeDragEnd();
    }

    private NodeModel AddFbpNode(
        Point position,
        Component component,
        JObject initNode = null,
        string cmd = ""
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
                if (!RegistryServiceIdToPetNameAndSturdyRef.ContainsKey(componentServiceId))
                {
                    unavailableService = true;
                    RegistryServiceIdToPetNameAndSturdyRef.Add(
                        componentServiceId,
                        (
                            $"Service '{componentServiceId[..3]}..{componentServiceId[^3..]}' unavailable!",
                            null
                        )
                    );
                }

                var initNodeComponentId = initNode?["componentId"]?.Value<string>() ?? "";
                //preserve componentId from flow file, even if no service is connected right now
                if (!string.IsNullOrEmpty(initNodeComponentId))
                    componentId = initNodeComponentId;
                var procName =
                    initNode?["processName"]?.ToString() ?? initNode?["process_name"]?.ToString();

                var config = initNode?.GetValue("config");
                var configStr = (config?.Type ?? JTokenType.Null) switch
                {
                    JTokenType.Object => config?.ToString(Newtonsoft.Json.Formatting.Indented),
                    JTokenType.String => config?.ToString(),
                    _ => "",
                };

                CapnpFbpComponentModel node = null;
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
                            Editor = this,
                            ComponentId = componentId,
                            ComponentServiceId = componentServiceId,
                            ComponentName = unavailableService
                                ? ""
                                : component.Info.Name ?? componentId,
                            ProcessName =
                                procName
                                ?? $"{component.Info.Name ?? "new"} {CapnpFbpComponentModel.ProcessNo++}",
                            Cmd = cmd,
                            ShortDescription = unavailableService
                                ? ""
                                : component.Info.Description ?? "",
                            DefaultConfigString = unavailableService
                                ? ""
                                : component.DefaultConfig?.Value ?? "",
                            ConfigString = configStr,
                            DisplayNoOfConfigLines =
                                initNode?["displayNoOfConfigLines"]?.Value<int>() ?? 3,
                            Editable =
                                initNode?.GetValue("editable")?.Value<bool>()
                                ?? (component.Factory?.which ?? Component.factory.WHICH.None)
                                    == Component.factory.WHICH.None,
                            InParallelCount =
                                initNode?.GetValue("parallelProcesses")?.Value<int>()
                                ?? initNode?.GetValue("parallel_processes")?.Value<int>()
                                ?? 1,
                        };

                        SetDefaultComponentSize(rnode);
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
                            Editor = this,
                            ComponentId = componentId,
                            ComponentServiceId = componentServiceId,
                            ComponentName = unavailableService
                                ? ""
                                : component.Info.Name ?? componentId,
                            ProcessName =
                                procName
                                ?? $"{component.Info.Name ?? "new"} {CapnpFbpComponentModel.ProcessNo++}",
                            Cmd = cmd,
                            ShortDescription = unavailableService
                                ? ""
                                : component.Info.Description ?? "",
                            DefaultConfigString = unavailableService
                                ? ""
                                : component.DefaultConfig?.Value ?? "",
                            ConfigString = configStr,
                            DisplayNoOfConfigLines =
                                initNode?["displayNoOfConfigLines"]?.Value<int>() ?? 3,
                            Editable =
                                initNode?.GetValue("editable")?.Value<bool>()
                                ?? (component.Factory?.which ?? Component.factory.WHICH.None)
                                    == Component.factory.WHICH.None,
                            InParallelCount =
                                initNode?.GetValue("parallelProcesses")?.Value<int>()
                                ?? initNode?.GetValue("parallel_processes")?.Value<int>()
                                ?? 1,
                        };
                        SetDefaultComponentSize(pnode);
                        if (component.Factory?.which == Component.factory.WHICH.Process)
                        {
                            pnode.ProcessFactory = Proxy.Share(component.Factory!.Process);
                        }

                        node = pnode;
                        break;
                    }
                }

                var controlsContainer = Diagram.Controls.AddFor(node); //, ControlsType.OnHover);
                // controlsContainer.Add(
                //     new AddPortControl(0.2, 0, -33, -50)
                //     {
                //         Label = "in",
                //         PortType = CapnpFbpPortModel.PortType.In,
                //         NodeModel = node,
                //     }
                // );
                // controlsContainer.Add(
                //     new AddPortControl(0.8, 0, -41, -50)
                //     {
                //         Label = "out",
                //         PortType = CapnpFbpPortModel.PortType.Out,
                //         NodeModel = node,
                //     }
                // );
                controlsContainer.Add(new RemoveProcessControl(0.5, 0, -20, -50));
                // controlsContainer.Add(
                //     new ToggleEditNodeControl(1.1, 0, -20, -50) { NodeModel = node }
                // );

                foreach (var (i, input) in component.InPorts.Select((inp, i) => (i, inp)))
                    AddPortControl.CreateAndAddPort(
                        node,
                        CapnpFbpPortModel.PortType.In,
                        i,
                        input.Name,
                        input.ContentType,
                        input.Desc,
                        input.Type == Component.Port.PortType.array
                    );

                foreach (var (i, output) in component.OutPorts.Select((outp, i) => (i, outp)))
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
                RegisterNodeLayoutEvents(node);
                Diagram.Nodes.Add(node);
                return node;
            }
            case Component.ComponentType.iip:
            {
                var compId = component.Info.Id;
                var node = new CapnpFbpIipComponentModel(new Point(position.X, position.Y))
                {
                    Editor = this,
                    ComponentId = compId,
                    ShortDescription = initNode?["shortDescription"]?.ToString() ?? "",
                    Content = initNode?["content"]?.ToString() ?? "",
                    DisplayNoOfLines = initNode?["displayNoOfLines"]?.Value<int>() ?? 3,
                };
                SetDefaultComponentSize(node);
                AddPortControl.CreateAndAddPort(node, CapnpFbpPortModel.PortType.Out, 0, "IIP");
                RegisterNodeLayoutEvents(node);
                Diagram.Nodes.Add(node);
                Diagram.Controls.AddFor(node).Add(new RemoveProcessControl(0.5, 0, -20, -50));
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
                    Editor = this,
                    ComponentId = componentId,
                    ComponentName = component.Info.Name ?? componentId,
                    ProcessName =
                        procName
                        ?? $"{component.Info.Name ?? "new"} {CapnpFbpComponentModel.ProcessNo++}",
                };
                node.Size = new Size(ViewNodeWidth, ViewNodeHeight);

                Diagram.Controls.AddFor(node).Add(new RemoveProcessControl(0.5, 0, -20, -50));

                foreach (var (i, input) in component.InPorts.Select((inp, i) => (i, inp)))
                    AddPortControl.CreateAndAddPort(
                        node,
                        CapnpFbpPortModel.PortType.In,
                        i,
                        input.Name,
                        input.ContentType,
                        input.Desc,
                        input.Type == Component.Port.PortType.array
                    );

                foreach (var (i, output) in component.OutPorts.Select((outp, i) => (i, outp)))
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
                RegisterNodeLayoutEvents(node);
                Diagram.Nodes.Add(node);
                return node;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }

        return null;
    }

    private static void SetDefaultComponentSize(NodeModel node) =>
        node.Size = new Size(Shared.Shared.CardWidth, Shared.Shared.CardHeight);
}
