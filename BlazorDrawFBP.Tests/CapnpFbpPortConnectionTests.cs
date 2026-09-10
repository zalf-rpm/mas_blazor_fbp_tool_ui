namespace BlazorDrawFBP.Tests;

using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;

[TestClass]
public class CapnpFbpPortConnectionTests
{
    private BlazorDiagram _diagram = null!;
    private CapnpFbpRunnableComponentModel _sourceNode = null!;
    private CapnpFbpRunnableComponentModel _targetNode = null!;

    [TestInitialize]
    public void Setup()
    {
        _diagram = new BlazorDiagram();
        _sourceNode = new CapnpFbpRunnableComponentModel("src", new Point(0, 0));
        _targetNode = new CapnpFbpRunnableComponentModel("tgt", new Point(200, 0));
        _diagram.Nodes.Add(_sourceNode);
        _diagram.Nodes.Add(_targetNode);
    }

    [TestMethod]
    public void CanConnect_OutToIn_ReturnsTrue()
    {
        var outPort = new CapnpFbpOutPortModel(_sourceNode, PortAlignment.Right);
        var inPort = new CapnpFbpInPortModel(_targetNode, PortAlignment.Left);
        _sourceNode.AddPort(outPort);
        _targetNode.AddPort(inPort);

        Assert.IsTrue(CapnpFbpPortModel.CanConnect(outPort, inPort));
    }

    [TestMethod]
    public void CanConnect_InToOut_ReturnsTrue()
    {
        var inPort = new CapnpFbpInPortModel(_targetNode, PortAlignment.Left);
        var outPort = new CapnpFbpOutPortModel(_sourceNode, PortAlignment.Right);
        _sourceNode.AddPort(outPort);
        _targetNode.AddPort(inPort);

        Assert.IsTrue(CapnpFbpPortModel.CanConnect(inPort, outPort));
    }

    [TestMethod]
    public void CanConnect_InToIn_ReturnsFalse()
    {
        var inPort1 = new CapnpFbpInPortModel(_sourceNode, PortAlignment.Left);
        var inPort2 = new CapnpFbpInPortModel(_targetNode, PortAlignment.Left);
        _sourceNode.AddPort(inPort1);
        _targetNode.AddPort(inPort2);

        Assert.IsFalse(CapnpFbpPortModel.CanConnect(inPort1, inPort2));
    }

    [TestMethod]
    public void CanConnect_OutToOut_ReturnsFalse()
    {
        var outPort1 = new CapnpFbpOutPortModel(_sourceNode, PortAlignment.Right);
        var outPort2 = new CapnpFbpOutPortModel(_targetNode, PortAlignment.Right);
        _sourceNode.AddPort(outPort1);
        _targetNode.AddPort(outPort2);

        Assert.IsFalse(CapnpFbpPortModel.CanConnect(outPort1, outPort2));
    }

    [TestMethod]
    public void CanConnect_NonArrayOutPort_WhenAlreadyConnected_ReturnsFalse()
    {
        var outPort = new CapnpFbpOutPortModel(_sourceNode, PortAlignment.Right);
        var inPort1 = new CapnpFbpInPortModel(_targetNode, PortAlignment.Left);
        var inPort2 = new CapnpFbpInPortModel(_targetNode, PortAlignment.Bottom);
        _sourceNode.AddPort(outPort);
        _targetNode.AddPort(inPort1);
        _targetNode.AddPort(inPort2);

        var link = new RememberCapnpPortsLinkModel(outPort, inPort1);
        _diagram.Links.Add(link);

        Assert.IsFalse(outPort.CanAcceptMoreConnections);
        Assert.IsFalse(CapnpFbpPortModel.CanConnect(outPort, inPort2));
    }

    [TestMethod]
    public void CanConnect_ArrayOutPort_WhenAlreadyConnected_ReturnsTrueForDifferentTarget()
    {
        var arrayOutPort = new CapnpFbpOutPortModel(_sourceNode, PortAlignment.Right)
        {
            IsArrayPort = true
        };
        var inPort1 = new CapnpFbpInPortModel(_targetNode, PortAlignment.Left);
        var inPort2 = new CapnpFbpInPortModel(_targetNode, PortAlignment.Bottom);
        _sourceNode.AddPort(arrayOutPort);
        _targetNode.AddPort(inPort1);
        _targetNode.AddPort(inPort2);

        var link = new RememberCapnpPortsLinkModel(arrayOutPort, inPort1);
        _diagram.Links.Add(link);

        Assert.IsTrue(arrayOutPort.CanAcceptMoreConnections);
        Assert.IsTrue(CapnpFbpPortModel.CanConnect(arrayOutPort, inPort2));
    }

    [TestMethod]
    public void CanConnect_DuplicateLinkBetweenSamePorts_ReturnsFalse()
    {
        var arrayOutPort = new CapnpFbpOutPortModel(_sourceNode, PortAlignment.Right)
        {
            IsArrayPort = true
        };
        var inPort = new CapnpFbpInPortModel(_targetNode, PortAlignment.Left);
        _sourceNode.AddPort(arrayOutPort);
        _targetNode.AddPort(inPort);

        var link = new RememberCapnpPortsLinkModel(arrayOutPort, inPort);
        _diagram.Links.Add(link);

        // Same pair cannot have a duplicate link
        Assert.IsFalse(CapnpFbpPortModel.CanConnect(arrayOutPort, inPort));
    }

    [TestMethod]
    public void CanAttachTo_NonCapnpFbpPortModel_ReturnsFalse()
    {
        var outPort = new CapnpFbpOutPortModel(_sourceNode, PortAlignment.Right);
        var plainPort = new PortModel(_targetNode, PortAlignment.Left);

        Assert.IsFalse(outPort.CanAttachTo(plainPort));
    }
}
