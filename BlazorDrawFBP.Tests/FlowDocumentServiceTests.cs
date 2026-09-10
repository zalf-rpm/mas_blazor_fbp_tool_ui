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
}
