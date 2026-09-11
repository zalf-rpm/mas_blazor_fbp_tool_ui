using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Services;
using BlazorDrawFBP.Tests.TestDoubles;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Newtonsoft.Json.Linq;

namespace BlazorDrawFBP.Tests;

[TestClass]
public class FbpDiagramFactoryTests
{
    [TestMethod]
    public void CreateConfiguredDiagram_RegistersWidgetsAndShortcuts()
    {
        var factory = new FbpDiagramFactory();
        var runtime = new FakeFbpRuntimeService();

        var diagram = factory.CreateConfiguredDiagram(runtime);

        Assert.IsNotNull(diagram);
        Assert.AreSame(diagram, runtime.Diagram);
        Assert.IsTrue(diagram.Options.AllowMultiSelection);
        Assert.IsTrue(diagram.Options.Zoom.Enabled);
        Assert.IsTrue(diagram.Options.Zoom.Inverse);
    }

    [TestMethod]
    public void CreateConfiguredDiagram_NotifiesOnStructureChanged()
    {
        var factory = new FbpDiagramFactory();
        var runtime = new FakeFbpRuntimeService();
        var structureChangedCount = 0;

        var diagram = factory.CreateConfiguredDiagram(runtime, () => structureChangedCount++);

        diagram.Nodes.Add(new NodeModel(new Point(10, 10)));
        Assert.AreEqual(1, structureChangedCount);
    }
}

[TestClass]
public class FbpNodeFactoryTests
{
    [TestMethod]
    public void CreateComponentFromJson_ParsesCorrectly()
    {
        var factory = new FbpNodeFactory();
        var json = new JObject
        {
            {
                "info",
                new JObject
                {
                    { "id", "c1" },
                    { "name", "Comp One" },
                    { "description", "Desc" },
                }
            },
            { "type", "standard" },
            {
                "inPorts",
                new JArray
                {
                    new JObject
                    {
                        { "name", "in1" },
                        { "type", "standard" },
                        { "contentType", "text/plain" },
                    },
                }
            },
            {
                "outPorts",
                new JArray
                {
                    new JObject
                    {
                        { "name", "out1" },
                        { "type", "standard" },
                        { "contentType", "text/plain" },
                    },
                }
            },
        };

        var comp = factory.CreateComponentFromJson(json);
        Assert.IsNotNull(comp);
        Assert.AreEqual("c1", comp.Info.Id);
        Assert.AreEqual("Comp One", comp.Info.Name);
        Assert.AreEqual("Desc", comp.Info.Description);
        Assert.AreEqual(1, comp.InPorts.Count);
        Assert.AreEqual(1, comp.OutPorts.Count);
    }

    [TestMethod]
    public void AddFbpNode_StandardComponent_CreatesRunnableNode()
    {
        var factory = new FbpNodeFactory();
        var runtime = new FakeFbpRuntimeService();
        var diagramFactory = new FbpDiagramFactory();
        var diagram = diagramFactory.CreateConfiguredDiagram(runtime);

        var comp = new Component
        {
            Info = new IdInformation { Id = "test-comp", Name = "Test" },
            Type = Component.ComponentType.standard,
            InPorts = [],
            OutPorts = [],
        };

        var node = factory.AddFbpNode(diagram, runtime, new Point(50, 50), comp);
        Assert.IsNotNull(node);
        Assert.IsInstanceOfType<CapnpFbpRunnableComponentModel>(node);
        Assert.AreEqual(1, diagram.Nodes.Count);
    }

    [TestMethod]
    public void AddFbpNode_IipComponent_CreatesIipNode()
    {
        var factory = new FbpNodeFactory();
        var runtime = new FakeFbpRuntimeService();
        var diagramFactory = new FbpDiagramFactory();
        var diagram = diagramFactory.CreateConfiguredDiagram(runtime);

        var comp = new Component
        {
            Info = new IdInformation { Id = "iip", Name = "IIP" },
            Type = Component.ComponentType.iip,
            InPorts = [],
            OutPorts = [],
        };

        var node = factory.AddFbpNode(
            diagram,
            runtime,
            new Point(50, 50),
            comp,
            new JObject { { "content", "42" } }
        );
        Assert.IsNotNull(node);
        Assert.IsInstanceOfType<CapnpFbpIipComponentModel>(node);
        Assert.AreEqual("42", ((CapnpFbpIipComponentModel)node).Content);
    }
}
