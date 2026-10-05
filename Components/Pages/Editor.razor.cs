using System.Text;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Extensions;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using BlazorDrawFBP.Services;
using Capnp.Rpc;
using Mas.Infrastructure.BlazorComponents;
using Mas.Infrastructure.Common;
using Mas.Schema.Fbp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using Newtonsoft.Json.Linq;
using Exception = System.Exception;

namespace BlazorDrawFBP.Components.Pages;

using Proxy = Proxy;

public partial class Editor : IAsyncDisposable
{
    private const double ZoomToFitMargin = 80;
    private ErrorBoundary? _canvasErrorBoundary;
    private Component? _draggedComponent;
    private string _draggedComponentServiceId = "";
    private bool _hasRendered;
    private bool _loadingFlow;
    private JObject? _pendingServicesMergeDocument;

    private CancellationTokenSource? _snapshotDebounceCts;

    [Parameter]
    public Guid? FlowId { get; set; }

    public FlowSession? CurrentSession { get; private set; }
    public IFbpRuntimeService RuntimeService =>
        CurrentSession?.RuntimeService ?? InjectedRuntimeService;
    public ConnectionManager ConMan =>
        CurrentSession?.RuntimeService.ConnectionManager ?? InjectedConMan;
    public BlazorDiagram Diagram { get; set; } = null!;

    public string ShortFlowId => FlowId.HasValue ? FlowId.Value.ToString("N")[..8] : "";

    public Dictionary<ulong, Type> InterfaceIdToType => RuntimeService.InterfaceIdToType;
    public Dictionary<string, Proxy> SturdyRef2Services => RuntimeService.SturdyRef2Services;

    public bool HasActiveExecution =>
        RuntimeService?.IsExecutingFlow == true
        || RuntimeService?.HasBusyLifecycleNodes == true
        || Diagram?.Nodes.Any(node =>
            node switch
            {
                CapnpFbpComponentModel comp => comp.DisplayLifecycleState
                    == ComponentLifecycleState.Running
                    || comp.IsLifecycleBusy,
                CapnpFbpViewComponentModel view => view.LifecycleState
                    == ComponentLifecycleState.Running
                    || view.IsLifecycleBusy,
                CapnpFbpIipComponentModel iip => iip.DisplayLifecycleState
                    == ComponentLifecycleState.Starting
                    || iip.IsLifecycleBusy,
                _ => false,
            }
        ) == true;

    public string CurrentTtlLabel =>
        CurrentSession != null
            ? FlowSession.FormatTtl(CurrentSession.Ttl)
            : FlowSession.FormatTtl(TimeSpan.FromMinutes(30));

    private IFbpNodeFactory EffectiveNodeFactory => NodeFactory ?? new FbpNodeFactory();
    private IFbpDiagramFactory EffectiveDiagramFactory => DiagramFactory ?? new FbpDiagramFactory();

    private IFlowDocumentService EffectiveFlowDocumentService =>
        FlowDocumentService ?? new FlowDocumentService(EffectiveNodeFactory);

    public async ValueTask DisposeAsync()
    {
        _snapshotDebounceCts?.Cancel();
        _snapshotDebounceCts?.Dispose();
        _snapshotDebounceCts = null;

        _clearButtonCts?.Cancel();
        _clearButtonCts?.Dispose();
        _clearButtonCts = null;
        RuntimeService.StateChanged -= OnRuntimeStateChanged;
        RuntimeService.ServiceConnectionDropped -= OnServiceConnectionDropped;
        CleanupService.UnregisterCleanup();
    }

