namespace BlazorDrawFBP.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using Capnp;
using Capnp.Rpc;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Registry;
using Newtonsoft.Json.Linq;

public class FbpRuntimeService : IFbpRuntimeService, IAsyncDisposable
{
    public const string NoRegistryServiceId = "no_service";
    private static readonly TimeSpan ExecuteFlowSettleTimeout = TimeSpan.FromSeconds(15);
    private const int ExecuteFlowSettlePollIntervalMs = 100;

    private static readonly (string Background, string Foreground)[] ComponentServicePalette =
    [
        ("#64748B", "#FFFFFF"),
        ("#0072B2", "#FFFFFF"),
        ("#D55E00", "#FFFFFF"),
        ("#009E73", "#FFFFFF"),
        ("#CC79A7", "#FFFFFF"),
        ("#E69F00", "#111827"),
        ("#56B4E9", "#111827"),
        ("#000000", "#FFFFFF"),
        ("#4B6B94", "#FFFFFF"),
        ("#882255", "#FFFFFF"),
        ("#117733", "#FFFFFF"),
        ("#332288", "#FFFFFF"),
        ("#999933", "#FFFFFF"),
        ("#AA4499", "#FFFFFF"),
        ("#88CCEE", "#111827"),
        ("#44AA99", "#FFFFFF"),
        ("#1D4ED8", "#FFFFFF"),
        ("#7C3AED", "#FFFFFF"),
        ("#B45309", "#FFFFFF"),
        ("#0F766E", "#FFFFFF"),
        ("#BE185D", "#FFFFFF"),
        ("#374151", "#FFFFFF"),
        ("#047857", "#FFFFFF"),
        ("#4338CA", "#FFFFFF"),
        ("#6D28D9", "#FFFFFF"),
        ("#A21CAF", "#FFFFFF"),
        ("#BE123C", "#FFFFFF"),
        ("#0E7490", "#FFFFFF"),
        ("#15803D", "#FFFFFF"),
        ("#B91C1C", "#FFFFFF"),
        ("#6B21A8", "#FFFFFF"),
        ("#9D174D", "#FFFFFF"),
        ("#854D0E", "#FFFFFF"),
        ("#1E40AF", "#FFFFFF"),
        ("#065F46", "#FFFFFF"),
        ("#991B1B", "#FFFFFF"),
        ("#581C87", "#FFFFFF"),
        ("#831843", "#FFFFFF"),
        ("#713F12", "#FFFFFF"),
        ("#1E3A8A", "#FFFFFF"),
        ("#064E3B", "#FFFFFF"),
        ("#7F1D1D", "#FFFFFF"),
        ("#4C1D95", "#FFFFFF"),
        ("#701A75", "#FFFFFF"),
        ("#365314", "#FFFFFF"),
        ("#172554", "#FFFFFF"),
        ("#022C22", "#FFFFFF"),
        ("#450A0A", "#FFFFFF"),
        ("#2E1065", "#FFFFFF"),
        ("#4A044E", "#FFFFFF"),
        ("#1A2E05", "#FFFFFF"),
        ("#030712", "#FFFFFF"),
        ("#2563EB", "#FFFFFF"),
        ("#7C2D12", "#FFFFFF"),
        ("#059669", "#FFFFFF"),
        ("#D97706", "#111827"),
        ("#4F46E5", "#FFFFFF"),
        ("#DB2777", "#FFFFFF"),
        ("#0D9488", "#FFFFFF"),
        ("#EA580C", "#FFFFFF"),
        ("#9333EA", "#FFFFFF"),
        ("#65A30D", "#111827"),
        ("#0284C7", "#FFFFFF"),
        ("#E11D48", "#FFFFFF"),
        ("#16A34A", "#FFFFFF"),
        ("#CA8A04", "#111827"),
        ("#6366F1", "#FFFFFF"),
        ("#C026D3", "#FFFFFF"),
        ("#14B8A6", "#111827"),
        ("#F97316", "#111827"),
        ("#A855F7", "#FFFFFF"),
        ("#84CC16", "#111827"),
        ("#06B6D4", "#111827"),
        ("#F43F5E", "#FFFFFF"),
        ("#22C55E", "#111827"),
        ("#EAB308", "#111827"),
        ("#818CF8", "#111827"),
        ("#D946EF", "#FFFFFF"),
        ("#2DD4BF", "#111827"),
        ("#FB923C", "#111827"),
        ("#C084FC", "#111827"),
        ("#A3E635", "#111827"),
        ("#38BDF8", "#111827"),
        ("#FB7185", "#111827"),
        ("#4ADE80", "#111827"),
        ("#FACC15", "#111827"),
        ("#A5B4FC", "#111827"),
        ("#E879F9", "#111827"),
        ("#5EEAD4", "#111827"),
        ("#FDBA74", "#111827"),
        ("#D8B4FE", "#111827"),
        ("#BEF264", "#111827"),
        ("#7DD3FC", "#111827"),
        ("#FDA4AF", "#111827"),
        ("#86EFAC", "#111827"),
        ("#FDE047", "#111827"),
        ("#C7D2FE", "#111827"),
        ("#F0ABFC", "#111827"),
        ("#99F6E4", "#111827"),
        ("#FED7AA", "#111827"),
        ("#E9D5FF", "#111827"),
        ("#D9F99D", "#111827"),
        ("#BAE6FD", "#111827"),
        ("#FECDD3", "#111827"),
        ("#BBF7D0", "#111827"),
        ("#FEF08A", "#111827"),
        ("#E0E7FF", "#111827"),
        ("#FAE8FF", "#111827"),
        ("#CCFBF1", "#111827"),
        ("#FFEDD5", "#111827"),
        ("#F3E8FF", "#111827"),
        ("#ECFCCB", "#111827"),
        ("#E0F2FE", "#111827"),
        ("#FFE4E6", "#111827"),
        ("#DCFCE7", "#111827"),
        ("#FEF9C3", "#111827"),
        ("#EEF2FF", "#111827"),
        ("#FDF4FF", "#111827"),
        ("#F0FDFA", "#111827"),
        ("#FFF7ED", "#111827"),
        ("#FAF5FF", "#111827"),
        ("#F7FEE7", "#111827"),
        ("#F0F9FF", "#111827"),
        ("#FFF1F2", "#111827"),
        ("#F0FDF4", "#111827"),
        ("#FEFCE8", "#111827"),
        ("#2B2D42", "#FFFFFF"),
        ("#8D99AE", "#111827"),
        ("#EDF2F4", "#111827"),
        ("#EF233C", "#FFFFFF"),
        ("#D90429", "#FFFFFF"),
        ("#264653", "#FFFFFF"),
        ("#2A9D8F", "#FFFFFF"),
        ("#E76F51", "#FFFFFF"),
        ("#606C38", "#FFFFFF"),
        ("#283618", "#FFFFFF"),
        ("#DDA15E", "#111827"),
        ("#BC6C25", "#FFFFFF"),
        ("#03045E", "#FFFFFF"),
        ("#023E8A", "#FFFFFF"),
        ("#0077B6", "#FFFFFF"),
        ("#0096C7", "#FFFFFF"),
        ("#00B4D8", "#111827"),
        ("#48CAE4", "#111827"),
        ("#90E0EF", "#111827"),
        ("#ADE8F4", "#111827"),
        ("#CAF0F8", "#111827"),
        ("#1F77B4", "#FFFFFF"),
        ("#FF7F0E", "#111827"),
        ("#2CA02C", "#FFFFFF"),
        ("#D62728", "#FFFFFF"),
        ("#9467BD", "#FFFFFF"),
        ("#8C564B", "#FFFFFF"),
        ("#E377C2", "#111827"),
        ("#7F7F7F", "#FFFFFF"),
        ("#BCBD22", "#111827"),
        ("#17BECF", "#111827"),
        ("#4E79A7", "#FFFFFF"),
        ("#F28E2B", "#111827"),
        ("#E15759", "#FFFFFF"),
        ("#76B7B2", "#111827"),
        ("#59A14F", "#FFFFFF"),
        ("#EDC948", "#111827"),
        ("#B07AA1", "#FFFFFF"),
        ("#FF9DA7", "#111827"),
        ("#9C755F", "#FFFFFF"),
        ("#BAB0AC", "#111827"),
        ("#332288", "#FFFFFF"),
        ("#117733", "#FFFFFF"),
        ("#44AA99", "#FFFFFF"),
        ("#88CCEE", "#111827"),
        ("#E69F00", "#111827"),
        ("#56B4E9", "#111827"),
        ("#009E73", "#FFFFFF"),
        ("#F0E442", "#111827"),
        ("#0072B2", "#FFFFFF"),
        ("#D55E00", "#FFFFFF"),
        ("#CC79A7", "#FFFFFF"),
        ("#DC267F", "#FFFFFF"),
    ];

