namespace BlazorDrawFBP.Tests;

using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Tests.TestDoubles;

[TestClass]
public class CapnpFbpComponentModelTests
{
    private FakeFbpRuntimeService _runtimeService = null!;
    private BlazorDiagram _diagram = null!;

    [TestInitialize]
    public void Setup()
    {
        _diagram = new BlazorDiagram();
        _runtimeService = new FakeFbpRuntimeService { Diagram = _diagram };
    }

    [TestMethod]
    public void RunnableComponentModel_InitialState_IsIdleAndCanStart()
    {
        var node = new CapnpFbpRunnableComponentModel("node1", new Point(10, 20))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentName = "TestComp",
            ProcessName = "Process 1",
            ComponentServiceId = "svc1"
        };

        Assert.AreEqual(ComponentLifecycleState.Idle, node.LifecycleState);
        Assert.AreEqual(ComponentLifecycleState.Idle, node.DisplayLifecycleState);
        Assert.AreEqual("Idle", node.LifecycleLabel);
        Assert.IsTrue(node.CanStart);
        Assert.IsFalse(node.CanStop);
        Assert.IsFalse(node.IsLifecycleBusy);
        Assert.IsNull(node.LifecycleError);
        Assert.AreEqual(1, node.InParallelCount);
        Assert.IsFalse(node.HasProcChildren);
    }

    [TestMethod]
    public async Task RunnableComponentModel_AdjustProcCountAsync_WithoutArrayInput_OnlyUpdatesCount()
    {
        var node = new CapnpFbpRunnableComponentModel("standalone_node", new Point(10, 20))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentName = "Worker",
            ProcessName = "WorkerProcess"
        };

        _diagram.Nodes.Add(node);

        await node.AdjustProcCountAsync(2);
        Assert.AreEqual(3, node.InParallelCount);
        // Not eligible for multiplication without incoming array port links
        Assert.IsFalse(node.HasProcChildren);

        await node.AdjustProcCountAsync(-2);
        Assert.AreEqual(1, node.InParallelCount);
    }

    [TestMethod]
    public async Task RunnableComponentModel_AdjustProcCountAsync_WithArrayInput_SpawnsAndRemovesChildren()
    {
        var sourceNode = new CapnpFbpRunnableComponentModel("source", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram
        };
        var sourceOutPort = new CapnpFbpOutPortModel(sourceNode, PortAlignment.Right)
        {
            Name = "OUT",
            IsArrayPort = true
        };
        sourceNode.AddPort(sourceOutPort);

        var targetNode = new CapnpFbpRunnableComponentModel("target", new Point(100, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram
        };
        var targetInPort = new CapnpFbpInPortModel(targetNode, PortAlignment.Left)
        {
            Name = "IN"
        };
        targetNode.AddPort(targetInPort);

        var link = new RememberCapnpPortsLinkModel(sourceOutPort, targetInPort);

        _diagram.Nodes.Add(sourceNode);
        _diagram.Nodes.Add(targetNode);
        _diagram.Links.Add(link);

        // Increase to 3 instances -> spawns 2 child contexts
        await targetNode.AdjustProcCountAsync(2);
        Assert.AreEqual(3, targetNode.InParallelCount);
        Assert.IsTrue(targetNode.HasProcChildren);
        Assert.AreEqual(2, targetNode.ProcChildComponents.Count);

        foreach (var child in targetNode.ProcChildComponents)
        {
            Assert.AreSame(_runtimeService, child.RuntimeService);
            Assert.AreSame(_diagram, child.Diagram);
            Assert.IsTrue(child.IsInternalProcChild);
            Assert.AreSame(targetNode, child.ProcOwnerNode);
        }

        // Decrease back to 1 instance -> tears down child contexts
        await targetNode.AdjustProcCountAsync(-2);
        Assert.AreEqual(1, targetNode.InParallelCount);
        Assert.IsFalse(targetNode.HasProcChildren);
        Assert.AreEqual(0, targetNode.ProcChildComponents.Count);
    }

    [TestMethod]
    public async Task ComponentModel_SwitchService_DelegatesToRuntimeService()
    {
        var node = new CapnpFbpRunnableComponentModel("node1", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentServiceId = "old_service"
        };

        await _runtimeService.SwitchComponentServiceAsync(node, "new_service");

        Assert.AreEqual("new_service", node.ComponentServiceId);
        Assert.AreEqual(1, _runtimeService.SwitchServiceCalls.Count);
        Assert.AreEqual("new_service", _runtimeService.SwitchServiceCalls[0].ServiceId);
    }

    [TestMethod]
    public void PortCreation_AttachesCorrectParentAndAlignment()
    {
        var node = new CapnpFbpRunnableComponentModel("test_node", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram
        };

        var inPort = new CapnpFbpInPortModel(node, PortAlignment.Left) { Name = "IN" };
        var outPort = new CapnpFbpOutPortModel(node, PortAlignment.Right) { Name = "OUT" };

        Assert.AreSame(node, inPort.Parent);
        Assert.AreSame(node, outPort.Parent);
        Assert.AreEqual(PortAlignment.Left, inPort.Alignment);
        Assert.AreEqual(PortAlignment.Right, outPort.Alignment);
        Assert.AreEqual(CapnpFbpPortModel.PortType.In, inPort.ThePortType);
        Assert.AreEqual(CapnpFbpPortModel.PortType.Out, outPort.ThePortType);
    }
}
