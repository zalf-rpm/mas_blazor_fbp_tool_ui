using System.Reflection;
using Blazor.Diagrams;
using BlazorDrawFBP.Components.Editor;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Pages;
using BlazorDrawFBP.Services;
using BlazorDrawFBP.Tests.TestDoubles;
using Mas.Infrastructure.BlazorComponents;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json.Linq;

namespace BlazorDrawFBP.Tests;

[TestClass]
public class EditorRouteAndFlowTests
{
    [TestMethod]
    public void EditorRoutes_ContainRootAndFlowIdRoutes()
    {
        var routes = typeof(Editor)
            .GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>()
            .Select(r => r.Template)
            .ToHashSet();

        Assert.IsTrue(routes.Contains("/"), "Route '/' is missing.");
        Assert.IsTrue(routes.Contains("/editor"), "Route '/editor' is missing.");
        Assert.IsTrue(
            routes.Contains("/flow/{FlowId:guid}"),
            "Route '/flow/{FlowId:guid}' is missing."
        );
        Assert.IsTrue(
            routes.Contains("/editor/{FlowId:guid}"),
            "Route '/editor/{FlowId:guid}' is missing."
        );
    }

    [TestMethod]
    public void Editor_HasFlowIdParameterProperty()
    {
        var prop = typeof(Editor).GetProperty(
            "FlowId",
            BindingFlags.Public | BindingFlags.Instance
        );
        Assert.IsNotNull(prop, "Property 'FlowId' must exist on Editor.");
        Assert.AreEqual(
            typeof(Guid?),
            prop.PropertyType,
            "Property 'FlowId' must be of type Guid?."
        );

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
        Assert.IsNotNull(
            exportJsonMethod,
            "Public method 'ExportFlowJsonAsync()' must exist on Editor."
        );
        Assert.AreEqual(typeof(Task<JObject>), exportJsonMethod.ReturnType);

        var exportMermaidMethod = typeof(Editor).GetMethod(
            "ExportFlowMermaidAsync",
            BindingFlags.Public | BindingFlags.Instance,
            Type.EmptyTypes
        );
        Assert.IsNotNull(
            exportMermaidMethod,
            "Public method 'ExportFlowMermaidAsync()' must exist on Editor."
        );
        Assert.AreEqual(typeof(Task<string>), exportMermaidMethod.ReturnType);

        var loadJsonMethod = typeof(Editor).GetMethod(
            "LoadFlowFromJsonAsync",
            BindingFlags.Public | BindingFlags.Instance,
            new[] { typeof(JObject) }
        );
        Assert.IsNotNull(
            loadJsonMethod,
            "Public method 'LoadFlowFromJsonAsync(JObject)' must exist on Editor."
        );
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
        Assert.IsNotNull(
            terminateSession,
            "Method 'TerminateFlowSessionAsync()' must exist on Editor."
        );
        Assert.AreEqual(typeof(Task), terminateSession.ReturnType);

        var cycleTtl = typeof(Editor).GetMethod(
            "CycleFlowTtl",
            BindingFlags.Public | BindingFlags.Instance,
            Type.EmptyTypes
        );
        Assert.IsNotNull(cycleTtl, "Method 'CycleFlowTtl()' must exist on Editor.");

        var ttlProp = typeof(Editor).GetProperty(
            "CurrentTtlLabel",
            BindingFlags.Public | BindingFlags.Instance
        );
        Assert.IsNotNull(ttlProp, "Property 'CurrentTtlLabel' must exist on Editor.");
        Assert.AreEqual(typeof(string), ttlProp.PropertyType);
    }

    [TestMethod]
    public void Editor_CreateConfiguredDiagram_InitializesWithoutNullReference()
    {
        var editor = new Editor();
        var fakeRuntime = new FakeFbpRuntimeService();

        var method = typeof(Editor).GetMethod(
            "CreateConfiguredDiagram",
            BindingFlags.NonPublic | BindingFlags.Instance,
            new[] { typeof(IFbpRuntimeService) }
        );
        Assert.IsNotNull(method, "Method 'CreateConfiguredDiagram' must exist.");

        var diagram = method.Invoke(editor, new object[] { fakeRuntime }) as BlazorDiagram;
        Assert.IsNotNull(
            diagram,
            "CreateConfiguredDiagram should return a BlazorDiagram instance."
        );
        Assert.IsNotNull(
            editor.Diagram,
            "Editor.Diagram property should be set after CreateConfiguredDiagram."
        );
        Assert.AreSame(
            diagram,
            fakeRuntime.Diagram,
            "RuntimeService.Diagram should match created diagram."
        );
    }

