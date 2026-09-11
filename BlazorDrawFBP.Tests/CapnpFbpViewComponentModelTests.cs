using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Tests.TestDoubles;
using Mas.Infrastructure.Common;
using Microsoft.AspNetCore.Components;

namespace BlazorDrawFBP.Tests;

[TestClass]
public class CapnpFbpViewComponentModelTests
{
    private BlazorDiagram _diagram = null!;
    private FakeFbpRuntimeService _runtimeService = null!;

    [TestInitialize]
    public void Setup()
    {
        _diagram = new BlazorDiagram();
        _runtimeService = new FakeFbpRuntimeService { Diagram = _diagram };
    }

    [TestMethod]
    public void ViewComponentModel_InitialState_IsIdleAndCanStart()
    {
        var node = new CapnpFbpViewComponentModel("view1", new Point(10, 20))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentName = "TextViewer",
            ProcessName = "ViewerProcess",
        };

        Assert.AreEqual(ComponentLifecycleState.Idle, node.LifecycleState);
        Assert.AreEqual("Idle", node.LifecycleLabel);
        Assert.IsTrue(node.CanStart);
        Assert.IsFalse(node.CanStop);
        Assert.IsFalse(node.IsLifecycleBusy);
        Assert.IsNull(node.LifecycleError);
        Assert.IsTrue(node.AppendMode);
        Assert.AreEqual(100, node.DisplayWidthPx);
        Assert.AreEqual(132, node.DisplayHeightPx);
    }

    [TestMethod]
    public void ViewContent_InAppendMode_AppendsLinesWithBreak()
    {
        var node = new CapnpFbpViewComponentModel(new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            AppendMode = true,
        };

        node.ViewContent = new MarkupString("First line");
        Assert.AreEqual("First line", node.ViewContent.Value);

        node.ViewContent = new MarkupString("Second line");
        Assert.AreEqual("First line<br>Second line", node.ViewContent.Value);
    }

    [TestMethod]
    public void ViewContent_InOverwriteMode_ReplacesContent()
    {
        var node = new CapnpFbpViewComponentModel(new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            AppendMode = false,
        };

        node.ViewContent = new MarkupString("First line");
        Assert.AreEqual("First line", node.ViewContent.Value);

        node.ViewContent = new MarkupString("Replacement line");
        Assert.AreEqual("Replacement line", node.ViewContent.Value);
    }

    [TestMethod]
    public void ResetViewContent_ClearsContent()
    {
        var node = new CapnpFbpViewComponentModel(new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
        };

        node.ViewContent = new MarkupString("Data");
        Assert.IsFalse(string.IsNullOrEmpty(node.ViewContent.Value));

        node.ResetViewContent();
        Assert.IsTrue(string.IsNullOrEmpty(node.ViewContent.Value));
    }

    [TestMethod]
    public async Task StartProcess_WhenNoChannelServiceConnected_SetsLifecycleFault()
    {
        var node = new CapnpFbpViewComponentModel("view1", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ProcessName = "ViewerProcess",
        };
        _runtimeService.CurrentChannelStarterService = null;

        await node.StartProcess(new ConnectionManager());

        Assert.AreEqual(ComponentLifecycleState.Failed, node.LifecycleState);
        Assert.IsNotNull(node.LifecycleError);
        StringAssert.Contains(node.LifecycleError, "No channel service connected.");
    }
}
