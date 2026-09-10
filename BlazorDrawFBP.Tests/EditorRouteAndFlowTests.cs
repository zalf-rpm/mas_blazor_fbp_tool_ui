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
}
