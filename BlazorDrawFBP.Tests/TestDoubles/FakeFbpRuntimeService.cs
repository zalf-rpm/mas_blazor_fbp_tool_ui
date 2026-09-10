namespace BlazorDrawFBP.Tests.TestDoubles;

using System.Collections.Generic;
using System.Threading.Tasks;
using Blazor.Diagrams;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Services;
using Mas.Infrastructure.Common;
using Mas.Schema.Fbp;

public class FakeFbpRuntimeService : IFbpRuntimeService
{
    public ConnectionManager ConnectionManager { get; set; } = new();
    public IStartChannelsService? CurrentChannelStarterService { get; set; }
    public BlazorDiagram? Diagram { get; set; }

    public Dictionary<string, string> ServiceNames { get; } = [];
    public List<(CapnpFbpComponentModel Node, string ServiceId)> SwitchServiceCalls { get; } = [];

    public string GetComponentServiceName(string serviceId) =>
        ServiceNames.GetValueOrDefault(serviceId, "Test Service");

    public string GetComponentServiceBadgeStyle(string serviceId) =>
        "background-color: #0072B2; color: #FFFFFF;";

    public (string Background, string Foreground) GetComponentServiceColors(string serviceId) =>
        ("#0072B2", "#FFFFFF");

    public IReadOnlyList<KeyValuePair<string, (string, string?)>> GetBindableComponentServices(CapnpFbpComponentModel node) =>
        [new KeyValuePair<string, (string, string?)>("svc1", ("Service 1", null))];

    public Task SwitchComponentServiceAsync(CapnpFbpComponentModel node, string componentServiceId)
    {
        SwitchServiceCalls.Add((node, componentServiceId));
        node.ComponentServiceId = componentServiceId;
        return Task.CompletedTask;
    }
}
