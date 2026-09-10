namespace BlazorDrawFBP.Tests;

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BlazorDrawFBP.Pages;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json.Linq;

[TestClass]
public class EditorRouteAndFlowTests
{
    [TestMethod]
    public void EditorRoutes_ContainRootAndFlowIdRoutes()
    {
        var routes = typeof(Editor)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(r => r.Template)
            .ToHashSet();

        Assert.IsTrue(routes.Contains("/"), "Route '/' is missing.");
        Assert.IsTrue(routes.Contains("/editor"), "Route '/editor' is missing.");
        Assert.IsTrue(routes.Contains("/flow/{FlowId:guid}"), "Route '/flow/{FlowId:guid}' is missing.");
        Assert.IsTrue(routes.Contains("/editor/{FlowId:guid}"), "Route '/editor/{FlowId:guid}' is missing.");
    }

    [TestMethod]
    public void Editor_HasFlowIdParameterProperty()
    {
        var prop = typeof(Editor).GetProperty("FlowId", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(prop, "Property 'FlowId' must exist on Editor.");
        Assert.AreEqual(typeof(Guid?), prop.PropertyType, "Property 'FlowId' must be of type Guid?.");

        var paramAttr = prop.GetCustomAttribute<ParameterAttribute>();
        Assert.IsNotNull(paramAttr, "Property 'FlowId' must be decorated with [Parameter].");
    }

    [TestMethod]
    public void Editor_HasExtractedFlowLoadAndExportMethods()
    {
        var exportJsonMethod = typeof(Editor).GetMethod(
            "ExportFlowJsonAsync",
            BindingFlags.Public | BindingFlags.Instance,
            Type.EmptyTypes
        );
        Assert.IsNotNull(exportJsonMethod, "Public method 'ExportFlowJsonAsync()' must exist on Editor.");
        Assert.AreEqual(typeof(Task<JObject>), exportJsonMethod.ReturnType);

        var exportMermaidMethod = typeof(Editor).GetMethod(
            "ExportFlowMermaidAsync",
            BindingFlags.Public | BindingFlags.Instance,
            Type.EmptyTypes
        );
        Assert.IsNotNull(exportMermaidMethod, "Public method 'ExportFlowMermaidAsync()' must exist on Editor.");
        Assert.AreEqual(typeof(Task<string>), exportMermaidMethod.ReturnType);

        var loadJsonMethod = typeof(Editor).GetMethod(
            "LoadFlowFromJsonAsync",
            BindingFlags.Public | BindingFlags.Instance,
            new[] { typeof(JObject) }
        );
        Assert.IsNotNull(loadJsonMethod, "Public method 'LoadFlowFromJsonAsync(JObject)' must exist on Editor.");
        Assert.AreEqual(typeof(Task), loadJsonMethod.ReturnType);
    }

    [TestMethod]
    public void Editor_ShortFlowId_FormatsCorrectly()
    {
        var editor = new Editor();
        Assert.AreEqual("", editor.ShortFlowId);

#pragma warning disable BL0005
        editor.FlowId = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");
#pragma warning restore BL0005
        Assert.AreEqual("abcdef12", editor.ShortFlowId);
    }

    [TestMethod]
    public void Editor_HasPhase4SessionManagementMethods()
    {
        var createNewFlow = typeof(Editor).GetMethod(
            "CreateNewFlow",
            BindingFlags.Public | BindingFlags.Instance,
            Type.EmptyTypes
        );
        Assert.IsNotNull(createNewFlow, "Method 'CreateNewFlow()' must exist on Editor.");

        var copyUrl = typeof(Editor).GetMethod(
            "CopyFlowUrlToClipboard",
            BindingFlags.Public | BindingFlags.Instance,
            Type.EmptyTypes
        );
        Assert.IsNotNull(copyUrl, "Method 'CopyFlowUrlToClipboard()' must exist on Editor.");

        var terminateSession = typeof(Editor).GetMethod(
            "TerminateFlowSessionAsync",
            BindingFlags.Public | BindingFlags.Instance,
            Type.EmptyTypes
        );
        Assert.IsNotNull(terminateSession, "Method 'TerminateFlowSessionAsync()' must exist on Editor.");
        Assert.AreEqual(typeof(Task), terminateSession.ReturnType);
    }

    [TestMethod]
    public void Editor_CreateConfiguredDiagram_InitializesWithoutNullReference()
    {
        var editor = new Editor();
        var fakeRuntime = new BlazorDrawFBP.Tests.TestDoubles.FakeFbpRuntimeService();

        var method = typeof(Editor).GetMethod(
            "CreateConfiguredDiagram",
            BindingFlags.NonPublic | BindingFlags.Instance,
            new[] { typeof(BlazorDrawFBP.Services.IFbpRuntimeService) }
        );
        Assert.IsNotNull(method, "Method 'CreateConfiguredDiagram' must exist.");

        var diagram = method.Invoke(editor, new object[] { fakeRuntime }) as Blazor.Diagrams.BlazorDiagram;
        Assert.IsNotNull(diagram, "CreateConfiguredDiagram should return a BlazorDiagram instance.");
        Assert.IsNotNull(editor.Diagram, "Editor.Diagram property should be set after CreateConfiguredDiagram.");
        Assert.AreSame(diagram, fakeRuntime.Diagram, "RuntimeService.Diagram should match created diagram.");
    }
}

