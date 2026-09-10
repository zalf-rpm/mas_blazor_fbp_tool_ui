namespace BlazorDrawFBP.Pages;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Extensions;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Services;
using Mas.Infrastructure.BlazorComponents;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Registry;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using Newtonsoft.Json.Linq;
using Proxy = Capnp.Rpc.Proxy;

public partial class Editor : IAsyncDisposable
{
    private const string LoadFlowInputId = "load-flow-input";
    private const double ZoomToFitMargin = 80;

    [Parameter]
    public Guid? FlowId { get; set; }

    public FlowSession? CurrentSession { get; private set; }
    public IFbpRuntimeService RuntimeService => CurrentSession?.RuntimeService ?? InjectedRuntimeService;
    public ConnectionManager ConMan => CurrentSession?.RuntimeService.ConnectionManager ?? InjectedConMan;
    public BlazorDiagram Diagram { get; set; } = null!;

    public string ShortFlowId => FlowId.HasValue ? FlowId.Value.ToString("N")[..8] : "";

    public Dictionary<ulong, Type> InterfaceIdToType => RuntimeService.InterfaceIdToType;
    public Dictionary<string, Proxy> SturdyRef2Services => RuntimeService.SturdyRef2Services;

    private CancellationTokenSource? _snapshotDebounceCts;
    private Component? _draggedComponent;
    private string _draggedComponentServiceId = "";
    private bool _loadingFlow;

    public void ScheduleSessionSnapshot()
    {
        if (CurrentSession == null || _loadingFlow)
            return;

        _snapshotDebounceCts?.Cancel();
        _snapshotDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _snapshotDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cts.Token);
                if (cts.Token.IsCancellationRequested)
                    return;

