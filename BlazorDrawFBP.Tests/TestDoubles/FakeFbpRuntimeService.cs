using Blazor.Diagrams;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Services;
using Capnp.Rpc;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Registry;

namespace BlazorDrawFBP.Tests.TestDoubles;

public class FakeFbpRuntimeService : IFbpRuntimeService
{
    public Dictionary<string, string> ServiceNames { get; } = [];
    public List<(CapnpFbpComponentModel Node, string ServiceId)> SwitchServiceCalls { get; } = [];
    public ConnectionManager ConnectionManager { get; set; } = new();
    public IStartChannelsService? CurrentChannelStarterService { get; set; }
    public BlazorDiagram? Diagram { get; set; }

    public bool HasConnectedComponentService => ServiceId2Registries.Count > 0;
    public bool HasConnectedChannelService => ServiceId2ChannelStarterServices.Count > 0;

    public bool HasComponentsOnCanvas =>
        Diagram?.Nodes.Any(node =>
            node
                is CapnpFbpComponentModel
                    or CapnpFbpViewComponentModel
                    or CapnpFbpIipComponentModel
        ) == true;

    public bool HasBusyLifecycleNodes { get; set; }
    public bool CanExecuteFlow =>
        HasConnectedComponentService && HasConnectedChannelService && HasComponentsOnCanvas;
    public bool IsExecutingFlow { get; set; }
    public string ExecuteFlowButtonTitle => "Execute flow";

    public Dictionary<ulong, Type> InterfaceIdToType { get; } = [];
    public Dictionary<string, IRegistry> ServiceId2Registries { get; } = [];
    public Dictionary<string, (string, string?)> RegistryServiceIdToPetNameAndSturdyRef { get; } =
    [];
    public Dictionary<string, IStartChannelsService> ServiceId2ChannelStarterServices { get; } = [];
    public Dictionary<string, (string, string)> ChannelServiceIdToPetNameAndSturdyRef { get; } = [];
    public Dictionary<string, Proxy> SturdyRef2Services { get; } = [];
    public Dictionary<(string, string), Component> ServiceIdAndComponentId2Component { get; } = [];
    public Dictionary<
        string,
        HashSet<(string, string)>
    > CatId2CompServiceIdAndComponentIds { get; } = [];
    public Dictionary<string, IdInformation> CatId2Info { get; } = [];

    public event Action? StateChanged;
    public event Action<ServiceConnectionDroppedEventArgs>? ServiceConnectionDropped;

    public void NotifyServiceConnectionDropped(
        string serviceType,
        string serviceId,
        string petName,
        string? sturdyRef = null,
        string? customMessage = null
    )
    {
        ServiceConnectionDropped?.Invoke(
            new ServiceConnectionDroppedEventArgs(
                serviceType,
                serviceId,
                petName,
                sturdyRef,
                customMessage
            )
        );
    }

    public string GetComponentServiceName(string serviceId)
    {
        return ServiceNames.GetValueOrDefault(serviceId, "Test Service");
    }

    public string GetComponentServiceBadgeStyle(string serviceId)
    {
        return "background-color: #0072B2; color: #FFFFFF;";
    }

    public string GetComponentServiceHandleStyle(string serviceId)
    {
        return "background-color: #0072B2; color: #FFFFFF;";
    }

    public (string Background, string Foreground) GetComponentServiceColors(string serviceId)
    {
        return ("#0072B2", "#FFFFFF");
    }

    public IReadOnlyList<KeyValuePair<string, (string, string?)>> GetBindableComponentServices(
        CapnpFbpComponentModel node
    )
    {
        return [new KeyValuePair<string, (string, string?)>("svc1", ("Service 1", null))];
    }

    public Task SwitchComponentServiceAsync(CapnpFbpComponentModel node, string componentServiceId)
    {
        SwitchServiceCalls.Add((node, componentServiceId));
        node.ComponentServiceId = componentServiceId;
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task<IStartChannelsService?> ConnectToStartChannelsServiceAsync(
        string petName,
        string sturdyRef
    )
    {
        return Task.FromResult<IStartChannelsService?>(null);
    }

    public Task<IRegistry?> ConnectToRegistryServiceAsync(string petName, string sturdyRef)
    {
        return Task.FromResult<IRegistry?>(null);
    }

    public Task HandleSturdyRefConnectedAsync(
        (ulong interfaceId, string sturdyRef, string petName) connection
    )
    {
        return Task.CompletedTask;
    }

    public Task HandleSturdyRefDisconnectedAsync((ulong interfaceId, string sturdyRef) connection)
    {
        return Task.CompletedTask;
    }

    public void DisconnectChannelStarterService(string sturdyRef) { }

    public void DisconnectRegistryService(string sturdyRef) { }

    public void DisconnectChannelStarterServiceById(string serviceId) { }

    public void DisconnectRegistryServiceById(string serviceId) { }

    public Task<int> CheckConnectedServicesHealthAsync(
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(0);
    }

    public void InitDefaultComponents(string jsonContent) { }

    public Task ClearDiagramAsync()
    {
        Diagram?.Nodes.Clear();
        Diagram?.Links.Clear();
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task ExecuteNodeAsync(Model node)
    {
        return Task.CompletedTask;
    }

    public Task ResetNodeAsync(Model node)
    {
        return Task.CompletedTask;
    }

    public Task ExecuteFlowAsync(Action? onStateChanged = null)
    {
        return Task.CompletedTask;
    }
}