    public ConnectionManager ConnectionManager { get; }

    private BlazorDiagram? _diagram;
    public BlazorDiagram? Diagram
    {
        get => _diagram;
        set
        {
            if (_diagram != null)
            {
                _diagram.Nodes.Added -= OnDiagramNodesChanged;
                _diagram.Nodes.Removed -= OnDiagramNodesChanged;
            }
            _diagram = value;
            if (_diagram != null)
            {
                _diagram.Nodes.Added += OnDiagramNodesChanged;
                _diagram.Nodes.Removed += OnDiagramNodesChanged;
            }
            NotifyStateChanged();
        }
    }

    private void OnDiagramNodesChanged(NodeModel node) => NotifyStateChanged();

    public Dictionary<string, IRegistry> ServiceId2Registries { get; } = [];
    public Dictionary<string, (string, string?)> RegistryServiceIdToPetNameAndSturdyRef { get; } = [];
    public Dictionary<string, IStartChannelsService> ServiceId2ChannelStarterServices { get; } = [];
    public Dictionary<string, (string, string)> ChannelServiceIdToPetNameAndSturdyRef { get; } = [];
    public Dictionary<string, Proxy> SturdyRef2Services { get; } = [];
    public Dictionary<(string, string), Component> ServiceIdAndComponentId2Component { get; } = [];
    public Dictionary<string, HashSet<(string, string)>> CatId2CompServiceIdAndComponentIds { get; } = [];
    public Dictionary<string, IdInformation> CatId2Info { get; } = [];