    [TestMethod]
    public async Task Editor_MergeFlowServicesIntoLocalStorage_ImportsAliveAndSkipsDeadOrDuplicate()
    {
        var editor = new Editor();
        var fakeStorage = new FakeLocalStorageService();
        var fakeRuntime = new FakeFbpRuntimeService();

        typeof(Editor)
            .GetProperty("LocalStorage", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(editor, fakeStorage);
        typeof(Editor)
            .GetProperty("InjectedRuntimeService", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(editor, fakeRuntime);

        // Pre-populate storage with an existing bookmark for an alive registry
        var existingBookmarks = new List<StoredSrData>
        {
            new()
            {
                InterfaceId = Shared.Shared.RegistryInterfaceId,
                PetName = "My Custom Existing PetName",
                SturdyRef = "capnp://alive-reg",
                AutoConnect = false,
            },
        };
        await fakeStorage.SetItemAsync(StoredSrData.StorageKey, existingBookmarks);

        // Configure runtime services:
        // 1. "alive-reg" is alive & connected
        fakeRuntime.ServiceId2Registries["alive-reg"] = null!;
        fakeRuntime.RegistryServiceIdToPetNameAndSturdyRef["alive-reg"] = (
            "Alive Registry",
            "capnp://alive-reg"
        );

        // 2. "dead-reg" is NOT in ServiceId2Registries (dead)
        fakeRuntime.RegistryServiceIdToPetNameAndSturdyRef["dead-reg"] = (
            "Service 'dead..reg' unavailable!",
            null
        );

        // 3. "alive-chan" is alive & connected
        fakeRuntime.ServiceId2ChannelStarterServices["alive-chan"] = null!;
        fakeRuntime.ChannelServiceIdToPetNameAndSturdyRef["alive-chan"] = (
            "Alive Channel Starter",
            "capnp://alive-chan"
        );

        // Flow JSON contains all three services
        var flowDoc = new JObject
        {
            {
                "services",
                new JObject
                {
                    {
                        "components",
                        new JObject
                        {
                            { "alive-reg", "capnp://alive-reg" },
                            { "dead-reg", "capnp://dead-reg" },
                        }
                    },
                    {
                        "channels",
                        new JObject { { "alive-chan", "capnp://alive-chan" } }
                    },
                }
            },
        };

        var addedCount = await editor.MergeFlowServicesIntoLocalStorageAsync(flowDoc);

        Assert.AreEqual(1, addedCount, "Only alive-chan should be newly imported.");

        var savedBookmarks = await StoredSrData.GetAllData(fakeStorage);
        Assert.AreEqual(
            2,
            savedBookmarks.Count,
            "Should contain existing alive-reg plus newly imported alive-chan."
        );

        // Existing bookmark should have preserved its custom PetName and settings
        var existing = savedBookmarks.FirstOrDefault(b => b.SturdyRef == "capnp://alive-reg");
        Assert.IsNotNull(existing);
        Assert.AreEqual("My Custom Existing PetName", existing.PetName);
        Assert.IsFalse(existing.AutoConnect);

        // Newly imported bookmark should have AutoConnect = true
        var importedChan = savedBookmarks.FirstOrDefault(b => b.SturdyRef == "capnp://alive-chan");
        Assert.IsNotNull(importedChan);
        Assert.AreEqual(Shared.Shared.ChannelStarterInterfaceId, importedChan.InterfaceId);
        Assert.AreEqual("Alive Channel Starter", importedChan.PetName);
        Assert.IsTrue(importedChan.AutoConnect);

        // Dead service should not have been added
        Assert.IsFalse(savedBookmarks.Any(b => b.SturdyRef == "capnp://dead-reg"));
    }

    [TestMethod]
    public void Editor_HasActiveExecution_ReflectsRuntimeAndNodeStates()
    {
        var editor = new Editor();
        var fakeRuntime = new FakeFbpRuntimeService();
        typeof(Editor)
            .GetProperty("InjectedRuntimeService", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(editor, fakeRuntime);

        // Initially no active execution
        Assert.IsFalse(editor.HasActiveExecution);

        // Simulate busy lifecycle nodes
        fakeRuntime.HasBusyLifecycleNodes = true;
        Assert.IsTrue(editor.HasActiveExecution);
        fakeRuntime.HasBusyLifecycleNodes = false;

        // Simulate executing flow
        fakeRuntime.IsExecutingFlow = true;
        Assert.IsTrue(editor.HasActiveExecution);
        fakeRuntime.IsExecutingFlow = false;

        Assert.IsFalse(editor.HasActiveExecution);
    }

    [TestMethod]
    public void Components_HaveEditorRequiredAttributes_OnEssentialParameters()
    {
        // 1. FlowCanvasControls
        var canvasProps = typeof(FlowCanvasControls).GetProperties();
        var canvasExecuteFlow = canvasProps.First(p => p.Name == "OnExecuteFlow");
        Assert.IsNotNull(canvasExecuteFlow.GetCustomAttribute<EditorRequiredAttribute>());

        // 2. FlowToolbar
        var toolbarProps = typeof(FlowToolbar).GetProperties();
        var toolbarNewFlow = toolbarProps.First(p => p.Name == "OnCreateNewFlow");
        Assert.IsNotNull(toolbarNewFlow.GetCustomAttribute<EditorRequiredAttribute>());

        // 3. CapnpFbpComponentWidget
        var widgetProps = typeof(CapnpFbpComponentWidget).GetProperties();
        var nodeProp = widgetProps.First(p => p.Name == "Node");
        Assert.IsNotNull(nodeProp.GetCustomAttribute<EditorRequiredAttribute>());

        // 4. ComponentPalette
        var paletteProps = typeof(ComponentPalette).GetProperties();
        var dragEndProp = paletteProps.First(p => p.Name == "OnComponentDragEnd");
        Assert.IsNotNull(dragEndProp.GetCustomAttribute<EditorRequiredAttribute>());
    }
}
