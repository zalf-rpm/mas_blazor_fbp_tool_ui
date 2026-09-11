using Blazor.Diagrams.Core.Geometry;
using BlazorDrawFBP.Models;
using Mas.Schema.Common;

namespace BlazorDrawFBP.Tests;

[TestClass]
public class SharedTests
{
    [TestMethod]
    public void MakeUniqueKey_WhenKeyDoesNotExist_ReturnsKeyUnchanged()
    {
        var dict = new Dictionary<string, int> { { "other_key", 1 } };
        var result = Shared.Shared.MakeUniqueKey(dict, "my_service");

        Assert.AreEqual("my_service", result);
    }

    [TestMethod]
    public void MakeUniqueKey_WhenKeyExists_AppendsNumber2()
    {
        var dict = new Dictionary<string, int> { { "my_service", 1 } };
        var result = Shared.Shared.MakeUniqueKey(dict, "my_service");

        Assert.AreEqual("my_service2", result);
    }

    [TestMethod]
    public void MakeUniqueKey_WhenMultipleCollisionsExist_IncrementsCounterUntilFree()
    {
        var dict = new Dictionary<string, int>
        {
            { "my_service", 1 },
            { "my_service2", 2 },
            { "my_service3", 3 },
        };
        var result = Shared.Shared.MakeUniqueKey(dict, "my_service");

        Assert.AreEqual("my_service4", result);
    }

    [TestMethod]
    public void FormatStructuredTextType_AllEnumValues_ReturnsExpectedDescription()
    {
        Assert.AreEqual(
            "as (structured) plain text",
            Shared.Shared.FormatStructuredTextType(StructuredText.Type.unstructured)
        );
        Assert.AreEqual(
            "as JSON",
            Shared.Shared.FormatStructuredTextType(StructuredText.Type.json)
        );
        Assert.AreEqual("as XML", Shared.Shared.FormatStructuredTextType(StructuredText.Type.xml));
        Assert.AreEqual(
            "as TOML",
            Shared.Shared.FormatStructuredTextType(StructuredText.Type.toml)
        );
        Assert.AreEqual(
            "as SturdyRef",
            Shared.Shared.FormatStructuredTextType(StructuredText.Type.sturdyRef)
        );
        Assert.AreEqual(
            "is unknown text type",
            Shared.Shared.FormatStructuredTextType((StructuredText.Type)999)
        );
    }

    [TestMethod]
    public void NodeNameFromPort_ReturnsProcessNameForComponentModel()
    {
        var node = new CapnpFbpRunnableComponentModel("node1", new Point(0, 0))
        {
            ProcessName = "FilterWorker",
        };
        var port = new CapnpFbpOutPortModel(node) { Name = "out" };

        var name = Shared.Shared.NodeNameFromPort(port);
        Assert.AreEqual("FilterWorker", name);
    }

    [TestMethod]
    public void NodeNameFromPort_ReturnsIdForIipComponentModel()
    {
        var node = new CapnpFbpIipComponentModel(new Point(0, 0));
        var port = new CapnpFbpOutPortModel(node) { Name = "IIP" };

        var name = Shared.Shared.NodeNameFromPort(port);
        Assert.AreEqual(node.Id, name);
    }

    [TestMethod]
    public void MakePortToolTipText_FormatsInAndOutPortsCorrectly()
    {
        var node = new CapnpFbpRunnableComponentModel("node1", new Point(0, 0))
        {
            ProcessName = "Worker",
        };
        var inPort = new CapnpFbpInPortModel(node)
        {
            Name = "input",
            ContentType = "text",
            Description = "Receives text data",
        };
        var outPort = new CapnpFbpOutPortModel(node)
        {
            Name = "output",
            ContentType = "common.capnp:StructuredText|image",
        };

        var inTooltip = Shared.Shared.MakePortToolTipText(inPort);
        var outTooltip = Shared.Shared.MakePortToolTipText(outPort);

        StringAssert.Contains(inTooltip.Value, "receives");
        StringAssert.Contains(inTooltip.Value, "input");
        StringAssert.Contains(inTooltip.Value, "text");
        StringAssert.Contains(inTooltip.Value, "Receives text data");

        StringAssert.Contains(outTooltip.Value, "sends");
        StringAssert.Contains(outTooltip.Value, "output");
        StringAssert.Contains(outTooltip.Value, "StructuredText");
        StringAssert.Contains(outTooltip.Value, "image");
    }
}
