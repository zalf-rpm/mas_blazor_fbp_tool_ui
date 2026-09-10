namespace BlazorDrawFBP.Tests;

using System.Linq;
using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Services;
using Mas.Infrastructure.Common;

[TestClass]
public class FbpRuntimeServiceTests
{
    private FbpRuntimeService _runtimeService = null!;
    private BlazorDiagram _diagram = null!;

    [TestInitialize]
    public void Setup()
    {
        _runtimeService = new FbpRuntimeService(new ConnectionManager());
        _diagram = new BlazorDiagram();
        _runtimeService.Diagram = _diagram;
    }

    [TestMethod]
    public void CanExecuteFlow_Initially_ReturnsFalseWithExpectedTitle()
    {
        Assert.IsFalse(_runtimeService.CanExecuteFlow);
        Assert.IsFalse(_runtimeService.HasConnectedComponentService);
        Assert.IsFalse(_runtimeService.HasConnectedChannelService);
        Assert.AreEqual(
            "Connect both a components service and a channel service to execute the flow.",
            _runtimeService.ExecuteFlowButtonTitle
        );
    }

    [TestMethod]
    public void GetComponentServicePalette_ReturnsConsistentStyles()
    {
        var style = _runtimeService.GetComponentServiceBadgeStyle("nonexistent");
        var handleStyle = _runtimeService.GetComponentServiceHandleStyle("nonexistent");

        Assert.IsFalse(string.IsNullOrWhiteSpace(style));
        Assert.IsFalse(string.IsNullOrWhiteSpace(handleStyle));
        StringAssert.Contains(style, "background-color:");
        StringAssert.Contains(handleStyle, "background-color:");
    }

    [TestMethod]
    public void InitDefaultComponents_LoadsComponentsCorrectly()
    {
        var sampleJson = """
        {
            "categories": [
                { "id": "test_cat", "name": "Test Category", "description": "Category Description" }
            ],
            "entries": [
                {
                    "categoryId": "test_cat",
                    "component": {
                        "info": { "id": "test_id", "name": "Test Name", "description": "Test Description" },
                        "type": "standard",
                        "inPorts": [{ "name": "in1", "type": "standard", "contentType": "text" }],
                        "outPorts": [{ "name": "out1", "type": "standard", "contentType": "text" }]
                    }
                }
            ]
        }
        """;

        _runtimeService.InitDefaultComponents(sampleJson);

        Assert.IsTrue(_runtimeService.CatId2CompServiceIdAndComponentIds.ContainsKey("test_cat"));
        Assert.IsTrue(_runtimeService.CatId2Info.ContainsKey("test_cat"));
        Assert.AreEqual("Test Category", _runtimeService.CatId2Info["test_cat"].Name);

        var key = (FbpRuntimeService.NoRegistryServiceId, "test_id");
        Assert.IsTrue(_runtimeService.ServiceIdAndComponentId2Component.ContainsKey(key));
        var component = _runtimeService.ServiceIdAndComponentId2Component[key];
        Assert.AreEqual("Test Name", component.Info.Name);
        Assert.AreEqual("Test Description", component.Info.Description);
        Assert.AreEqual(1, component.InPorts.Count);
        Assert.AreEqual(1, component.OutPorts.Count);
    }

    [TestMethod]
    public async Task ClearDiagramAsync_ClearsAllNodes()
    {
        var node = new CapnpFbpRunnableComponentModel("node1", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentName = "Worker"
        };
        _diagram.Nodes.Add(node);

        Assert.AreEqual(1, _diagram.Nodes.Count);

        await _runtimeService.ClearDiagramAsync();

        Assert.AreEqual(0, _diagram.Nodes.Count);
    }

    [TestMethod]
    public void GetFlowStartupOrder_SortsDownstreamBeforeUpstream()
    {
        // Source node -> Target node
        var sourceNode = new CapnpFbpRunnableComponentModel("source", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentName = "Source",
            ProcessName = "SourceProcess"
        };
        var targetNode = new CapnpFbpRunnableComponentModel("target", new Point(200, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentName = "Target",
            ProcessName = "TargetProcess"
        };

        var outPort = new CapnpFbpOutPortModel(sourceNode, PortAlignment.Right) { Name = "out" };
        sourceNode.AddPort(outPort);

        var inPort = new CapnpFbpInPortModel(targetNode, PortAlignment.Left) { Name = "in" };
        targetNode.AddPort(inPort);

        _diagram.Nodes.Add(sourceNode);
        _diagram.Nodes.Add(targetNode);

        var link = new RememberCapnpPortsLinkModel(outPort, inPort);
        _diagram.Links.Add(link);

        var order = _runtimeService.GetFlowStartupOrder();

        Assert.AreEqual(2, order.Count);
        // Target (downstream) should come before source (upstream)
        Assert.AreSame(targetNode, order[0]);
        Assert.AreSame(sourceNode, order[1]);
    }
}
