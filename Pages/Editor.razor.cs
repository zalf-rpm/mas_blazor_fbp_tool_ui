using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
using BlazorDrawFBP.Services;
using Capnp.Rpc;
using Mas.Infrastructure.BlazorComponents;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Registry;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Newtonsoft.Json.Linq;
//using SharedDemo.Demos;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using Exception = System.Exception;
using Restorer = Mas.Infrastructure.Common.Restorer;
using Size = Blazor.Diagrams.Core.Geometry.Size;

namespace BlazorDrawFBP.Pages;

public partial class Editor : IAsyncDisposable
{
    [Parameter]
    public Guid? FlowId { get; set; }

    public FlowSession? CurrentSession { get; private set; }
    public IFbpRuntimeService RuntimeService => CurrentSession?.RuntimeService ?? InjectedRuntimeService;
    public ConnectionManager ConMan => CurrentSession?.RuntimeService.ConnectionManager ?? InjectedConMan;

    private CancellationTokenSource? _snapshotDebounceCts;

    public void ScheduleSessionSnapshot()
    {
        if (CurrentSession == null || _loadingFlow)
            return;

        _snapshotDebounceCts?.Cancel();
        _snapshotDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _snapshotDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cts.Token);
                if (cts.Token.IsCancellationRequested)
                    return;