    private async Task OnBeforeInternalNavigation(LocationChangingContext context)
    {
        if (HasActiveExecution)
        {
            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Processes are Running",
                "This flow has running processes or active executions. Navigating away will detach your session. Do you want to proceed?",
                "Leave",
                cancelText: "Stay"
            );

            if (confirmed != true)
                context.PreventNavigation();
        }
    }

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

                if (CurrentSession != null)
                    await CurrentSession.RuntimeService.CheckConnectedServicesHealthAsync(
                        cts.Token
                    );

                var doc = await ExportFlowJsonAsync();
                if (CurrentSession != null)
                {
                    doc["session"] = new JObject
                    {
                        { "ttlMinutes", CurrentSession.Ttl.TotalMinutes },
                    };
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

    public void CycleFlowTtl()
    {
        if (CurrentSession == null)
            return;

        var newTtl = CurrentSession.CycleTtl();
        var label = FlowSession.FormatTtl(newTtl);
        Snackbar.Add($"Session detached TTL set to {label}", Severity.Info);
        ScheduleSessionSnapshot();
        SafeStateHasChanged();
    }

    public async Task TerminateFlowSessionAsync()
    {
        if (!FlowId.HasValue)
            return;

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Terminate Flow Session?",
            "Are you sure you want to terminate this flow session? All running processes and channels will be stopped immediately and the session will be purged.",
            "Terminate",
            cancelText: "Cancel"
        );

        if (confirmed != true)
            return;

        var targetFlowId = FlowId.Value;
        if (SessionStore.RemoveSession(targetFlowId, out var session))
        {
            if (session.RuntimeService is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
            else
                await session.RuntimeService.ClearDiagramAsync();
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
            return;

        if (CurrentSession != null)
        {
            CurrentSession.RuntimeService.StateChanged -= OnRuntimeStateChanged;
            CurrentSession.RuntimeService.ServiceConnectionDropped -= OnServiceConnectionDropped;
            SessionStore.MarkDetached(CurrentSession.Id);
        }

        CurrentSession = SessionStore.GetOrCreateSession(FlowId.Value);
        SessionStore.MarkAttached(FlowId.Value);
        CleanupService.RegisterCleanup(() =>
        {
            if (FlowId.HasValue)
                SessionStore.MarkDetached(FlowId.Value);
            return Task.CompletedTask;
        });
        CurrentSession.RuntimeService.StateChanged += OnRuntimeStateChanged;
        CurrentSession.RuntimeService.ServiceConnectionDropped += OnServiceConnectionDropped;

        if (CurrentSession.Diagram == null)
        {
            CurrentSession.Diagram = CreateConfiguredDiagram(CurrentSession.RuntimeService);
            Diagram = CurrentSession.Diagram;
            if (CurrentSession.FlowDocument != null)
                await LoadFlowFromJsonAsync(CurrentSession.FlowDocument);
        }
        else
        {
            Diagram = CurrentSession.Diagram;
            CurrentSession.RuntimeService.Diagram = Diagram;
            await CurrentSession.RuntimeService.CheckConnectedServicesHealthAsync();
            if (CurrentSession.FlowDocument != null)
                await TryMergeFlowServicesIntoLocalStorageAsync(CurrentSession.FlowDocument);
        }

        SafeStateHasChanged();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        _hasRendered = true;

        if (await LocalStorage.ContainKeyAsync(StoredSrData.StorageKey))
        {
            var allBookmarks = await StoredSrData.GetAllData(LocalStorage);
            allBookmarks.Sort();

            foreach (var ssrd in allBookmarks.Where(ssrd => ssrd.AutoConnect))
                if (ssrd.InterfaceId == Shared.Shared.ChannelStarterInterfaceId)
                    await ConnectToStartChannelsService(ConMan, ssrd.PetName, ssrd.SturdyRef);
                else if (ssrd.InterfaceId == Shared.Shared.RegistryInterfaceId)
                    await ConnectToRegistryService(ConMan, ssrd.PetName, ssrd.SturdyRef);
        }

        if (_pendingServicesMergeDocument != null)
        {
            var doc = _pendingServicesMergeDocument;
            _pendingServicesMergeDocument = null;
            await MergeFlowServicesIntoLocalStorageAsync(doc);
        }

        SafeStateHasChanged();
    }

    private BlazorDiagram CreateConfiguredDiagram(IFbpRuntimeService runtime)
    {
        var diagram = EffectiveDiagramFactory.CreateConfiguredDiagram(
            runtime,
            ScheduleSessionSnapshot,
            SafeStateHasChanged,
            ZoomToFitFlowAsync
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
        return await EffectiveFlowDocumentService.ExportFlowDocumentAsync(
            Diagram,
            RuntimeService,
            asMermaid
        );
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

            if (CurrentSession != null && dia["session"]?["ttlMinutes"] != null)
                if (
                    double.TryParse(dia["session"]?["ttlMinutes"]?.ToString(), out var mins)
                    && mins > 0
                )
                    CurrentSession.Ttl = TimeSpan.FromMinutes(mins);

            await TryMergeFlowServicesIntoLocalStorageAsync(dia);
        }
        finally
        {
            _loadingFlow = false;
        }

        ScheduleSessionSnapshot();
    }

    protected async Task LoadFlow(IBrowserFile? file)
    {
        if (file == null)
            return;
        var s = file.OpenReadStream();
        if (s.Length > 1 * 1024 * 1024)
            return; // 1 MB
        var dia = JObject.Parse(await new StreamReader(s).ReadToEndAsync());
        await LoadFlowFromJsonAsync(dia);
    }

    protected async Task SaveFlow(bool asMermaid)
    {
        await RuntimeService.CheckConnectedServicesHealthAsync();
        var (dia, mermaid) = await ExportFlowDocumentAsync(asMermaid);
        var content = asMermaid ? mermaid ?? "" : dia?.ToString() ?? "{}";
        var ext = asMermaid ? "mmd" : "json";
        await JsRuntime.InvokeVoidAsync(
            "saveAsBase64",
            $"flow.{ext}",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
        );
    }

    public async Task TryMergeFlowServicesIntoLocalStorageAsync(JObject? flowDoc)
    {
        if (flowDoc == null)
            return;

        if (!_hasRendered)
        {
            _pendingServicesMergeDocument = flowDoc;
            return;
        }

        await MergeFlowServicesIntoLocalStorageAsync(flowDoc);
    }

    public async Task<int> MergeFlowServicesIntoLocalStorageAsync(JObject flowDoc)
    {
        if (LocalStorage == null)
            return 0;

        List<StoredSrData> bookmarks;
        try
        {
            bookmarks = await StoredSrData.GetAllData(LocalStorage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to retrieve bookmarks from LocalStorage: {ex.Message}");
            return 0;
        }

        var addedCount = 0;

        if (flowDoc["services"]?["channels"] is JObject channelsObj)
            foreach (var prop in channelsObj.Properties())
            {
                var serviceId = prop.Name;
                var sturdyRef = prop.Value?.ToString();
                if (string.IsNullOrWhiteSpace(sturdyRef))
                    continue;

                var isAlive = RuntimeService.ChannelServiceIdToPetNameAndSturdyRef.Any(entry =>
                    entry.Value.Item2 == sturdyRef
                    && RuntimeService.ServiceId2ChannelStarterServices.ContainsKey(entry.Key)
                );

                if (!isAlive)
                    continue;

                var petName =
                    RuntimeService.ChannelServiceIdToPetNameAndSturdyRef.TryGetValue(
                        serviceId,
                        out var tuple
                    ) && !string.IsNullOrWhiteSpace(tuple.Item1)
                        ? tuple.Item1
                        : serviceId;

                var exists = bookmarks.Any(b =>
                    b.InterfaceId == Shared.Shared.ChannelStarterInterfaceId
                    && string.Equals(b.SturdyRef, sturdyRef, StringComparison.Ordinal)
                );

                if (!exists)
                {
                    bookmarks.Add(
                        new StoredSrData
                        {
                            InterfaceId = Shared.Shared.ChannelStarterInterfaceId,
                            PetName = petName,
                            SturdyRef = sturdyRef,
                            AutoConnect = true,
                        }
                    );
                    addedCount++;
                }
            }

        if (flowDoc["services"]?["components"] is JObject componentsObj)
            foreach (var prop in componentsObj.Properties())
            {
                var serviceId = prop.Name;
                var sturdyRef = prop.Value?.ToString();
                if (string.IsNullOrWhiteSpace(sturdyRef))
                    continue;

                var isAlive = RuntimeService.RegistryServiceIdToPetNameAndSturdyRef.Any(entry =>
                    entry.Key != "no_service"
                    && entry.Value.Item2 == sturdyRef
                    && RuntimeService.ServiceId2Registries.ContainsKey(entry.Key)
                );

                if (!isAlive)
                    continue;

                var petName =
                    RuntimeService.RegistryServiceIdToPetNameAndSturdyRef.TryGetValue(
                        serviceId,
                        out var tuple
                    ) && !string.IsNullOrWhiteSpace(tuple.Item1)
                        ? tuple.Item1
                        : serviceId;

                var exists = bookmarks.Any(b =>
                    b.InterfaceId == Shared.Shared.RegistryInterfaceId
                    && string.Equals(b.SturdyRef, sturdyRef, StringComparison.Ordinal)
                );

                if (!exists)
                {
                    bookmarks.Add(
                        new StoredSrData
                        {
                            InterfaceId = Shared.Shared.RegistryInterfaceId,
                            PetName = petName,
                            SturdyRef = sturdyRef,
                            AutoConnect = true,
                        }
                    );
                    addedCount++;
                }
            }

        if (addedCount > 0)
            try
            {
                bookmarks.Sort();
                await StoredSrData.SaveAllData(LocalStorage, bookmarks);
                Snackbar?.Add(
                    $"Imported {addedCount} service bookmark{(addedCount > 1 ? "s" : "")} from flow to local storage.",
                    Severity.Success
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save merged bookmarks to LocalStorage: {ex.Message}");
            }

        return addedCount;
    }

    public async Task ClearDiagram()
    {
        if (CurrentSession != null)
            CurrentSession.FlowDocument = null;
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
        var bounds = Diagram.Nodes.GetBounds();
        Diagram.ZoomToFit(ZoomToFitMargin);
        var extraHeight =
            Diagram.Container.Height - (bounds.Height + 2 * ZoomToFitMargin) * Diagram.Zoom;
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

    private Task ExecuteNode(Model node)
    {
        return RuntimeService.ExecuteNodeAsync(node);
    }

    private Task ResetNode(Model node)
    {
        return RuntimeService.ResetNodeAsync(node);
    }

    private Task ExecuteFlow()
    {
        return RuntimeService.ExecuteFlowAsync(() => InvokeAsync(StateHasChanged));
    }

    private Task HandleSturdyRefConnectedAsync((ulong, string, string) connection)
    {
        return RuntimeService.HandleSturdyRefConnectedAsync(connection);
    }

    private Task HandleSturdyRefDisconnectedAsync((ulong, string) connection)
    {
        return RuntimeService.HandleSturdyRefDisconnectedAsync(connection);
    }

    private Task ConnectToStartChannelsService(
        ConnectionManager conMan,
        string petName,
        string sturdyRef
    )
    {
        return RuntimeService.ConnectToStartChannelsServiceAsync(petName, sturdyRef);
    }

    private Task ConnectToRegistryService(
        ConnectionManager conMan,
        string petName,
        string sturdyRef
    )
    {
        return RuntimeService.ConnectToRegistryServiceAsync(petName, sturdyRef);
    }

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

    private void OnRuntimeStateChanged()
    {
        SafeStateHasChanged();
    }

    private void OnServiceConnectionDropped(ServiceConnectionDroppedEventArgs args)
    {
        try
        {
            _ = InvokeAsync(() =>
            {
                Snackbar.Add(args.Message, Severity.Warning);
                SafeStateHasChanged();
            });
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"OnServiceConnectionDropped toast failed: {ex.Message}");
        }
    }
}