                var doc = await ExportFlowJsonAsync();
                if (CurrentSession != null)
                {
                    CurrentSession.FlowDocument = doc;
                    CurrentSession.Touch();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"Session snapshot failed: {ex.Message}");
            }
        });
    }

    public void CreateNewFlow()
    {
        var newId = Guid.NewGuid();
        NavigationManager.NavigateTo($"/flow/{newId}");
    }

    public async Task CopyFlowUrlToClipboard()
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", NavigationManager.Uri);
            Snackbar.Add("Flow URL copied to clipboard!", Severity.Info);
        }
        catch (Exception)
        {
            Snackbar.Add($"Flow URL: {NavigationManager.Uri}", Severity.Info);
        }
    }

    public async Task TerminateFlowSessionAsync()
    {
        if (!FlowId.HasValue)
            return;

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Terminate Flow Session?",
            "Are you sure you want to terminate this flow session? All running processes and channels will be stopped immediately and the session will be purged.",
            yesText: "Terminate",
            cancelText: "Cancel"
        );

        if (confirmed != true)
            return;

        var targetFlowId = FlowId.Value;
        if (SessionStore.RemoveSession(targetFlowId, out var session))
        {
            if (session.RuntimeService is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
            else
            {
                await session.RuntimeService.ClearDiagramAsync();
            }
        }
        else
        {
            await RuntimeService.ClearDiagramAsync();
        }

        Snackbar.Add("Flow session terminated.", Severity.Warning);
        var newId = Guid.NewGuid();
        NavigationManager.NavigateTo($"/flow/{newId}");
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!FlowId.HasValue)
        {
            FlowId = Guid.NewGuid();
            NavigationManager.NavigateTo($"/flow/{FlowId.Value}", replace: true);
            return;
        }

        if (CurrentSession != null && CurrentSession.Id == FlowId.Value)
        {
            return;
        }

        if (CurrentSession != null)
        {
            CurrentSession.RuntimeService.StateChanged -= OnRuntimeStateChanged;
            SessionStore.MarkDetached(CurrentSession.Id);
        }

        CurrentSession = SessionStore.GetOrCreateSession(FlowId.Value);
        SessionStore.MarkAttached(FlowId.Value);
        CleanupService.RegisterCleanup(() =>
        {
            if (FlowId.HasValue)
            {
                SessionStore.MarkDetached(FlowId.Value);
            }
            return Task.CompletedTask;
        });
        CurrentSession.RuntimeService.StateChanged += OnRuntimeStateChanged;

        if (CurrentSession.Diagram == null)
        {
            CurrentSession.Diagram = CreateConfiguredDiagram(CurrentSession.RuntimeService);
            Diagram = CurrentSession.Diagram;
            if (CurrentSession.FlowDocument != null)
            {
                await LoadFlowFromJsonAsync(CurrentSession.FlowDocument);
            }
        }
        else
        {
            Diagram = CurrentSession.Diagram;
            CurrentSession.RuntimeService.Diagram = Diagram;
        }

        SafeStateHasChanged();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;
        if (!await LocalStorage.ContainKeyAsync("sturdy-ref-store"))
            return;
        var allBookmarks = await StoredSrData.GetAllData(LocalStorage);
        allBookmarks.Sort();

        foreach (var ssrd in allBookmarks.Where(ssrd => ssrd.AutoConnect))
        {
            if (ssrd.InterfaceId == BlazorDrawFBP.Shared.Shared.ChannelStarterInterfaceId)
                await ConnectToStartChannelsService(ConMan, ssrd.PetName, ssrd.SturdyRef);
            else if (ssrd.InterfaceId == BlazorDrawFBP.Shared.Shared.RegistryInterfaceId)
                await ConnectToRegistryService(ConMan, ssrd.PetName, ssrd.SturdyRef);
        }

        SafeStateHasChanged();
    }

    private IFbpNodeFactory EffectiveNodeFactory => NodeFactory ?? new FbpNodeFactory();
    private IFbpDiagramFactory EffectiveDiagramFactory => DiagramFactory ?? new FbpDiagramFactory();
    private IFlowDocumentService EffectiveFlowDocumentService =>
        FlowDocumentService ?? new FlowDocumentService(EffectiveNodeFactory);

    private BlazorDiagram CreateConfiguredDiagram(IFbpRuntimeService runtime)
    {
        var diagram = EffectiveDiagramFactory.CreateConfiguredDiagram(
            runtime,
            onStructureChanged: ScheduleSessionSnapshot,
            onDiagramInteracted: SafeStateHasChanged,
            onZoomToFit: ZoomToFitFlowAsync
        );
        Diagram = diagram;
        return diagram;
    }

    public async Task<JObject> ExportFlowJsonAsync()
    {
        if (Diagram == null)
            return new JObject();
        return await EffectiveFlowDocumentService.ExportFlowJsonAsync(Diagram, RuntimeService);
    }

    public async Task<string> ExportFlowMermaidAsync()
    {
        if (Diagram == null)
            return string.Empty;
        return await EffectiveFlowDocumentService.ExportFlowMermaidAsync(Diagram, RuntimeService);
    }

    public async Task<(JObject? Json, string? Mermaid)> ExportFlowDocumentAsync(bool asMermaid)
    {
        if (Diagram == null)
            return (new JObject(), string.Empty);
        return await EffectiveFlowDocumentService.ExportFlowDocumentAsync(Diagram, RuntimeService, asMermaid);
    }

    public async Task LoadFlowFromJsonAsync(JObject dia)
    {
        if (Diagram == null)
            return;

        _loadingFlow = true;
        try
        {
            await EffectiveFlowDocumentService.LoadFlowFromJsonAsync(
                Diagram,
                RuntimeService,
                dia,
                ZoomToFitFlowAsync,
                RegisterNodeLayoutEvents
            );
        }
        finally
        {
            _loadingFlow = false;
        }
    }

    protected async Task LoadFlow(IBrowserFile file)
    {
        var s = file.OpenReadStream();
        if (s.Length > 1 * 1024 * 1024)
            return; // 1 MB
        var dia = JObject.Parse(await new StreamReader(s).ReadToEndAsync());
        await LoadFlowFromJsonAsync(dia);
    }

    protected async Task LoadFlowSelected(InputFileChangeEventArgs args)
    {
        try
        {
            if (args.FileCount > 0)
            {
                await LoadFlow(args.File);
            }
        }
        finally
        {
            _loadFlowInputVersion++;
        }
    }

    protected async Task SaveFlow(bool asMermaid)
    {
        var (dia, mermaid) = await ExportFlowDocumentAsync(asMermaid);
        var content = asMermaid ? (mermaid ?? "") : (dia?.ToString() ?? "{}");
        var ext = asMermaid ? "mmd" : "json";
        await JsRuntime.InvokeVoidAsync(
            "saveAsBase64",
            $"flow.{ext}",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
        );
    }

    public async Task ClearDiagram()
    {
        if (CurrentSession != null)
        {
            CurrentSession.FlowDocument = null;
        }
        await RuntimeService.ClearDiagramAsync();
    }

    private void RegisterNodeLayoutEvents(NodeModel node)
    {
        node.Moved += OnNodeMoved;
        node.SizeChanged += OnNodeSizeChanged;
    }

    private void OnNodeMoved(MovableModel movedModel)
    {
        if (movedModel is NodeModel movedNode)
            FbpLayoutHelper.RefreshPortLayout(movedNode);
        ScheduleSessionSnapshot();
    }

    private void OnNodeSizeChanged(NodeModel node)
    {
        FbpLayoutHelper.RefreshPortLayout(node);
        ScheduleSessionSnapshot();
    }

    private Task ZoomToFitFlowAsync()
    {
        try
        {
            return InvokeAsync(ZoomToFitFlow);
        }
        catch (InvalidOperationException)
        {
            ZoomToFitFlow();
            return Task.CompletedTask;
        }
    }

    private void ZoomToFitFlow()
    {
        if (Diagram == null || Diagram.Nodes.Count == 0 || Diagram.Container == null)
            return;

        Diagram.UnselectAll();
        var bounds = DiagramExtensions.GetBounds(Diagram.Nodes);
        Diagram.ZoomToFit(ZoomToFitMargin);
        var extraHeight =
            Diagram.Container.Height - ((bounds.Height + 2 * ZoomToFitMargin) * Diagram.Zoom);
        if (extraHeight > 0)
            Diagram.UpdatePan(0, extraHeight / 2);

        Diagram.Refresh();
    }

    private void OnNodeDragStart(Component component, string componentServiceId)
    {
        _draggedComponent = component;
        _draggedComponentServiceId = componentServiceId;
    }

    private void OnNodeDragEnd()
    {
        _draggedComponent = null;
        _draggedComponentServiceId = "";
    }

    private void OnNodeDrop(DragEventArgs e)
    {
        if (_draggedComponent == null || Diagram == null)
            return;

        var position = Diagram.GetRelativeMousePoint(e.ClientX - 125, e.ClientY - 100);
        EffectiveNodeFactory.AddFbpNode(
            Diagram,
            RuntimeService,
            position,
            _draggedComponent,
            new JObject { { "componentServiceId", _draggedComponentServiceId } },
            onNodeLayoutChanged: RegisterNodeLayoutEvents
        );
        OnNodeDragEnd();
    }

    private Task ExecuteNode(Model node) => RuntimeService.ExecuteNodeAsync(node);
    private Task ResetNode(Model node) => RuntimeService.ResetNodeAsync(node);
    private Task ExecuteFlow() => RuntimeService.ExecuteFlowAsync(() => InvokeAsync(StateHasChanged));

    private Task HandleSturdyRefConnectedAsync((ulong, string, string) connection) =>
        RuntimeService.HandleSturdyRefConnectedAsync(connection);

    private Task HandleSturdyRefDisconnectedAsync((ulong, string) connection) =>
        RuntimeService.HandleSturdyRefDisconnectedAsync(connection);

    private Task ConnectToStartChannelsService(
        ConnectionManager conMan,
        string petName,
        string sturdyRef
    ) => RuntimeService.ConnectToStartChannelsServiceAsync(petName, sturdyRef);

    private Task ConnectToRegistryService(ConnectionManager conMan, string petName, string sturdyRef) =>
        RuntimeService.ConnectToRegistryServiceAsync(petName, sturdyRef);

    private void SafeStateHasChanged()
    {
        try
        {
            _ = InvokeAsync(StateHasChanged);
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"SafeStateHasChanged failed: {ex.Message}");
        }
    }

    private void OnRuntimeStateChanged() => SafeStateHasChanged();

    public async ValueTask DisposeAsync()
    {
        _snapshotDebounceCts?.Cancel();
        _snapshotDebounceCts?.Dispose();
        _snapshotDebounceCts = null;

        _clearButtonCts?.Cancel();
        _clearButtonCts?.Dispose();
        _clearButtonCts = null;
        RuntimeService.StateChanged -= OnRuntimeStateChanged;
        CleanupService.UnregisterCleanup();
    }
}
