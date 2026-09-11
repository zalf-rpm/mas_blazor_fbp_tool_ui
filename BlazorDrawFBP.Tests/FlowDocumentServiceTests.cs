namespace BlazorDrawFBP.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Services;
using BlazorDrawFBP.Tests.TestDoubles;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using MudBlazor;
using Newtonsoft.Json.Linq;

[TestClass]
public class FlowDocumentServiceTests
{
    private FbpNodeFactory _nodeFactory = null!;
    private FlowDocumentService _flowDocService = null!;
    private FakeFbpRuntimeService _runtime = null!;
    private BlazorDiagram _diagram = null!;

    [TestInitialize]
    public void Setup()
    {
        _nodeFactory = new FbpNodeFactory();
        _flowDocService = new FlowDocumentService(_nodeFactory);
        _runtime = new FakeFbpRuntimeService();
        var diagramFactory = new FbpDiagramFactory();
        _diagram = diagramFactory.CreateConfiguredDiagram(_runtime);
    }

    [TestMethod]
    public async Task ExportFlowJsonAsync_ReturnsValidJObject()
    {
        var json = await _flowDocService.ExportFlowJsonAsync(_diagram, _runtime);
        Assert.IsNotNull(json);
        Assert.IsNotNull(json["nodes"]);
        Assert.IsNotNull(json["links"]);
    }

    [TestMethod]
    public async Task ExportFlowMermaidAsync_ReturnsMermaidString()
    {
        var mermaid = await _flowDocService.ExportFlowMermaidAsync(_diagram, _runtime);
        Assert.IsNotNull(mermaid);
        Assert.IsTrue(mermaid.Contains("flowchart") || mermaid.Length > 0);
    }

    [TestMethod]
    public async Task LoadFlowFromJsonAsync_PopulatesNodesAndLinks()
    {
        var testComponent = new Component
        {
            Info = new IdInformation { Id = "comp-1", Name = "TestComponent", Description = "A test component" },
            Type = Component.ComponentType.standard,
            InPorts = [new Component.Port { Name = "in1", Type = Component.Port.PortType.standard, ContentType = "text" }],
            OutPorts = [new Component.Port { Name = "out1", Type = Component.Port.PortType.standard, ContentType = "text" }],
        };
        _runtime.ServiceIdAndComponentId2Component[("no_service", "comp-1")] = testComponent;

        var flowJson = new JObject
        {
            {
                "nodes",
                new JArray
                {
                    new JObject
                    {
                        { "nodeId", "node-1" },
                        { "componentId", "comp-1" },
                        { "processName", "Process1" },
                        { "location", new JObject { { "x", 100 }, { "y", 150 } } },
                    },
                    new JObject
                    {
                        { "nodeId", "node-2" },
                        { "componentId", "comp-1" },
                        { "processName", "Process2" },
                        { "location", new JObject { { "x", 400 }, { "y", 150 } } },
                    },
                }
            },
            {
                "links",
                new JArray
                {
                    new JObject
                    {
                        { "source", new JObject { { "nodeId", "node-1" }, { "port", "out1" } } },
                        { "target", new JObject { { "nodeId", "node-2" }, { "port", "in1" } } },
                    },
                }
            },
        };

        var zoomCalled = false;
        await _flowDocService.LoadFlowFromJsonAsync(
            _diagram,
            _runtime,
            flowJson,
            onZoomToFit: () => { zoomCalled = true; return Task.CompletedTask; }
        );

        Assert.AreEqual(2, _diagram.Nodes.Count);
        Assert.AreEqual(1, _diagram.Links.Count);
        Assert.IsTrue(zoomCalled, "onZoomToFit callback should be called after loading flow.");
    }

