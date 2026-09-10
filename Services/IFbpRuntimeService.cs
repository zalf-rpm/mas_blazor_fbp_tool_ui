namespace BlazorDrawFBP.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using Capnp.Rpc;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Registry;

public interface IFbpRuntimeService
{
    ConnectionManager ConnectionManager { get; }
    IStartChannelsService? CurrentChannelStarterService { get; }
    BlazorDiagram? Diagram { get; set; }

    bool HasConnectedComponentService { get; }
    bool HasConnectedChannelService { get; }
    bool HasBusyLifecycleNodes { get; }
    bool CanExecuteFlow { get; }
    bool IsExecutingFlow { get; }
    string ExecuteFlowButtonTitle { get; }

    Dictionary<ulong, Type> InterfaceIdToType { get; }
    Dictionary<string, IRegistry> ServiceId2Registries { get; }
    Dictionary<string, (string, string?)> RegistryServiceIdToPetNameAndSturdyRef { get; }
    Dictionary<string, IStartChannelsService> ServiceId2ChannelStarterServices { get; }
    Dictionary<string, (string, string)> ChannelServiceIdToPetNameAndSturdyRef { get; }
    Dictionary<string, Proxy> SturdyRef2Services { get; }
    Dictionary<(string, string), Component> ServiceIdAndComponentId2Component { get; }
    Dictionary<string, HashSet<(string, string)>> CatId2CompServiceIdAndComponentIds { get; }
    Dictionary<string, IdInformation> CatId2Info { get; }

    string GetComponentServiceName(string serviceId);
    string GetComponentServiceBadgeStyle(string serviceId);
    string GetComponentServiceHandleStyle(string serviceId);
    (string Background, string Foreground) GetComponentServiceColors(string serviceId);
    IReadOnlyList<KeyValuePair<string, (string, string?)>> GetBindableComponentServices(CapnpFbpComponentModel node);
    Task SwitchComponentServiceAsync(CapnpFbpComponentModel node, string componentServiceId);

    Task<IStartChannelsService?> ConnectToStartChannelsServiceAsync(string petName, string sturdyRef);
    Task<IRegistry?> ConnectToRegistryServiceAsync(string petName, string sturdyRef);
    Task HandleSturdyRefConnectedAsync((ulong interfaceId, string sturdyRef, string petName) connection);
    Task HandleSturdyRefDisconnectedAsync((ulong interfaceId, string sturdyRef) connection);
    void DisconnectChannelStarterService(string sturdyRef);
    void DisconnectRegistryService(string sturdyRef);

    void InitDefaultComponents(string jsonContent);
    Task ClearDiagramAsync();
    Task ExecuteNodeAsync(Model node);
    Task ResetNodeAsync(Model node);
    Task ExecuteFlowAsync(Action? onStateChanged = null);

    event Action? StateChanged;
}
