using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Services;

namespace BlazorDrawFBP.Tests;

[TestClass]
public class NodeStackTests
{
    private BlazorDiagram _diagram = null!;
    private NodeStack _stack = null!;

    [TestInitialize]
    public void Setup()
    {
        _diagram = new BlazorDiagram();
        _stack = NodeStack.For(_diagram);
    }

    private NodeModel AddNode(NodeModel? node = null)
    {
        node ??= new NodeModel(new Point(0, 0));
        _diagram.Nodes.Add(node);
        return node;
    }

    [TestMethod]
    public void NewNodes_AppearInFront()
    {
        var first = AddNode();
        var second = AddNode();

        Assert.IsTrue(_stack.ZIndexOf(second) > _stack.ZIndexOf(first));
    }

    [TestMethod]
    public void Raise_BringsABackNodeAboveTheOthers_WithoutReorderingTheDiagram()
    {
        var back = AddNode();
        var front = AddNode();
        var libraryOrder = _diagram.Nodes.Select(n => n.Order).ToList();

        _stack.Raise(back);

        Assert.IsTrue(_stack.ZIndexOf(back) > _stack.ZIndexOf(front));
        CollectionAssert.AreEqual(libraryOrder, _diagram.Nodes.Select(n => n.Order).ToList());
    }

    [TestMethod]
    public void Raise_NodeAlreadyOnTop_DoesNothing()
    {
        AddNode();
        var top = AddNode();
        var changed = 0;
        _stack.Changed += () => changed++;
        var z = _stack.ZIndexOf(top);

        _stack.Raise(top);

        Assert.AreEqual(z, _stack.ZIndexOf(top));
        Assert.AreEqual(0, changed);
    }

    [TestMethod]
    public void Raise_TellsListenersToRenderTheCssAgain()
    {
        var back = AddNode();
        AddNode();
        var changed = 0;
        _stack.Changed += () => changed++;

        _stack.Raise(back);

        Assert.AreEqual(1, changed);
    }

    [TestMethod]
    public void Popups_StayAboveRegularNodes_EvenWhenARegularNodeIsRaisedLater()
    {
        var back = AddNode();
        AddNode();
        var popup = AddNode(new PortOptionsNode(new Point(0, 0)));
        var other = AddNode(new UpdatePortNameNode(new Point(0, 0)));

        _stack.Raise(back);

        Assert.IsTrue(_stack.ZIndexOf(popup) > _stack.ZIndexOf(back));
        Assert.IsTrue(_stack.ZIndexOf(other) > _stack.ZIndexOf(back));
    }

    [TestMethod]
    public void BuildCss_HasOneRulePerNode_AndForgetsRemovedNodes()
    {
        var first = AddNode();
        var second = AddNode();

        var css = _stack.BuildCss();
        StringAssert.Contains(css, $"[data-node-id=\"{first.Id}\"]{{z-index:{_stack.ZIndexOf(first)}}}");
        StringAssert.Contains(css, $"[data-node-id=\"{second.Id}\"]{{z-index:{_stack.ZIndexOf(second)}}}");

        _diagram.Nodes.Remove(first);

        Assert.IsFalse(_stack.BuildCss().Contains(first.Id));
    }

    [TestMethod]
    public void Raise_IgnoresNodesOfOtherDiagramsAndNull()
    {
        _stack.Raise(null);
        _stack.Raise(new NodeModel(new Point(0, 0)));
    }

    [TestMethod]
    public void For_ReturnsTheSameStackPerDiagram_AndCoversNodesAddedBeforeTheFirstCall()
    {
        var diagram = new BlazorDiagram();
        var early = new NodeModel(new Point(0, 0));
        diagram.Nodes.Add(early);

        var stack = NodeStack.For(diagram);

        Assert.AreSame(stack, NodeStack.For(diagram));
        Assert.IsTrue(stack.ZIndexOf(early) > 0);
    }
}
