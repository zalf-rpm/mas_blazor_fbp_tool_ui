namespace BlazorDrawFBP.Tests;

using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Tests.TestDoubles;

[TestClass]
public class CapnpFbpPortLayoutTests
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
    public void Calculate_WhenNodeHasNoPorts_ReturnsEmptyDictionary()
    {
        var node = new CapnpFbpRunnableComponentModel("node1", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram
        };

        var placements = CapnpFbpPortLayout.Calculate(node);
        Assert.AreEqual(0, placements.Count);
    }

    [TestMethod]
    public void Calculate_WhenNodeHasInAndOutPorts_ComputesPlacementsForBoth()
    {
        var node = new CapnpFbpRunnableComponentModel("node1", new Point(0, 0))
        {
            RuntimeService = _runtimeService,
            Diagram = _diagram,
            ComponentServiceId = "svc1",
            Size = new Size(200, 150)
        };

        var inPort = new CapnpFbpInPortModel(node, PortAlignment.Left) { Name = "IN" };
        var outPort = new CapnpFbpOutPortModel(node, PortAlignment.Right) { Name = "OUT" };
        node.AddPort(inPort);
        node.AddPort(outPort);

        var placements = CapnpFbpPortLayout.Calculate(node);

        Assert.AreEqual(2, placements.Count);
        Assert.IsTrue(placements.ContainsKey(inPort.Id));
        Assert.IsTrue(placements.ContainsKey(outPort.Id));

        var inPlacement = placements[inPort.Id];
        var outPlacement = placements[outPort.Id];

        Assert.AreEqual(PortAlignment.Left, inPlacement.Alignment);
        Assert.AreEqual(PortAlignment.Right, outPlacement.Alignment);

        var inStyle = inPlacement.ToStyle();
        var outStyle = outPlacement.ToStyle();

        Assert.IsFalse(string.IsNullOrEmpty(inStyle));
        Assert.IsFalse(string.IsNullOrEmpty(outStyle));
        StringAssert.StartsWith(inStyle, "top: ");
        StringAssert.StartsWith(outStyle, "top: ");
    }
}
