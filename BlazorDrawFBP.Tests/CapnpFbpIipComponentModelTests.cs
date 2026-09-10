namespace BlazorDrawFBP.Tests;

using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Tests.TestDoubles;

[TestClass]
public class CapnpFbpIipComponentModelTests
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
    public void IipComponentModel_InitialState_IsIdle()
    {
        var node = new CapnpFbpIipComponentModel(new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentId = "iip1",
            Content = "test content"
        };

        Assert.AreEqual(ComponentLifecycleState.Idle, node.LifecycleState);
        Assert.AreEqual(ComponentLifecycleState.Idle, node.DisplayLifecycleState);
        Assert.IsTrue(node.CanStart);
        Assert.IsFalse(node.CanStop);
        Assert.IsNull(node.LifecycleError);
    }

    [TestMethod]
    public async Task IipComponentModel_SendIip_WithoutChannelStarter_TransitionsToFailed()
    {
        _runtimeService.CurrentChannelStarterService = null;

        var node = new CapnpFbpIipComponentModel(new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentId = "iip1",
            Content = "test content"
        };

        await node.SendIip(_runtimeService.ConnectionManager);

        Assert.AreEqual(ComponentLifecycleState.Failed, node.LifecycleState);
        Assert.AreEqual(ComponentLifecycleState.Failed, node.DisplayLifecycleState);
        Assert.AreEqual("No channel service connected.", node.LifecycleError);
    }

    [TestMethod]
    public async Task IipComponentModel_ResetExecution_RestoresIdleState()
    {
        _runtimeService.CurrentChannelStarterService = null;

        var node = new CapnpFbpIipComponentModel(new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentId = "iip1",
            Content = "test content"
        };

        await node.SendIip(_runtimeService.ConnectionManager);
        Assert.AreEqual(ComponentLifecycleState.Failed, node.LifecycleState);

        await node.ResetExecution();
        Assert.AreEqual(ComponentLifecycleState.Idle, node.LifecycleState);
        Assert.IsNull(node.LifecycleError);
    }
}
