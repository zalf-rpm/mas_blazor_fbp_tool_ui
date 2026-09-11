using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Services;
using BlazorDrawFBP.Tests.TestDoubles;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Tests;

[TestClass]
public class FbpRuntimeServiceTests
{
    private BlazorDiagram _diagram = null!;
    private FbpRuntimeService _runtimeService = null!;

    [TestInitialize]
    public void Setup()
    {
        _runtimeService = new FbpRuntimeService(new ConnectionManager(), TimeSpan.Zero);
        _diagram = new BlazorDiagram();
        _runtimeService.Diagram = _diagram;
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _runtimeService.DisposeAsync();
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
    public void CanExecuteFlow_DisabledUntilAtLeastOneComponentIsOnCanvas()
    {
        Assert.IsFalse(_runtimeService.CanExecuteFlow);
        Assert.IsFalse(_runtimeService.HasComponentsOnCanvas);

        _runtimeService.ServiceId2Registries["reg1"] = null!;
        _runtimeService.ServiceId2ChannelStarterServices["chan1"] = null!;
        Assert.IsTrue(_runtimeService.HasConnectedComponentService);
        Assert.IsTrue(_runtimeService.HasConnectedChannelService);

        Assert.IsFalse(_runtimeService.HasComponentsOnCanvas);
        Assert.IsFalse(_runtimeService.CanExecuteFlow);
        Assert.AreEqual(
            "Add at least one component to the canvas to execute the flow.",
            _runtimeService.ExecuteFlowButtonTitle
        );

        var componentNode = new CapnpFbpComponentModel(new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
        };
        _diagram.Nodes.Add(componentNode);

        Assert.IsTrue(_runtimeService.HasComponentsOnCanvas);
        Assert.IsTrue(_runtimeService.CanExecuteFlow);
        Assert.AreEqual("Execute entire flow", _runtimeService.ExecuteFlowButtonTitle);

        _diagram.Nodes.Remove(componentNode);

        Assert.IsFalse(_runtimeService.HasComponentsOnCanvas);
        Assert.IsFalse(_runtimeService.CanExecuteFlow);
        Assert.AreEqual(
            "Add at least one component to the canvas to execute the flow.",
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
            ComponentName = "Worker",
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
            ProcessName = "SourceProcess",
        };
        var targetNode = new CapnpFbpRunnableComponentModel("target", new Point(200, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentName = "Target",
            ProcessName = "TargetProcess",
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

    [TestMethod]
    public void GetFlowStartupOrder_ThreeLayerPipeline_SortsConsumersFirst()
    {
        var node1 = new CapnpFbpRunnableComponentModel("node1", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ProcessName = "Producer",
        };
        var node2 = new CapnpFbpRunnableComponentModel("node2", new Point(100, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ProcessName = "Transformer",
        };
        var node3 = new CapnpFbpRunnableComponentModel("node3", new Point(200, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ProcessName = "Consumer",
        };

        var out1 = new CapnpFbpOutPortModel(node1, PortAlignment.Right);
        var in2 = new CapnpFbpInPortModel(node2, PortAlignment.Left);
        var out2 = new CapnpFbpOutPortModel(node2, PortAlignment.Right);
        var in3 = new CapnpFbpInPortModel(node3, PortAlignment.Left);

        node1.AddPort(out1);
        node2.AddPort(in2);
        node2.AddPort(out2);
        node3.AddPort(in3);

        _diagram.Nodes.Add(node1);
        _diagram.Nodes.Add(node2);
        _diagram.Nodes.Add(node3);

        _diagram.Links.Add(new RememberCapnpPortsLinkModel(out1, in2));
        _diagram.Links.Add(new RememberCapnpPortsLinkModel(out2, in3));

        var order = _runtimeService.GetFlowStartupOrder();

        Assert.AreEqual(3, order.Count);
        Assert.AreSame(node3, order[0]); // Consumer
        Assert.AreSame(node2, order[1]); // Transformer
        Assert.AreSame(node1, order[2]); // Producer
    }

    [TestMethod]
    public void GetFlowStartupOrder_CyclicFlow_ReturnsAllNodesWithoutHanging()
    {
        var nodeA = new CapnpFbpRunnableComponentModel("nodeA", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ProcessName = "NodeA",
        };
        var nodeB = new CapnpFbpRunnableComponentModel("nodeB", new Point(100, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ProcessName = "NodeB",
        };

        var outA = new CapnpFbpOutPortModel(nodeA, PortAlignment.Right);
        var inB = new CapnpFbpInPortModel(nodeB, PortAlignment.Left);
        var outB = new CapnpFbpOutPortModel(nodeB, PortAlignment.Right);
        var inA = new CapnpFbpInPortModel(nodeA, PortAlignment.Left);

        nodeA.AddPort(outA);
        nodeA.AddPort(inA);
        nodeB.AddPort(outB);
        nodeB.AddPort(inB);

        _diagram.Nodes.Add(nodeA);
        _diagram.Nodes.Add(nodeB);

        _diagram.Links.Add(new RememberCapnpPortsLinkModel(outA, inB));
        _diagram.Links.Add(new RememberCapnpPortsLinkModel(outB, inA));

        var order = _runtimeService.GetFlowStartupOrder();

        Assert.AreEqual(2, order.Count);
        Assert.IsTrue(order.Contains(nodeA));
        Assert.IsTrue(order.Contains(nodeB));
    }

    [TestMethod]
    public void ServiceIdAndComponentId2Component_AllowsRepeatedRegistrationWithoutException()
    {
        var key = ("service-1", "comp-1");
        var component = new Component
        {
            Info = new IdInformation { Id = "comp-1", Name = "Component 1" },
        };

        _runtimeService.ServiceIdAndComponentId2Component[key] = component;
        // Re-adding / assigning same key must be idempotent and not throw ArgumentException
        _runtimeService.ServiceIdAndComponentId2Component[key] = component;

        Assert.IsTrue(_runtimeService.ServiceIdAndComponentId2Component.ContainsKey(key));
        Assert.AreEqual("comp-1", _runtimeService.ServiceIdAndComponentId2Component[key].Info.Id);
    }

    [TestMethod]
    public async Task CheckConnectedServicesHealthAsync_HealthyServices_Retained()
    {
        var fakeReg = new FakeRegistryService { ServiceId = "reg-1" };
        var fakeChan = new FakeStartChannelsService { ServiceId = "chan-1" };

        _runtimeService.ServiceId2Registries["reg-1"] = fakeReg;
        _runtimeService.RegistryServiceIdToPetNameAndSturdyRef["reg-1"] = (
            "Reg 1",
            "capnp://reg-1"
        );
        _runtimeService.ServiceId2ChannelStarterServices["chan-1"] = fakeChan;
        _runtimeService.ChannelServiceIdToPetNameAndSturdyRef["chan-1"] = (
            "Chan 1",
            "capnp://chan-1"
        );

        var pruned = await _runtimeService.CheckConnectedServicesHealthAsync();

        Assert.AreEqual(0, pruned);
        Assert.IsTrue(_runtimeService.ServiceId2Registries.ContainsKey("reg-1"));
        Assert.IsTrue(_runtimeService.ServiceId2ChannelStarterServices.ContainsKey("chan-1"));
        Assert.IsFalse(fakeReg.IsDisposed);
        Assert.IsFalse(fakeChan.IsDisposed);
    }

    [TestMethod]
    public async Task CheckConnectedServicesHealthAsync_UnresponsiveServices_PrunedAndCleanedUp()
    {
        var fakeReg = new FakeRegistryService { ServiceId = "dead-reg", ShouldFailPing = true };
        var fakeChan = new FakeStartChannelsService
        {
            ServiceId = "dead-chan",
            ShouldFailPing = true,
        };

        _runtimeService.ServiceId2Registries["dead-reg"] = fakeReg;
        _runtimeService.RegistryServiceIdToPetNameAndSturdyRef["dead-reg"] = (
            "Dead Reg",
            "capnp://dead-reg"
        );
        _runtimeService.ServiceId2ChannelStarterServices["dead-chan"] = fakeChan;
        _runtimeService.ChannelServiceIdToPetNameAndSturdyRef["dead-chan"] = (
            "Dead Chan",
            "capnp://dead-chan"
        );

        var node = new CapnpFbpRunnableComponentModel("node1", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentServiceId = "dead-reg",
        };
        _diagram.Nodes.Add(node);

        var pruned = await _runtimeService.CheckConnectedServicesHealthAsync();

        Assert.AreEqual(2, pruned);
        Assert.IsFalse(_runtimeService.ServiceId2Registries.ContainsKey("dead-reg"));
        Assert.IsFalse(_runtimeService.ServiceId2ChannelStarterServices.ContainsKey("dead-chan"));
        Assert.IsTrue(fakeReg.IsDisposed);
        Assert.IsTrue(fakeChan.IsDisposed);

        // Registry entry in RegistryServiceIdToPetNameAndSturdyRef should be marked unavailable because node is on canvas
        Assert.IsTrue(
            _runtimeService.RegistryServiceIdToPetNameAndSturdyRef.ContainsKey("dead-reg")
        );
        Assert.IsNull(_runtimeService.RegistryServiceIdToPetNameAndSturdyRef["dead-reg"].Item2);
        StringAssert.Contains(
            _runtimeService.RegistryServiceIdToPetNameAndSturdyRef["dead-reg"].Item1,
            "unavailable!"
        );

        // Channel service should be completely removed
        Assert.IsFalse(
            _runtimeService.ChannelServiceIdToPetNameAndSturdyRef.ContainsKey("dead-chan")
        );
    }

    [TestMethod]
    public void DisconnectRegistryServiceById_WithoutNodesOnCanvas_CompletelyRemovesService()
    {
        var fakeReg = new FakeRegistryService { ServiceId = "reg-orphan" };
        _runtimeService.ServiceId2Registries["reg-orphan"] = fakeReg;
        _runtimeService.RegistryServiceIdToPetNameAndSturdyRef["reg-orphan"] = (
            "Orphan",
            "capnp://orphan"
        );

        _runtimeService.DisconnectRegistryServiceById("reg-orphan");

        Assert.IsFalse(_runtimeService.ServiceId2Registries.ContainsKey("reg-orphan"));
        Assert.IsFalse(
            _runtimeService.RegistryServiceIdToPetNameAndSturdyRef.ContainsKey("reg-orphan")
        );
        Assert.IsTrue(fakeReg.IsDisposed);
    }

    [TestMethod]
    public async Task CheckConnectedServicesHealthAsync_FiresServiceConnectionDroppedEvent()
    {
        var droppedEvents = new List<ServiceConnectionDroppedEventArgs>();
        _runtimeService.ServiceConnectionDropped += args => droppedEvents.Add(args);

        var fakeReg = new FakeRegistryService { ServiceId = "dead-reg", ShouldFailPing = true };
        _runtimeService.ServiceId2Registries["dead-reg"] = fakeReg;
        _runtimeService.RegistryServiceIdToPetNameAndSturdyRef["dead-reg"] = (
            "My Registry",
            "capnp://dead-reg"
        );

        var fakeChan = new FakeStartChannelsService
        {
            ServiceId = "dead-chan",
            ShouldFailPing = true,
        };
        _runtimeService.ServiceId2ChannelStarterServices["dead-chan"] = fakeChan;
        _runtimeService.ChannelServiceIdToPetNameAndSturdyRef["dead-chan"] = (
            "My Channel Starter",
            "capnp://dead-chan"
        );

        await _runtimeService.CheckConnectedServicesHealthAsync();

        Assert.AreEqual(2, droppedEvents.Count);

        var chanEvent = droppedEvents.FirstOrDefault(e => e.ServiceId == "dead-chan");
        Assert.IsNotNull(chanEvent);
        Assert.AreEqual("Channel starter service", chanEvent.ServiceType);
        Assert.AreEqual("My Channel Starter", chanEvent.PetName);
        StringAssert.Contains(
            chanEvent.Message,
            "Connection dropped: Channel starter service 'My Channel Starter' is unreachable."
        );

        var regEvent = droppedEvents.FirstOrDefault(e => e.ServiceId == "dead-reg");
        Assert.IsNotNull(regEvent);
        Assert.AreEqual("Component registry service", regEvent.ServiceType);
        Assert.AreEqual("My Registry", regEvent.PetName);
        StringAssert.Contains(
            regEvent.Message,
            "Connection dropped: Component registry service 'My Registry' is unreachable."
        );
    }
}