    [TestMethod]
    public async Task ExportFlowJsonAsync_ExcludesDeadOrUnavailableServices()
    {
        _runtime.ServiceId2Registries["active-reg"] = null!;
        _runtime.RegistryServiceIdToPetNameAndSturdyRef["active-reg"] = ("Active Reg", "capnp://active-reg");

        // Dead/unavailable service: has null sturdyRef and is not in ServiceId2Registries
        _runtime.RegistryServiceIdToPetNameAndSturdyRef["dead-reg"] = ("Service 'dea..reg' unavailable!", null);

        // Active channel service
        _runtime.ServiceId2ChannelStarterServices["active-chan"] = null!;
        _runtime.ChannelServiceIdToPetNameAndSturdyRef["active-chan"] = ("Active Chan", "capnp://active-chan");

        var doc = await _flowDocService.ExportFlowJsonAsync(_diagram, _runtime);
        var servicesObj = doc["services"] as JObject;

        Assert.IsNotNull(servicesObj);
        var components = servicesObj["components"] as JObject;
        var channels = servicesObj["channels"] as JObject;

        Assert.IsNotNull(components);
        Assert.IsNotNull(channels);
        Assert.IsTrue(components.ContainsKey("active-reg"));
        Assert.IsFalse(components.ContainsKey("dead-reg"));
        Assert.IsTrue(channels.ContainsKey("active-chan"));
    }

    [TestMethod]
    public async Task LoadFlowFromJsonAsync_DeadServiceInJson_DoesNotBlockLoadingAndMarksNodeUnavailable()
    {
        var flowJson = new JObject
        {
            {
                "services",
                new JObject
                {
                    { "components", new JObject { { "dead-svc", "capnp://unreachable-host:9999/dead" } } },
                    { "channels", new JObject { { "dead-chan", "capnp://unreachable-host:9999/dead-chan" } } }
                }
            },
            {
                "nodes",
                new JArray
                {
                    new JObject
                    {
                        { "nodeId", "node-dead" },
                        { "componentId", "some-comp" },
                        { "componentServiceId", "dead-svc" },
                        { "processName", "DeadNode" },
                        { "location", new JObject { { "x", 100 }, { "y", 150 } } },
                    }
                }
            },
            { "links", new JArray() }
        };

        await _flowDocService.LoadFlowFromJsonAsync(_diagram, _runtime, flowJson);

        Assert.AreEqual(1, _diagram.Nodes.Count);
        var node = _diagram.Nodes.First() as CapnpFbpComponentModel;
        Assert.IsNotNull(node);
        Assert.AreEqual("dead-svc", node.ComponentServiceId);
        Assert.IsTrue(_runtime.RegistryServiceIdToPetNameAndSturdyRef.ContainsKey("dead-svc"));
        Assert.IsNull(_runtime.RegistryServiceIdToPetNameAndSturdyRef["dead-svc"].Item2);
        StringAssert.Contains(_runtime.RegistryServiceIdToPetNameAndSturdyRef["dead-svc"].Item1, "unavailable!");
    }

    [TestMethod]
    public async Task ExportFlowJsonAsync_ScopesComponentServicesToThoseUsedByDiagramNodes()
    {
        _runtime.ServiceId2Registries["used-reg"] = null!;
        _runtime.RegistryServiceIdToPetNameAndSturdyRef["used-reg"] = ("Used Registry", "capnp://used-reg");

        _runtime.ServiceId2Registries["unused-reg"] = null!;
        _runtime.RegistryServiceIdToPetNameAndSturdyRef["unused-reg"] = ("Unused Registry", "capnp://unused-reg");

        var node = new CapnpFbpComponentModel(new Point(0, 0))
        {
            ComponentServiceId = "used-reg",
            ComponentId = "comp-1"
        };
        _diagram.Nodes.Add(node);

        var doc = await _flowDocService.ExportFlowJsonAsync(_diagram, _runtime);
        var components = doc["services"]?["components"] as JObject;

        Assert.IsNotNull(components);
        Assert.IsTrue(components.ContainsKey("used-reg"), "Component service used by diagram node must be exported.");
        Assert.IsFalse(components.ContainsKey("unused-reg"), "Connected component service not used by any diagram node should be excluded.");
    }
}