    public Dictionary<ulong, Type> InterfaceIdToType { get; } = new()
    {
        { Shared.Shared.RegistryInterfaceId, typeof(IRegistry) },
        { Shared.Shared.ChannelStarterInterfaceId, typeof(IStartChannelsService) },
    };

    public IStartChannelsService? CurrentChannelStarterService =>
        ServiceId2ChannelStarterServices.Values.FirstOrDefault();

    public bool HasConnectedComponentService => ServiceId2Registries.Count > 0;
    public bool HasConnectedChannelService => ServiceId2ChannelStarterServices.Count > 0;

    public bool HasComponentsOnCanvas =>
        Diagram?.Nodes.Any(IsExecutableFlowNode) == true;

    public bool HasBusyLifecycleNodes =>
        Diagram?.Nodes.Any(node => node switch
        {
            CapnpFbpComponentModel compNode => compNode.IsLifecycleBusy,
            CapnpFbpViewComponentModel viewNode => viewNode.IsLifecycleBusy,
            CapnpFbpIipComponentModel iipNode => iipNode.IsLifecycleBusy,
            _ => false,
        }) == true;

    public bool CanExecuteFlow =>
        HasConnectedComponentService
        && HasConnectedChannelService
        && HasComponentsOnCanvas
        && !HasBusyLifecycleNodes
        && !IsExecutingFlow;

    public bool IsExecutingFlow { get; private set; }

    public string ExecuteFlowButtonTitle =>
        !HasConnectedComponentService || !HasConnectedChannelService
            ? "Connect both a components service and a channel service to execute the flow."
            : !HasComponentsOnCanvas
                ? "Add at least one component to the canvas to execute the flow."
                : IsExecutingFlow
                    ? "Flow execution is already starting the current graph."
                    : HasBusyLifecycleNodes
                        ? "Processes are currently starting or stopping."
                        : "Execute entire flow";

