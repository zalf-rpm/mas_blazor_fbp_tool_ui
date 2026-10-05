using System.Collections.Concurrent;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using Capnp.Rpc;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Registry;

namespace BlazorDrawFBP.Services;

public interface IFbpRuntimeService
{
    ConnectionManager ConnectionManager { get; }
    IStartChannelsService? CurrentChannelStarterService { get; }
    BlazorDiagram? Diagram { get; set; }

    bool HasConnectedComponentService { get; }
    bool HasConnectedChannelService { get; }
    bool HasComponentsOnCanvas { get; }
    bool HasBusyLifecycleNodes { get; }
    bool CanExecuteFlow { get; }
    bool IsExecutingFlow { get; }
    string ExecuteFlowButtonTitle { get; }

    Dictionary<ulong, Type> InterfaceIdToType { get; }
    Dictionary<string, IRegistry> ServiceId2Registries { get; }
    Dictionary<string, (string, string?)> RegistryServiceIdToPetNameAndSturdyRef { get; }
    Dictionary<string, IStartChannelsService> ServiceId2ChannelStarterServices { get; }
    Dictionary<string, (string, string)> ChannelServiceIdToPetNameAndSturdyRef { get; }
    ConcurrentDictionary<string, Proxy> SturdyRef2Services { get; }
    Dictionary<(string, string), Component> ServiceIdAndComponentId2Component { get; }
    Dictionary<string, HashSet<(string, string)>> CatId2CompServiceIdAndComponentIds { get; }
    Dictionary<string, IdInformation> CatId2Info { get; }

    string GetComponentServiceName(string serviceId);
    string GetComponentServiceBadgeStyle(string serviceId);
    string GetComponentServiceHandleStyle(string serviceId);
    (string Background, string Foreground) GetComponentServiceColors(string serviceId);
    IReadOnlyList<KeyValuePair<string, (string, string?)>> GetBindableComponentServices(
        CapnpFbpComponentModel node
    );
    Task SwitchComponentServiceAsync(CapnpFbpComponentModel node, string componentServiceId);

    Task<IStartChannelsService?> ConnectToStartChannelsServiceAsync(
        string petName,
        string sturdyRef
    );
    Task<IRegistry?> ConnectToRegistryServiceAsync(string petName, string sturdyRef);
    bool IsConnected(string sturdyRef);
    Task<bool> ConnectBookmarkAsync(ulong interfaceId, string petName, string sturdyRef);
    void DisconnectBookmark(ulong interfaceId, string sturdyRef);
    void DisconnectChannelStarterService(string sturdyRef);
    void DisconnectRegistryService(string sturdyRef);
    void DisconnectChannelStarterServiceById(string serviceId);
    void DisconnectRegistryServiceById(string serviceId);
    Task<int> CheckConnectedServicesHealthAsync(CancellationToken cancellationToken = default);

    void InitDefaultComponents(string jsonContent);
    Task ClearDiagramAsync();
    Task ExecuteNodeAsync(Model node);
    Task ResetNodeAsync(Model node);
    Task ExecuteFlowAsync(Action? onStateChanged = null);
    event Action? StateChanged;
    event Action<ServiceConnectionDroppedEventArgs>? ServiceConnectionDropped;

    void NotifyServiceConnectionDropped(
        string serviceType,
        string serviceId,
        string petName,
        string? sturdyRef = null,
        string? customMessage = null
    );
}

public class ServiceConnectionDroppedEventArgs : EventArgs
{
    public ServiceConnectionDroppedEventArgs(
        string serviceType,
        string serviceId,
        string petName,
        string? sturdyRef = null,
        string? customMessage = null
    )
    {
        ServiceType = serviceType;
        ServiceId = serviceId;
        PetName = petName;
        SturdyRef = sturdyRef;
        var displayName =
            !string.IsNullOrWhiteSpace(petName) ? petName
            : !string.IsNullOrWhiteSpace(serviceId) ? serviceId
            : "Unknown";
        Message =
            customMessage ?? $"Connection dropped: {serviceType} '{displayName}' is unreachable.";
    }

    public string ServiceType { get; }
    public string ServiceId { get; }
    public string PetName { get; }
    public string? SturdyRef { get; }
    public string Message { get; }
}
