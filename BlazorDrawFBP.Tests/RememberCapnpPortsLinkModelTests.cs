using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;

namespace BlazorDrawFBP.Tests;

[TestClass]
public class RememberCapnpPortsLinkModelTests
{
    private CapnpFbpInPortModel _inPort = null!;
    private CapnpFbpOutPortModel _outPort = null!;
    private CapnpFbpRunnableComponentModel _sourceNode = null!;
    private CapnpFbpRunnableComponentModel _targetNode = null!;

    [TestInitialize]
    public void Setup()
    {
        _sourceNode = new CapnpFbpRunnableComponentModel("src", new Point(0, 0));
        _targetNode = new CapnpFbpRunnableComponentModel("tgt", new Point(100, 0));
        _outPort = new CapnpFbpOutPortModel(_sourceNode, PortAlignment.Right);
        _inPort = new CapnpFbpInPortModel(_targetNode, PortAlignment.Left);
        _sourceNode.AddPort(_outPort);
        _targetNode.AddPort(_inPort);
    }

    [TestMethod]
    public void Constructor_InitializesPortsAndAnchors()
    {
        var link = new RememberCapnpPortsLinkModel(_outPort, _inPort);

        Assert.AreSame(_outPort, link.OutPortModel);
        Assert.AreSame(_inPort, link.InPortModel);
        Assert.IsInstanceOfType<SinglePortAnchor>(link.Source);
        Assert.IsInstanceOfType<SinglePortAnchor>(link.Target);

        var sourceAnchor = (SinglePortAnchor)link.Source;
        var targetAnchor = (SinglePortAnchor)link.Target;

        Assert.AreSame(_outPort, sourceAnchor.Port);
        Assert.AreSame(_inPort, targetAnchor.Port);
        Assert.IsTrue(sourceAnchor.MiddleIfNoMarker);
        Assert.IsTrue(targetAnchor.MiddleIfNoMarker);
    }

    [TestMethod]
    public void AttachAndDetachFromPorts_UpdatesPortLinkCollections()
    {
        var link = new RememberCapnpPortsLinkModel(_outPort, _inPort);

        link.AttachToPorts();
        Assert.IsTrue(_outPort.Links.Contains(link));
        Assert.IsTrue(_inPort.Links.Contains(link));

        link.DetachFromPorts();
        Assert.IsFalse(_outPort.Links.Contains(link));
        Assert.IsFalse(_inPort.Links.Contains(link));
    }

    [TestMethod]
    public void ClearProcessOutDisconnect_ClearsConnectionState()
    {
        var link = new RememberCapnpPortsLinkModel(_outPort, _inPort);
        link.ClearProcessOutDisconnect();

        Assert.IsFalse(link.ProcessOutConnected);
        Assert.IsNull(link.ProcessOutDisconnect);
    }
}