    public event Action? StateChanged;

    public FbpRuntimeService(ConnectionManager connectionManager)
    {
        ConnectionManager = connectionManager;
        RegistryServiceIdToPetNameAndSturdyRef[NoRegistryServiceId] = ("No service", null);
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();

    public string GetComponentServiceName(string serviceId) =>
        RegistryServiceIdToPetNameAndSturdyRef
            .GetValueOrDefault(serviceId, ("Unknown service!", null))
            .Item1;

    public string GetComponentServiceBadgeStyle(string serviceId)
    {
        var (background, foreground) = GetComponentServiceColors(serviceId);
        return $"background-color: {background} !important; color: {foreground} !important;";
    }

    public string GetComponentServiceHandleStyle(string serviceId)
    {
        var (background, foreground) = GetComponentServiceColors(serviceId);
        return $"background-color: {background}; color: {foreground};";
    }

    public (string Background, string Foreground) GetComponentServiceColors(string serviceId)
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

    public IReadOnlyList<KeyValuePair<string, (string, string?)>> GetBindableComponentServices(
        CapnpFbpComponentModel node
    )
    {
        if (node == null)
            return [];

        return RegistryServiceIdToPetNameAndSturdyRef
            .Where(entry =>
                entry.Key == node.ComponentServiceId
                || TryGetBindableComponent(node, entry.Key, out _)
            )
            .ToList();
    }

    public async Task SwitchComponentServiceAsync(
        CapnpFbpComponentModel node,
        string componentServiceId
    )
    {
        if (
            node == null
            || node.IsLifecycleBusy
            || string.IsNullOrWhiteSpace(componentServiceId)
            || componentServiceId == node.ComponentServiceId
        )
        {
            return;
        }

        if (!TryGetBindableComponent(node, componentServiceId, out var component))
            return;

        await node.RebindToComponentServiceAsync(component, componentServiceId);
        NotifyStateChanged();
    }

    public bool TryGetBindableComponent(
        CapnpFbpComponentModel node,
        string componentServiceId,
        [NotNullWhen(true)] out Component? component
    )
    {
        if (
            node == null
            || string.IsNullOrWhiteSpace(node.ComponentId)
            || !ServiceIdAndComponentId2Component.TryGetValue(
                (componentServiceId, node.ComponentId),
                out component
            )
        )
        {
            component = null;
            return false;
        }

        return node switch
        {
            CapnpFbpRunnableComponentModel => component.Type == Component.ComponentType.standard,
            CapnpFbpProcessComponentModel => component.Type == Component.ComponentType.process,
            _ => false,
        };
    }

    public async Task<IStartChannelsService?> ConnectToStartChannelsServiceAsync(
        string petName,
        string sturdyRef
    )
    {
        try
        {
            var service = await ConnectionManager.Connect<IStartChannelsService>(sturdyRef);
            if (service == null)
                return null;
            var info = await service.Info();
            Console.WriteLine("Connected to channel starter service @ " + sturdyRef);
            var petName2 = Shared.Shared.MakeUniqueKey(
                ServiceId2ChannelStarterServices,
                petName ?? "chan_start_serv"
            );
            ServiceId2ChannelStarterServices[info.Id] = Proxy.Share(service);
            if (Proxy.Share(service) is Proxy proxy)
                SturdyRef2Services[sturdyRef] = proxy;
            ChannelServiceIdToPetNameAndSturdyRef[info.Id] = (petName2, sturdyRef);
            NotifyStateChanged();
            return service;
        }
        catch (RpcException)
        {
            Console.WriteLine("Couldn't connect to channel starter service @ " + sturdyRef);
        }

        return null;
    }

    public async Task<IRegistry?> ConnectToRegistryServiceAsync(
        string petName,
        string sturdyRef
    )
    {
        IRegistry? reg = null;
        try
        {
            reg = await ConnectionManager.Connect<IRegistry>(sturdyRef);
            if (reg == null)
                return null;
            var info = await reg.Info();
            Console.WriteLine("Connected to components registry @ " + sturdyRef);
            var petName2 = Shared.Shared.MakeUniqueKey(ServiceId2Registries, petName ?? "reg_serv");
            ServiceId2Registries[info.Id] = Proxy.Share(reg);
            if (Proxy.Share(reg) is Proxy proxy)
                SturdyRef2Services[sturdyRef] = proxy;
            RegistryServiceIdToPetNameAndSturdyRef[info.Id] = (petName2, sturdyRef);
            Console.WriteLine("added petName2: " + petName2 + " and sturdyRef: " + sturdyRef);
        }
        catch (RpcException)
        {
            Console.WriteLine("Couldn't connect to components registry @ " + sturdyRef);
            return null;
        }

        if (reg != null)
            await LoadComponentsFromRegistryAsync(reg, sturdyRef);

        NotifyStateChanged();
        return reg;
    }

    public async Task HandleSturdyRefConnectedAsync((ulong interfaceId, string sturdyRef, string petName) connection)
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
                var petName2 = Shared.Shared.MakeUniqueKey(
                    ServiceId2ChannelStarterServices,
                    petName
                );
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
            await LoadComponentsFromRegistryAsync(Proxy.Share(reg), sturdyRef);
            updatedConnections = true;
        }

