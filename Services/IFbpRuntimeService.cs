namespace BlazorDrawFBP.Services;

using System.Collections.Generic;
using System.Threading.Tasks;
using Blazor.Diagrams;
using BlazorDrawFBP.Models;
using Mas.Infrastructure.Common;
using Mas.Schema.Fbp;

public interface IFbpRuntimeService
{
    ConnectionManager ConnectionManager { get; }
    IStartChannelsService? CurrentChannelStarterService { get; }
    BlazorDiagram? Diagram { get; }

    string GetComponentServiceName(string serviceId);
    string GetComponentServiceBadgeStyle(string serviceId);
    (string Background, string Foreground) GetComponentServiceColors(string serviceId);
    IReadOnlyList<KeyValuePair<string, (string, string?)>> GetBindableComponentServices(CapnpFbpComponentModel node);
    Task SwitchComponentServiceAsync(CapnpFbpComponentModel node, string componentServiceId);
}