                var doc = await ExportFlowJsonAsync();
                if (CurrentSession != null)
                {
                    CurrentSession.FlowDocument = doc;
                    CurrentSession.Touch();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"Session snapshot failed: {ex.Message}");
            }
        });
    }

    public string ShortFlowId => FlowId.HasValue ? FlowId.Value.ToString("N")[..8] : "";

    public void CreateNewFlow()
    {
        var newId = Guid.NewGuid();
        NavigationManager.NavigateTo($"/flow/{newId}");
    }

    public async Task CopyFlowUrlToClipboard()
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", NavigationManager.Uri);
            Snackbar.Add("Flow URL copied to clipboard!", Severity.Info);
        }
        catch (Exception)
        {
            Snackbar.Add($"Flow URL: {NavigationManager.Uri}", Severity.Info);
        }
    }

    public async Task TerminateFlowSessionAsync()
    {
        if (!FlowId.HasValue)
            return;

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Terminate Flow Session?",
            "Are you sure you want to terminate this flow session? All running processes and channels will be stopped immediately and the session will be purged.",
            yesText: "Terminate",
            cancelText: "Cancel"
        );

        if (confirmed != true)
            return;

        var targetFlowId = FlowId.Value;
        if (SessionStore.RemoveSession(targetFlowId, out var session))
        {
            if (session.RuntimeService is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
            else
            {
                await session.RuntimeService.ClearDiagramAsync();
            }
        }
        else
        {
            await RuntimeService.ClearDiagramAsync();
        }

        Snackbar.Add("Flow session terminated.", Severity.Warning);
        var newId = Guid.NewGuid();
        NavigationManager.NavigateTo($"/flow/{newId}");
    }

    private void OnDiagramStructureChanged(Model _) => ScheduleSessionSnapshot();

    private const string NoRegistryServiceId = "no_service";
    private const string LoadFlowInputId = "load-flow-input";
    private const double ZoomToFitMargin = 80;
    private const int ViewNodeWidth = 350;
    private const int ViewNodeHeight = 200;

    private const int IipIdLength = 10;
    private const int ProcIdLength = 20;
    private static readonly Random Random = new();

    private readonly Restorer _restorer = new() { TcpHost = ConnectionManager.GetLocalIPAddress() };

    private Component? _draggedComponent;
    private string? _draggedComponentServiceId;
    private bool _loadingFlow;
    public BlazorDiagram Diagram { get; set; } = null!;

    public ConnectionManager ConnectionManager => RuntimeService.ConnectionManager;
    public IStartChannelsService? CurrentChannelStarterService => RuntimeService.CurrentChannelStarterService;
    public Dictionary<ulong, Type> InterfaceIdToType => RuntimeService.InterfaceIdToType;
    public Dictionary<string, IRegistry> ServiceId2Registries => RuntimeService.ServiceId2Registries;
    public Dictionary<string, (string, string?)> RegistryServiceIdToPetNameAndSturdyRef => RuntimeService.RegistryServiceIdToPetNameAndSturdyRef;
    public Dictionary<string, IStartChannelsService> ServiceId2ChannelStarterServices => RuntimeService.ServiceId2ChannelStarterServices;
    public Dictionary<string, (string, string)> ChannelServiceIdToPetNameAndSturdyRef => RuntimeService.ChannelServiceIdToPetNameAndSturdyRef;
    public Dictionary<string, Proxy> SturdyRef2Services => RuntimeService.SturdyRef2Services;
    public Dictionary<(string, string), Component> ServiceIdAndComponentId2Component => RuntimeService.ServiceIdAndComponentId2Component;
    public Dictionary<string, HashSet<(string, string)>> CatId2CompServiceIdAndComponentIds => RuntimeService.CatId2CompServiceIdAndComponentIds;
    public Dictionary<string, IdInformation> CatId2Info => RuntimeService.CatId2Info;

    public string GetComponentServiceName(string serviceId) => RuntimeService.GetComponentServiceName(serviceId);
    public string GetComponentServiceBadgeStyle(string serviceId) => RuntimeService.GetComponentServiceBadgeStyle(serviceId);
    public string GetComponentServiceHandleStyle(string serviceId) => RuntimeService.GetComponentServiceHandleStyle(serviceId);
    public (string Background, string Foreground) GetComponentServiceColors(string serviceId) => RuntimeService.GetComponentServiceColors(serviceId);
    public IReadOnlyList<KeyValuePair<string, (string, string?)>> GetBindableComponentServices(CapnpFbpComponentModel node) => RuntimeService.GetBindableComponentServices(node);
    public Task SwitchComponentServiceAsync(CapnpFbpComponentModel node, string componentServiceId) => RuntimeService.SwitchComponentServiceAsync(node, componentServiceId);

    private Task<IStartChannelsService?> ConnectToStartChannelsService(ConnectionManager conMan, string petName, string sturdyRef) =>
        RuntimeService.ConnectToStartChannelsServiceAsync(petName, sturdyRef);
    private Task<IRegistry?> ConnectToRegistryService(ConnectionManager conMan, string petName, string sturdyRef) =>
        RuntimeService.ConnectToRegistryServiceAsync(petName, sturdyRef);
    private Task HandleSturdyRefConnectedAsync((ulong, string, string) connection) =>
        RuntimeService.HandleSturdyRefConnectedAsync(connection);
    private Task HandleSturdyRefDisconnectedAsync((ulong, string) connection) =>
        RuntimeService.HandleSturdyRefDisconnectedAsync(connection);

    private static Component? CreateFromJson(JToken jComp)
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

    protected override async Task OnParametersSetAsync()
    {
        if (!FlowId.HasValue)
        {
            FlowId = Guid.NewGuid();
            NavigationManager.NavigateTo($"/flow/{FlowId.Value}", replace: true);
            return;
        }

        if (CurrentSession != null && CurrentSession.Id == FlowId.Value)
        {
            return;
        }

        if (CurrentSession != null)
        {
            CurrentSession.RuntimeService.StateChanged -= OnRuntimeStateChanged;
            SessionStore.MarkDetached(CurrentSession.Id);
        }

        CurrentSession = SessionStore.GetOrCreateSession(FlowId.Value);
        SessionStore.MarkAttached(FlowId.Value);
        CleanupService.RegisterCleanup(() =>
        {
            if (FlowId.HasValue)
            {
                SessionStore.MarkDetached(FlowId.Value);
            }
            return Task.CompletedTask;
        });
        CurrentSession.RuntimeService.StateChanged += OnRuntimeStateChanged;

        if (CurrentSession.Diagram == null)
        {
            CurrentSession.Diagram = CreateConfiguredDiagram(CurrentSession.RuntimeService);
            Diagram = CurrentSession.Diagram;
            if (CurrentSession.FlowDocument != null)
            {
                await LoadFlowFromJsonAsync(CurrentSession.FlowDocument);
            }
        }
        else
        {
            Diagram = CurrentSession.Diagram;
            CurrentSession.RuntimeService.Diagram = Diagram;
        }

        StateHasChanged();
    }

    private BlazorDiagram CreateConfiguredDiagram(IFbpRuntimeService runtime)
    {
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

        var diagram = new BlazorDiagram(options);
        runtime.Diagram = diagram;
        var ksb = diagram.GetBehavior<KeyboardShortcutsBehavior>();
        ksb?.RemoveShortcut("Delete", false, false, false);
        ksb?.SetShortcut("Delete", false, true, false, KeyboardShortcutsDefaults.DeleteSelection);

        if (File.Exists("Data/default_components.json"))
            runtime.InitDefaultComponents(File.ReadAllText("Data/default_components.json"));

        diagram.RegisterComponent<CapnpFbpRunnableComponentModel, CapnpFbpComponentWidget>();
        diagram.RegisterComponent<CapnpFbpProcessComponentModel, CapnpFbpComponentWidget>();
        diagram.RegisterComponent<CapnpFbpViewComponentModel, CapnpFbpViewComponentWidget>();
        diagram.RegisterComponent<CapnpFbpIipComponentModel, CapnpFbpIipComponentWidget>();
        diagram.RegisterComponent<UpdatePortNameNode, UpdatePortNameNodeWidget>();
        diagram.RegisterComponent<PortOptionsNode, PortOptionsNodeWidget>();
        diagram.RegisterComponent<NodeInformationControl, NodeInformationControlWidget>();
        diagram.RegisterComponent<LinkInformationControl, LinkInformationControlWidget>();
        diagram.RegisterComponent<AddPortControl, AddPortControlWidget>();
        diagram.RegisterComponent<ToggleEditNodeControl, ToggleEditNodeControlWidget>();
        diagram.RegisterComponent<RemoveProcessControl, RemoveProcessControlWidget>();
        diagram.RegisterComponent<RemoveLinkControl, RemoveLinkControlWidget>();
        diagram.RegisterComponent<LinkModel, FbpLinkWidget>(true);
        diagram.RegisterComponent<ChannelLinkLabelModel, ChannelLinkLabelWidget>();
        RegisterDiagramEvents(diagram);

        diagram.UnregisterBehavior<DragNewLinkBehavior>();
        diagram.RegisterBehavior(new FbpDragNewLinkBehavior(diagram));

        diagram.Nodes.Added += OnDiagramStructureChanged;
        diagram.Nodes.Removed += OnDiagramStructureChanged;
        diagram.Links.Added += OnDiagramStructureChanged;
        diagram.Links.Removed += OnDiagramStructureChanged;

        return diagram;
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

    private void QueueProcStructureSyncForLink(BaseLinkModel link)
    {
        if (
            _loadingFlow
            || link is not RememberCapnpPortsLinkModel { IsInternalProcLink: false } rememberedLink
        )
            return;

        if (rememberedLink.OutPortModel.Parent is CapnpFbpComponentModel sourceComponent)
            sourceComponent.QueueProcSyncForLinkChange();
        if (rememberedLink.InPortModel.Parent is CapnpFbpComponentModel targetComponent)
            targetComponent.QueueProcSyncForLinkChange();
    }

    private async Task SyncProcStructureAsync(IEnumerable<CapnpFbpComponentModel> nodes)
    {
        foreach (
            var node in nodes.Where(node => node != null && !node.IsInternalProcChild).Distinct()
        )
            await node.EnsureProcStructureSynchronizedAsync();
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

    private void RegisterNodeLayoutEvents(NodeModel node)
    {
        node.Moved += OnNodeMoved;
        node.SizeChanged += OnNodeSizeChanged;
    }

    private void OnNodeMoved(MovableModel movedModel)
    {
        if (movedModel is NodeModel movedNode)
            RefreshPortLayout(movedNode);
        ScheduleSessionSnapshot();
    }

    private void OnNodeSizeChanged(NodeModel node)
    {
        RefreshPortLayout(node);
        ScheduleSessionSnapshot();
    }

    private void RegisterDiagramEvents(BlazorDiagram diagram)
    {
        // diagram.Changed += () =>
        // {
        //     events.Add("Changed");
        //     StateHasChanged();
        // };

        // diagram.Nodes.Added += (n) => events.Add($"NodesAdded, NodeId={n.Id}");
        // diagram.Nodes.Removed += (n) => events.Add($"NodesRemoved, NodeId={n.Id}");

        // diagram.SelectionChanged += (m) =>
        // {
        //     events.Add($"SelectionChanged, Id={m.Id}, Type={m.GetType().Name}, Selected={m.Selected}");
        //     StateHasChanged();
        // };

        diagram.Links.Added += async l =>
        {
            diagram.Controls.AddFor(l).Add(new RemoveLinkControl(0.5, 0.5));
            if (l is RememberCapnpPortsLinkModel rememberedLink)
            {
                if (CurrentChannelStarterService is { } css)
                {
                    await Shared.Shared.ConnectLinkToRunningProcessesAsync(
                        ConMan,
                        css,
                        rememberedLink
                    );
                }
                RefreshPortLayout(l);
                QueueProcStructureSyncForLink(rememberedLink);
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
                        diagram.Links.Add(nl);
                        diagram.Links.Remove(l);
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
                        diagram.Links.Add(nl);
                        diagram.Links.Remove(l);
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

        diagram.Links.Removed += l =>
        {
            QueueProcStructureSyncForLink(l);
        };

        // diagram.Links.Removed += (l) => events.Add($"Links.Removed, LinkId={l.Id}");

        // diagram.PointerDown += (m, e) =>
        // {
        //     //Console.WriteLine($"MouseDown, Type={m?.GetType().Name}, ModelId={m?.Id}, Position=({e.ClientX}/{e.ClientY}");
        //     events.Add($"MouseDown, Type={m?.GetType().Name}, ModelId={m?.Id}");
        //     StateHasChanged();
        // };

        // diagram.PointerUp += (m, e) =>
        // {
        //     events.Add($"MouseUp, Type={m?.GetType().Name}, ModelId={m?.Id}");
        //     StateHasChanged();
        // };

        // diagram.PointerEnter += (m, e) =>
        // {
        //     //Console.WriteLine($"TouchStart, Type={m?.GetType().Name}, ModelId={m?.Id}, Position=({e.ClientX}/{e.ClientY}");
        //     events.Add($"TouchStart, Type={m?.GetType().Name}, ModelId={m?.Id}");
        //     StateHasChanged();
        // };

        // diagram.PointerLeave += (m, e) =>
        // {
        //     events.Add($"TouchEnd, Type={m?.GetType().Name}, ModelId={m?.Id}");
        //     StateHasChanged();
        // };

        diagram.PointerClick += (m, e) =>
        {
            if (m is CapnpFbpPortModel port)
            {
                var relativePt = diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);
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
                    NodeModel = port.Parent,
                    Container = diagram,
                };
                diagram.Nodes.Add(node);
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
        var width = (int)(Diagram.Container?.Width ?? 800);
        var height = (int)(Diagram.Container?.Height ?? 600);
        var x = Random.Next(0, Math.Max(1, width - 120));
        var y = Random.Next(0, Math.Max(1, height - 100));
        AddNode(x, y);
    }

    private void AddNode(double x, double y)
    {
        var node = new CapnpFbpComponentModel(new Point(x, y))
        {
            RuntimeService = RuntimeService,
            Diagram = Diagram,
        };
        Diagram.Nodes.Add(node);
    }

    protected void AddDefaultNode()
    {
        var width = (int)(Diagram.Container?.Width ?? 800);
        var height = (int)(Diagram.Container?.Height ?? 600);
        var x = Random.Next(0, Math.Max(1, width - 120));
        var y = Random.Next(0, Math.Max(1, height - 100));
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
        await LoadFlowFromJsonAsync(dia);
    }

    public async Task LoadFlowFromJsonAsync(JObject dia)
    {
        var oldNodeIdToNewNode = new Dictionary<string, NodeModel>();

        _loadingFlow = true;
        Diagram.SuspendRefresh = true;
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

                var diaNode = AddFbpNode(position, component, nodeObj, cmd);
                var oldNodeId = nodeObj["nodeId"]?.ToString() ?? nodeObj["node_id"]?.ToString() ?? "";
                oldNodeIdToNewNode[oldNodeId] = diaNode;
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
        }
        finally
        {
            Diagram.SuspendRefresh = false;
            _loadingFlow = false;
        }

        await SyncProcStructureAsync(Diagram.Nodes.OfType<CapnpFbpComponentModel>());
        Diagram.Refresh();
        try
        {
            await InvokeAsync(ZoomToFitFlow);
        }
        catch (InvalidOperationException)
        {
            ZoomToFitFlow();
        }
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

    public async Task<JObject> ExportFlowJsonAsync()
    {
        var (json, _) = await ExportFlowDocumentAsync(asMermaid: false);
        return json ?? new JObject();
    }

    public async Task<string> ExportFlowMermaidAsync()
    {
        var (_, mermaid) = await ExportFlowDocumentAsync(asMermaid: true);
        return mermaid ?? string.Empty;
    }

    public async Task<(JObject? Json, string? Mermaid)> ExportFlowDocumentAsync(bool asMermaid)
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
        var persistedLinks = Diagram
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
                            if (dia?["links"] is JArray links)
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

    protected async Task SaveFlow(bool asMermaid)
    {
        var (dia, mermaid) = await ExportFlowDocumentAsync(asMermaid);
        var content = asMermaid ? (mermaid ?? "") : (dia?.ToString() ?? "{}");
        var ext = asMermaid ? "mmd" : "json";
        await JsRuntime.InvokeVoidAsync(
            "saveAsBase64",
            $"flow.{ext}",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
        );
    }

    public async Task ClearDiagram()
    {
        if (CurrentSession != null)
        {
            CurrentSession.FlowDocument = null;
        }
        await RuntimeService.ClearDiagramAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _snapshotDebounceCts?.Cancel();
        _snapshotDebounceCts?.Dispose();
        _snapshotDebounceCts = null;

        _clearButtonCts?.Cancel();
        _clearButtonCts?.Dispose();
        _clearButtonCts = null;
        RuntimeService.StateChanged -= OnRuntimeStateChanged;
        CleanupService.UnregisterCleanup();
    }

    private void OnRuntimeStateChanged() => _ = InvokeAsync(StateHasChanged);

    private Task ExecuteNode(Model node) => RuntimeService.ExecuteNodeAsync(node);
    private Task ResetNode(Model node) => RuntimeService.ResetNodeAsync(node);
    private Task ExecuteFlow() => RuntimeService.ExecuteFlowAsync(() => InvokeAsync(StateHasChanged));

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
        JObject? initNode = null,
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
                    RegistryServiceIdToPetNameAndSturdyRef[componentServiceId] =
                        (
                            $"Service '{componentServiceId[..3]}..{componentServiceId[^3..]}' unavailable!",
                            null
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
                    JTokenType.Object => config?.ToString(Newtonsoft.Json.Formatting.Indented)
                        ?? "",
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
                            RuntimeService = RuntimeService,
                            Diagram = Diagram,
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
                            RuntimeService = RuntimeService,
                            Diagram = Diagram,
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
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported component type: {component.Type}"
                        );
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
                    RuntimeService = RuntimeService,
                    Diagram = Diagram,
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
                throw new NotSupportedException("Subflow components are not supported.");
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
                    RuntimeService = RuntimeService,
                    Diagram = Diagram,
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
    }

    private static void SetDefaultComponentSize(NodeModel node) =>
        node.Size = new Size(Shared.Shared.CardWidth, Shared.Shared.CardHeight);
}