        if (updatedConnections)
            NotifyStateChanged();
    }

    public async Task HandleSturdyRefDisconnectedAsync((ulong interfaceId, string sturdyRef) connection)
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

        NotifyStateChanged();
        await Task.CompletedTask;
    }

    public void DisconnectChannelStarterService(string sturdyRef)
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

        NotifyStateChanged();
    }

    public void DisconnectRegistryService(string sturdyRef)
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

        NotifyStateChanged();
    }

    public void RemoveRegistryPaletteEntries(string serviceId)
    {
        foreach (
            var componentKey in ServiceIdAndComponentId2Component
                .Keys.Where(componentKey => componentKey.Item1 == serviceId)
                .ToList()
        )
        {
            ServiceIdAndComponentId2Component.Remove(componentKey);
        }

        foreach (var categoryId in CatId2CompServiceIdAndComponentIds.Keys.ToList())
        {
            CatId2CompServiceIdAndComponentIds[categoryId]
                .RemoveWhere(componentKey => componentKey.Item1 == serviceId);
            if (
                !CatId2Info.ContainsKey(categoryId)
                && CatId2CompServiceIdAndComponentIds[categoryId].Count == 0
            )
            {
                CatId2CompServiceIdAndComponentIds.Remove(categoryId);
            }
        }

        NotifyStateChanged();
    }

    public async Task LoadComponentsFromRegistryAsync(IRegistry reg, string sturdyRef)
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
                    ServiceIdAndComponentId2Component[(info.Id, e.Id)] = await holder.Value();
                }
                catch (System.Exception ex)
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

        NotifyStateChanged();
    }

    public void InitDefaultComponents(string jsonContent)
    {
        var defComps = JObject.Parse(jsonContent);
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

            var component = CreateFromJson(comp);
            if (component == null)
                continue;
            value.Add((NoRegistryServiceId, component.Info.Id));
            ServiceIdAndComponentId2Component[(NoRegistryServiceId, component.Info.Id)] = component;
        }

        NotifyStateChanged();
    }

    public static Component? CreateFromJson(JToken jComp)
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

    public async Task ClearDiagramAsync()
    {
        if (Diagram == null)
            return;

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
        NotifyStateChanged();
    }

    public async Task ExecuteNodeAsync(Model node)
    {
        switch (node)
        {
            case CapnpFbpComponentModel compNode when compNode.CanStart:
                await compNode.StartProcess(ConnectionManager);
                break;
            case CapnpFbpViewComponentModel viewNode when viewNode.CanStart:
                await viewNode.StartProcess(ConnectionManager);
                break;
            case CapnpFbpIipComponentModel iipNode when iipNode.CanStart:
                await iipNode.SendIip(ConnectionManager);
                break;
        }
        NotifyStateChanged();
    }

    public async Task ResetNodeAsync(Model node)
    {
        switch (node)
        {
            case CapnpFbpComponentModel compNode when compNode.CanStop:
                await compNode.StopProcess();
                break;
            case CapnpFbpViewComponentModel viewNode when viewNode.CanStop:
                await viewNode.StopProcess();
                break;
            case CapnpFbpIipComponentModel iipNode:
                await iipNode.ResetExecution();
                break;
        }
        NotifyStateChanged();
    }

    public async Task ExecuteFlowAsync(Action? onStateChanged = null)
    {
        if (!CanExecuteFlow || Diagram == null)
            return;

        IsExecutingFlow = true;
        NotifyStateChanged();
        onStateChanged?.Invoke();

        try
        {
            var startupOrder = GetFlowStartupOrder();
            var startupNodes = startupOrder
                .Where(node => node is not CapnpFbpIipComponentModel)
                .ToList();
            var iipNodes = startupOrder.OfType<CapnpFbpIipComponentModel>().Cast<Model>().ToList();

            foreach (var node in startupNodes)
            {
                await ExecuteNodeAsync(node);
                await WaitForNodesToSettleAsync([node], onStateChanged);
                node.Refresh();
                NotifyStateChanged();
                onStateChanged?.Invoke();
            }

            if (!await WaitForNodesToSettleAsync(startupNodes, onStateChanged))
            {
                var busyNodes = string.Join(
                    ", ",
                    startupNodes.Where(IsLifecycleBusy).Select(GetFlowNodeName)
                );
                Console.WriteLine(
                    $"FbpRuntimeService::ExecuteFlow: timed out waiting for startup to settle before dispatching IIPs. Busy nodes: {busyNodes}"
                );
                return;
            }

            foreach (var node in iipNodes)
            {
                await ExecuteNodeAsync(node);
                node.Refresh();
                NotifyStateChanged();
                onStateChanged?.Invoke();
            }
        }
        catch (System.Exception ex)
        {
            Console.WriteLine($"FbpRuntimeService::ExecuteFlow: Caught exception: {ex}");
        }
        finally
        {
            IsExecutingFlow = false;
            NotifyStateChanged();
            onStateChanged?.Invoke();
        }
    }

    public IReadOnlyList<Model> GetFlowStartupOrder()
    {
        if (Diagram == null)
            return [];

        var nodes = Diagram.Nodes.Where(IsExecutableFlowNode).Cast<Model>().ToList();
        var originalOrder = nodes
            .Select((node, index) => (node, index))
            .ToDictionary(x => x.node, x => x.index);
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

            if (outgoing[target].Add(source))
                indegree[source]++;
        }

        var ready = nodes
            .Where(node => indegree[node] == 0)
            .OrderBy(node => originalOrder[node])
            .ToList();
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

        foreach (
            var node in nodes
                .Where(node => !ordered.Contains(node))
                .OrderBy(node => originalOrder[node])
        )
            ordered.Add(node);

        return ordered;
    }

    public async Task<bool> WaitForNodesToSettleAsync(IEnumerable<Model> nodes, Action? onStateChanged = null)
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
            NotifyStateChanged();
            onStateChanged?.Invoke();
        }

        return trackedNodes.All(node => !IsLifecycleBusy(node));
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

    public async ValueTask DisposeAsync()
    {
        await ClearDiagramAsync();

        foreach (var proxy in SturdyRef2Services.Values)
        {
            try { proxy.Dispose(); } catch { }
        }
        SturdyRef2Services.Clear();

        foreach (var registry in ServiceId2Registries.Values)
        {
            try { registry.Dispose(); } catch { }
        }
        ServiceId2Registries.Clear();

        foreach (var service in ServiceId2ChannelStarterServices.Values)
        {
            try { service.Dispose(); } catch { }
        }
        ServiceId2ChannelStarterServices.Clear();
    }
}
