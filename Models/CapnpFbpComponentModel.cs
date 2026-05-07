using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Pages;
using Mas.Infrastructure.Common;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Models;

public enum ComponentLifecycleState
{
    Idle,
    Starting,
    Running,
    Stopping,
    Failed,
    Closed,
}

public class CapnpFbpComponentModel : NodeModel, IAsyncDisposable
{
    internal const double ProcExpansionPaddingPx = 12d;
    internal const double ProcExpansionSpacingPx = 8d;
    internal const double ProcRowMinHeightPx = 48d;
    internal const double ProcRowVerticalPaddingPx = 8d;
    internal const double ProcRowPortSpacingPx = 28d;

    private readonly List<ProcChildContext> _procChildContexts = [];
    private readonly object _procSyncGate = new();
    private Task _procSyncTask = Task.CompletedTask;
    private bool _procFullSyncRequested;
    private bool _procCountSyncRequested;
    private int _inParallelCount = 1;
    private bool _procChildrenExpanded;
    private ComponentLifecycleState? _procAdjustmentState;
    private bool _procCountAdjustmentInFlight;

    public CapnpFbpComponentModel(Point position = null)
        : base(position) { }

    public CapnpFbpComponentModel(string id, Point position = null)
        : base(id, position) { }

    public Editor Editor { get; set; }
    public string ComponentId { get; set; }
    public string ComponentServiceId { get; set; }
    public string ComponentName { get; set; }
    public string ProcessName { get; set; }
    public string ShortDescription { get; set; }
    public string Cmd { get; set; }

    public int InParallelCount
    {
        get => _inParallelCount;
        set
        {
            var normalized = Math.Max(1, value);
            if (_inParallelCount == normalized)
                return;

            _inParallelCount = normalized;
            QueueProcCountSync();
            RefreshProcPresentation();
        }
    }

    public bool Editable { get; set; } = true;
    public static int ProcessNo { get; set; }
    public string DefaultConfigString { get; set; }
    public string ConfigString { get; set; }
    public int DisplayNoOfConfigLines { get; set; } = 3;
    public bool ProcessStarted { get; protected set; }
    public ComponentLifecycleState LifecycleState { get; private set; } = ComponentLifecycleState.Idle;
    public string LifecycleError { get; private set; }
    public bool IsInternalProcChild { get; protected set; }
    public CapnpFbpComponentModel ProcOwnerNode { get; protected set; }
    public int ProcDisplayIndex { get; protected set; } = 1;
    public double? PortLayoutHeightOverride { get; private set; }

    public IReadOnlyList<CapnpFbpComponentModel> ProcChildComponents =>
        _procChildContexts.Select(context => context.Node).ToArray();

    public bool ProcControlsVisible => !IsInternalProcChild && IsProcMultiplicationEligible;
    public bool HasProcChildren => _procChildContexts.Count > 0;

    public bool ProcChildrenExpanded
    {
        get => _procChildrenExpanded && HasProcChildren;
        set
        {
            var normalized = value && HasProcChildren;
            if (_procChildrenExpanded == normalized)
                return;

            _procChildrenExpanded = normalized;
            RefreshProcPresentation();
            RefreshAll();
        }
    }

    public bool CanStart =>
        !CanStop && !IsLifecycleBusy && EnumerateProcNodes().Any(node =>
            node.LifecycleState is ComponentLifecycleState.Idle
                or ComponentLifecycleState.Failed
                or ComponentLifecycleState.Closed
        );

    public bool CanStop =>
        EnumerateProcNodes().Any(node => node.LifecycleState == ComponentLifecycleState.Running);

    public bool IsLifecycleBusy =>
        _procCountAdjustmentInFlight || HasProcLifecycleTransitionInProgress();

    public string LifecycleLabel => LifecycleState.ToString();
    public ComponentLifecycleState DisplayLifecycleState => ResolveDisplayLifecycleState();
    public string DisplayLifecycleError => ResolveDisplayLifecycleError();
    public string DisplayLifecycleLabel => ResolveDisplayLifecycleLabel();
    public bool AnyProcRuntimeAttached =>
        RemoteProcessAttached() || _procChildContexts.Any(context => context.Node.AnyProcRuntimeAttached);

    public bool IsProcMultiplicationEligible =>
        !IsInternalProcChild
        && SupportsProcMultiplication
        && EvaluateProcMultiplicationEligibility(GetVisibleIncomingProcLinks());
    public bool ProcCountAdjustmentLocked =>
        IsInternalProcChild
        || _procCountAdjustmentInFlight
        || !CanAdjustProcCountIncrementally(GetVisibleIncomingProcLinks());

    protected virtual bool SupportsProcMultiplication => false;

    public virtual bool RemoteProcessAttached() => false;
    public virtual bool CanEditCommandLine() => false;

    public virtual async Task StartProcess(ConnectionManager conMan)
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: override StartProcess!"
        );
        SetLifecycleState(ComponentLifecycleState.Idle);
    }

    public virtual async Task StopProcess(ConnectionManager conMan)
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: override StopProcess"
        );
        SetLifecycleState(ComponentLifecycleState.Idle);
    }

    public virtual Task ResetExecution()
    {
        SetLifecycleState(ComponentLifecycleState.Idle, refresh: true);
        return Task.CompletedTask;
    }

    public async Task RebindToComponentServiceAsync(Component component, string componentServiceId)
    {
        if (component == null)
            throw new ArgumentNullException(nameof(component));

        await ShutdownForComponentServiceSwitchAsync();
        ApplyComponentServiceBinding(component, componentServiceId);
        CapnpFbpPortLayout.Apply(this, refreshPorts: false);
        SetLifecycleState(ComponentLifecycleState.Idle, refresh: true);
        await EnsureProcStructureSynchronizedAsync();
    }

    public Task EnsureProcStructureSynchronizedAsync()
    {
        if (IsInternalProcChild)
            return ProcOwnerNode?.EnsureProcStructureSynchronizedAsync() ?? Task.CompletedTask;

        lock (_procSyncGate)
        {
            _procFullSyncRequested = true;
            if (_procSyncTask.IsCompleted)
                _procSyncTask = RunProcStructureSyncLoopAsync();

            return _procSyncTask;
        }
    }

    public void QueueProcStructureSync()
    {
        _ = EnsureProcStructureSynchronizedAsync();
    }

    private Task EnsureProcCountSynchronizedAsync()
    {
        if (IsInternalProcChild)
            return ProcOwnerNode?.EnsureProcCountSynchronizedAsync() ?? Task.CompletedTask;

        lock (_procSyncGate)
        {
            _procCountSyncRequested = true;
            if (_procSyncTask.IsCompleted)
                _procSyncTask = RunProcStructureSyncLoopAsync();

            return _procSyncTask;
        }
    }

    private void QueueProcCountSync()
    {
        _ = EnsureProcCountSynchronizedAsync();
    }

    protected virtual Task ShutdownForComponentServiceSwitchAsync() => ResetExecution();

    public async Task AdjustProcCountAsync(int delta)
    {
        if (delta == 0 || IsInternalProcChild || _procCountAdjustmentInFlight)
            return;

        var targetCount = Math.Max(1, _inParallelCount + delta);
        if (targetCount == _inParallelCount)
            return;

        var previousAdjustmentState = _procAdjustmentState;
        _procCountAdjustmentInFlight = true;
        _procAdjustmentState = ResolveProcAdjustmentStateForCountChange(targetCount);
        RefreshAll();
        RefreshLinks();

        try
        {
            InParallelCount = targetCount;
            await EnsureProcCountSynchronizedAsync();
        }
        finally
        {
            _procAdjustmentState = previousAdjustmentState;
            _procCountAdjustmentInFlight = false;
            RefreshAll();
            RefreshLinks();
        }
    }

    protected virtual void ApplyComponentServiceBinding(Component component, string componentServiceId)
    {
        var componentId = component.Info?.Id;
        if (!string.IsNullOrWhiteSpace(componentId))
            ComponentId = componentId;

        ComponentServiceId = componentServiceId;
        ComponentName = component.Info?.Name ?? ComponentId;
        ShortDescription = component.Info?.Description ?? "";
        DefaultConfigString = component.DefaultConfig?.Value ?? "";
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpComponentModel::DisposeAsyncCore");

        if (!IsInternalProcChild)
            await TeardownProcChildrenAsync();

        await RemoveInternalProcLinksAsync();

        if (!IsInternalProcChild && Editor?.Diagram != null)
        {
            await Shared.Shared.RestoreDefaultPortVisibilityOfAttachedComponent(
                this,
                Editor.Diagram,
                this
            );
        }

        await DisposeStandardPorts();
    }

    protected async Task StartOwnedProcChildrenAsync(ConnectionManager conMan)
    {
        SyncOwnedProcChildSettings();
        foreach (var child in ProcChildComponents)
            await child.StartProcess(conMan);
    }

    protected async Task StopOwnedProcChildrenAsync(ConnectionManager conMan)
    {
        foreach (var child in ProcChildComponents.Reverse())
            await child.StopProcess(conMan);
    }

    protected async Task ResetOwnedProcChildrenAsync()
    {
        foreach (var child in ProcChildComponents.Reverse())
            await child.ResetExecution();
    }

    protected async Task ShutdownOwnedProcChildrenAsync()
    {
        foreach (var child in ProcChildComponents.Reverse())
            await child.ShutdownForComponentServiceSwitchAsync();
    }

    protected void SyncOwnedProcChildSettings()
    {
        foreach (var child in ProcChildComponents)
        {
            child.ComponentId = ComponentId;
            child.ComponentServiceId = ComponentServiceId;
            child.ComponentName = ComponentName;
            child.ProcessName = $"{ProcessName} [{child.ProcDisplayIndex}]";
            child.ShortDescription = ShortDescription;
            child.Cmd = Cmd;
            child.DefaultConfigString = DefaultConfigString;
            child.ConfigString = ConfigString;
            child.DisplayNoOfConfigLines = DisplayNoOfConfigLines;
        }
    }

    protected void SetLifecycleState(
        ComponentLifecycleState state,
        string error = null,
        bool refresh = false
    )
    {
        LifecycleState = state;
        ProcessStarted = state == ComponentLifecycleState.Running;
        LifecycleError = state == ComponentLifecycleState.Failed ? error ?? LifecycleError : null;

        if (refresh)
        {
            RefreshAll();
            RefreshLinks();
        }
    }

    protected void SetLifecycleFault(Exception exception, bool refresh = false)
    {
        Console.Error.WriteLine(exception);
        SetLifecycleState(ComponentLifecycleState.Failed, exception.Message, refresh);
    }

    protected virtual CapnpFbpComponentModel CreateProcChildModel(int displayIndex) => null;

    private async Task RunProcStructureSyncLoopAsync()
    {
        while (true)
        {
            bool fullSyncRequested;
            bool countSyncRequested;
            lock (_procSyncGate)
            {
                if (!_procFullSyncRequested && !_procCountSyncRequested)
                    return;

                fullSyncRequested = _procFullSyncRequested;
                countSyncRequested = _procCountSyncRequested;
                _procFullSyncRequested = false;
                _procCountSyncRequested = false;
            }

            if (fullSyncRequested)
            {
                await ResyncProcStructureCoreAsync();
                continue;
            }

            if (countSyncRequested)
                await SyncProcCountIncrementallyAsync();
        }
    }

    private async Task ResyncProcStructureCoreAsync()
    {
        if (IsInternalProcChild)
            return;

        var affectedVisibleNodes = CollectProcAffectedVisibleNodes();
        if (affectedVisibleNodes.Any(HasActiveLifecycle))
        {
            foreach (var node in affectedVisibleNodes)
                await Shared.Shared.ResetNodeLifecycleAsync(node);
        }

        await TeardownProcChildrenAsync();

        var incomingLinks = GetVisibleIncomingProcLinks();
        var outgoingLinks = GetVisibleOutgoingProcLinks();
        var desiredChildCount =
            EvaluateProcMultiplicationEligibility(incomingLinks) ? Math.Max(0, InParallelCount - 1) : 0;

        for (var childOffset = 0; childOffset < desiredChildCount; childOffset++)
        {
            var displayIndex = childOffset + 2;
            var context = CreateProcChildContext(displayIndex, incomingLinks, outgoingLinks);
            if (context == null)
                break;

            _procChildContexts.Add(context);
        }

        RefreshProcPresentation();
        RefreshAll();
        RefreshLinks();
    }

    private async Task SyncProcCountIncrementallyAsync()
    {
        if (IsInternalProcChild)
            return;

        var incomingLinks = GetVisibleIncomingProcLinks();
        var eligible = EvaluateProcMultiplicationEligibility(incomingLinks);
        if (!eligible)
        {
            if (_procChildContexts.Count > 0)
                await ResyncProcStructureCoreAsync();
            else
                RefreshProcPresentation();

            RefreshAll();
            RefreshLinks();
            return;
        }

        if (!CanAdjustProcCountIncrementally(incomingLinks))
        {
            _inParallelCount = _procChildContexts.Count + 1;
            RefreshProcPresentation();
            RefreshAll();
            RefreshLinks();
            return;
        }

        var outgoingLinks = GetVisibleOutgoingProcLinks();
        var desiredChildCount = Math.Max(0, InParallelCount - 1);
        if (desiredChildCount == _procChildContexts.Count)
        {
            RefreshProcPresentation();
            RefreshAll();
            RefreshLinks();
            return;
        }

        SyncOwnedProcChildSettings();

        while (_procChildContexts.Count < desiredChildCount)
        {
            await AddProcChildIncrementallyAsync(
                _procChildContexts.Count + 2,
                incomingLinks,
                outgoingLinks
            );
        }

        while (_procChildContexts.Count > desiredChildCount)
            await RemoveLastProcChildIncrementallyAsync();

        RefreshProcPresentation();
        RefreshAll();
        RefreshLinks();
    }

    private async Task TeardownProcChildrenAsync()
    {
        var children = ProcChildComponents.ToList();
        _procChildContexts.Clear();
        _procChildrenExpanded = false;

        foreach (var child in children)
            await child.DisposeAsync();

        RefreshProcPresentation();
    }

    private async Task RemoveInternalProcLinksAsync()
    {
        var internalLinks = Shared
            .Shared.AttachedLinks(this)
            .OfType<RememberCapnpPortsLinkModel>()
            .Where(link => link.IsInternalProcLink)
            .Distinct()
            .ToList();

        foreach (var link in internalLinks)
        {
            await link.DisconnectProcessOutPortAsync();
            await link.DisconnectWriterAsync();
            link.DetachFromPorts();
            link.OutPortModel.SyncLinkedWriterState();
            link.OutPortModel.SyncVisibility();
            link.OutPortModel.SyncProcessConnectionState();
            link.InPortModel.SyncVisibility();
            link.OutPortModel.Refresh();
            link.InPortModel.Refresh();
            link.OutPortModel.Parent?.RefreshAll();
            link.InPortModel.Parent?.RefreshAll();
        }
    }

    private static void ApplyInternalProcLinkVisuals(RememberCapnpPortsLinkModel link)
    {
        link.OutPortModel.SyncVisibility();
        link.InPortModel.SyncVisibility();
        CapnpFbpPortColors.ApplyLinkColor(link);
        link.OutPortModel.Parent?.RefreshAll();
        link.InPortModel.Parent?.RefreshAll();
    }

    private async ValueTask DisposeStandardPorts()
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpComponentModel::DisposeStandardPorts");
        foreach (var port in Ports)
        {
            if (port is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
        }
    }

    private void InitializeProcChildNode(CapnpFbpComponentModel child, int displayIndex)
    {
        child.IsInternalProcChild = true;
        child.ProcOwnerNode = this;
        child.ProcDisplayIndex = displayIndex;
        child.Editor = Editor;
        child.ComponentId = ComponentId;
        child.ComponentServiceId = ComponentServiceId;
        child.ComponentName = ComponentName;
        child.ProcessName = $"{ProcessName} [{displayIndex}]";
        child.ShortDescription = ShortDescription;
        child.Cmd = Cmd;
        child._inParallelCount = 1;
        child.Editable = false;
        child.DefaultConfigString = DefaultConfigString;
        child.ConfigString = ConfigString;
        child.DisplayNoOfConfigLines = DisplayNoOfConfigLines;
        child.Size = new Size(Size?.Width ?? Shared.Shared.CardWidth, Shared.Shared.CardHeight);
        child.PortLayoutHeightOverride = Shared.Shared.CardHeight;

        ClonePortsTo(child);
        CopyProcBindingTo(child);
    }

    private ProcChildContext CreateProcChildContext(
        int displayIndex,
        IReadOnlyCollection<RememberCapnpPortsLinkModel> incomingLinks,
        IReadOnlyCollection<RememberCapnpPortsLinkModel> outgoingLinks
    )
    {
        var child = CreateProcChildModel(displayIndex);
        if (child == null)
            return null;

        InitializeProcChildNode(child, displayIndex);
        var context = new ProcChildContext(child);
        MirrorProcLinksToChild(context, incomingLinks, outgoingLinks);
        return context;
    }

    private void MirrorProcLinksToChild(
        ProcChildContext context,
        IReadOnlyCollection<RememberCapnpPortsLinkModel> incomingLinks,
        IReadOnlyCollection<RememberCapnpPortsLinkModel> outgoingLinks
    )
    {
        var child = context.Node;

        foreach (var incomingLink in incomingLinks)
        {
            if (
                FindMatchingPort<CapnpFbpInPortModel>(child, incomingLink.InPortModel)
                is not { } childInPort
            )
            {
                continue;
            }

            var internalIncomingLink = new RememberCapnpPortsLinkModel(
                incomingLink.OutPortModel,
                childInPort
            )
            {
                IsInternalProcLink = true,
            };
            internalIncomingLink.AttachToPorts();
            ApplyInternalProcLinkVisuals(internalIncomingLink);
            context.Links.Add(internalIncomingLink);
        }

        foreach (var outgoingLink in outgoingLinks)
        {
            if (
                FindMatchingPort<CapnpFbpOutPortModel>(child, outgoingLink.OutPortModel)
                is not { } childOutPort
            )
            {
                continue;
            }

            var internalOutgoingLink = new RememberCapnpPortsLinkModel(
                childOutPort,
                outgoingLink.InPortModel
            )
            {
                IsInternalProcLink = true,
            };
            internalOutgoingLink.AttachToPorts();
            ApplyInternalProcLinkVisuals(internalOutgoingLink);
            context.Links.Add(internalOutgoingLink);
        }
    }

    protected virtual void CopyProcBindingTo(CapnpFbpComponentModel child) { }

    private void ClonePortsTo(CapnpFbpComponentModel child)
    {
        foreach (
            var port in Ports
                .OfType<CapnpFbpPortModel>()
                .OrderBy(port => port.ThePortType)
                .ThenBy(port => port.OrderNo)
        )
        {
            CapnpFbpPortModel clonedPort = port switch
            {
                CapnpFbpInPortModel => new CapnpFbpInPortModel(child, PortAlignment.Left),
                CapnpFbpOutPortModel => new CapnpFbpOutPortModel(child, PortAlignment.Right),
                _ => null,
            };
            if (clonedPort == null)
                continue;

            clonedPort.Name = port.Name;
            clonedPort.ContentType = port.ContentType;
            clonedPort.Description = port.Description;
            clonedPort.OrderNo = port.OrderNo;
            clonedPort.IsArrayPort = port.IsArrayPort;
            child.AddPort(clonedPort);
        }
    }

    private IReadOnlyList<RememberCapnpPortsLinkModel> GetVisibleIncomingProcLinks() =>
        Shared
            .Shared.AttachedLinks(this)
            .OfType<RememberCapnpPortsLinkModel>()
            .Where(link => !link.IsInternalProcLink && ReferenceEquals(link.InPortModel.Parent, this))
            .ToList();

    private IReadOnlyList<RememberCapnpPortsLinkModel> GetVisibleOutgoingProcLinks() =>
        Shared
            .Shared.AttachedLinks(this)
            .OfType<RememberCapnpPortsLinkModel>()
            .Where(link => !link.IsInternalProcLink && ReferenceEquals(link.OutPortModel.Parent, this))
            .ToList();

    private static bool EvaluateProcMultiplicationEligibility(
        IReadOnlyCollection<RememberCapnpPortsLinkModel> incomingLinks
    ) => incomingLinks.Count > 0 && incomingLinks.All(link => link.OutPortModel.IsArrayPort);

    private bool CanAdjustProcCountIncrementally(
        IReadOnlyCollection<RememberCapnpPortsLinkModel> incomingLinks
    ) =>
        !HasProcLifecycleTransitionInProgress()
        && incomingLinks.All(link =>
            !RequiresLiveProcSourceMutation(link.OutPortModel.Parent as Model)
            || SupportsLiveProcSourceMutation(link.OutPortModel.Parent as Model)
        );

    private bool HasProcLifecycleTransitionInProgress() =>
        EnumerateProcNodes().Any(node =>
            node.LifecycleState is ComponentLifecycleState.Starting or ComponentLifecycleState.Stopping
        );

    private ComponentLifecycleState? ResolveProcAdjustmentStateForCountChange(int targetCount)
    {
        if (targetCount > _inParallelCount && LifecycleState == ComponentLifecycleState.Running)
            return ComponentLifecycleState.Starting;

        if (targetCount < _inParallelCount && _procChildContexts.LastOrDefault()?.Node.CanStop == true)
            return ComponentLifecycleState.Stopping;

        return null;
    }

    private static bool RequiresLiveProcSourceMutation(Model source) =>
        source switch
        {
            CapnpFbpProcessComponentModel
            {
                LifecycleState: ComponentLifecycleState.Running
                    or ComponentLifecycleState.Starting
                    or ComponentLifecycleState.Stopping
            } => true,
            CapnpFbpRunnableComponentModel
            {
                LifecycleState: ComponentLifecycleState.Running
                    or ComponentLifecycleState.Starting
                    or ComponentLifecycleState.Stopping
            } => true,
            CapnpFbpViewComponentModel
            {
                LifecycleState: ComponentLifecycleState.Running
                    or ComponentLifecycleState.Starting
                    or ComponentLifecycleState.Stopping
            } => true,
            CapnpFbpIipComponentModel { DisplayLifecycleState: ComponentLifecycleState.Running } =>
                true,
            _ => false,
        };

    private static bool SupportsLiveProcSourceMutation(Model source) =>
        source is CapnpFbpProcessComponentModel { SupportsLivePortChanges: true };

    private HashSet<Model> CollectProcAffectedVisibleNodes()
    {
        var nodes = new HashSet<Model> { this };

        foreach (var link in GetVisibleIncomingProcLinks())
            nodes.Add(link.OutPortModel.Parent as Model);

        foreach (var link in GetVisibleOutgoingProcLinks())
            nodes.Add(link.InPortModel.Parent as Model);

        foreach (var child in ProcChildComponents)
        {
            foreach (var link in Shared.Shared.AttachedLinks(child).OfType<RememberCapnpPortsLinkModel>())
            {
                if (
                    link.OutPortModel.Parent is Model source
                    && source is not CapnpFbpComponentModel { IsInternalProcChild: true }
                )
                {
                    nodes.Add(source);
                }

                if (
                    link.InPortModel.Parent is Model target
                    && target is not CapnpFbpComponentModel { IsInternalProcChild: true }
                )
                {
                    nodes.Add(target);
                }
            }
        }

        nodes.RemoveWhere(node => node == null);
        return nodes;
    }

    private static T FindMatchingPort<T>(CapnpFbpComponentModel child, CapnpFbpPortModel sourcePort)
        where T : CapnpFbpPortModel
    {
        return child.Ports.OfType<T>().FirstOrDefault(port =>
            port.OrderNo == sourcePort.OrderNo
            && port.ThePortType == sourcePort.ThePortType
            && string.Equals(port.Name, sourcePort.Name, StringComparison.Ordinal)
        );
    }

    private IEnumerable<CapnpFbpComponentModel> EnumerateProcNodes()
    {
        yield return this;
        foreach (var child in _procChildContexts.Select(context => context.Node))
            yield return child;
    }

    private ComponentLifecycleState ResolveDisplayLifecycleState()
    {
        var states = EnumerateProcNodes().Select(node => node.LifecycleState).ToList();

        if (states.Contains(ComponentLifecycleState.Failed))
            return ComponentLifecycleState.Failed;
        if (_procAdjustmentState == ComponentLifecycleState.Stopping)
            return ComponentLifecycleState.Stopping;
        if (_procAdjustmentState == ComponentLifecycleState.Starting)
            return ComponentLifecycleState.Starting;
        if (states.Contains(ComponentLifecycleState.Stopping))
            return ComponentLifecycleState.Stopping;
        if (states.Contains(ComponentLifecycleState.Starting))
            return ComponentLifecycleState.Starting;
        if (states.Contains(ComponentLifecycleState.Running))
            return ComponentLifecycleState.Running;
        if (states.Contains(ComponentLifecycleState.Closed))
            return ComponentLifecycleState.Closed;

        return ComponentLifecycleState.Idle;
    }

    private string ResolveDisplayLifecycleError()
    {
        if (DisplayLifecycleState != ComponentLifecycleState.Failed)
            return null;

        return EnumerateProcNodes()
            .Select(node => node.LifecycleError)
            .FirstOrDefault(error => !string.IsNullOrWhiteSpace(error));
    }

    private string ResolveDisplayLifecycleLabel()
    {
        if (_procChildContexts.Count == 0)
            return _procAdjustmentState?.ToString() ?? LifecycleLabel;

        var states = EnumerateProcNodes().Select(node => node.LifecycleState).ToList();
        var displayState = DisplayLifecycleState;
        var matchingStateCount = states.Count(state => state == displayState);
        if (matchingStateCount == 0 && _procAdjustmentState == displayState)
            matchingStateCount = 1;
        return $"{displayState} ({matchingStateCount}/{states.Count})";
    }

    private void RefreshProcPresentation()
    {
        if (IsInternalProcChild)
            return;

        if (!HasProcChildren)
            _procChildrenExpanded = false;

        var width = Size?.Width ?? Shared.Shared.CardWidth;
        var baseHeight = (double)Shared.Shared.CardHeight;
        var expandedHeight = baseHeight;
        if (_procChildrenExpanded && HasProcChildren)
        {
            var expandedRows = GetExpandedProcRowLayouts();
            expandedHeight +=
                (2d * ProcExpansionPaddingPx)
                + expandedRows.Sum(row => row.HeightPx)
                + (Math.Max(0, expandedRows.Count - 1) * ProcExpansionSpacingPx);
        }

        PortLayoutHeightOverride = _procChildrenExpanded && HasProcChildren ? baseHeight : null;

        if (
            Size == null
            || Math.Abs(Size.Width - width) > double.Epsilon
            || Math.Abs(Size.Height - expandedHeight) > double.Epsilon
        )
        {
            Size = new Size(width, expandedHeight);
        }
    }

    internal IReadOnlyList<ProcDisplayRowLayout> GetExpandedProcRowLayouts()
    {
        if (!_procChildrenExpanded || !HasProcChildren)
            return [];

        var rowTopPx = (double)Shared.Shared.CardHeight + ProcExpansionPaddingPx;
        var rows = new List<ProcDisplayRowLayout>();
        foreach (var procNode in EnumerateProcNodes())
        {
            var rowHeightPx = GetExpandedProcRowHeight(procNode);
            rows.Add(new ProcDisplayRowLayout(procNode, rowTopPx, rowHeightPx));
            rowTopPx += rowHeightPx + ProcExpansionSpacingPx;
        }

        return rows;
    }

    internal double GetExpandedProcRowHeight(CapnpFbpComponentModel procNode)
    {
        ArgumentNullException.ThrowIfNull(procNode);

        var maxVisiblePorts = Math.Max(
            CountVisibleProcPorts(procNode, CapnpFbpPortModel.PortType.In),
            CountVisibleProcPorts(procNode, CapnpFbpPortModel.PortType.Out)
        );

        return maxVisiblePorts <= 1
            ? ProcRowMinHeightPx
            : Math.Max(
                ProcRowMinHeightPx,
                (2d * ProcRowVerticalPaddingPx)
                + CapnpFbpPortLayout.PortSizePx
                + ((maxVisiblePorts - 1) * ProcRowPortSpacingPx)
            );
    }

    internal static double GetExpandedProcPortCenterOffset(
        int portIndex,
        int portCount,
        double rowHeightPx
    )
    {
        if (portCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(portCount));
        if (portIndex < 0 || portIndex >= portCount)
            throw new ArgumentOutOfRangeException(nameof(portIndex));

        if (portCount == 1)
            return rowHeightPx / 2d;

        var centeredSpanPx =
            CapnpFbpPortLayout.PortSizePx + ((portCount - 1) * ProcRowPortSpacingPx);
        var firstCenterPx =
            ((rowHeightPx - centeredSpanPx) / 2d) + (CapnpFbpPortLayout.PortSizePx / 2d);
        return firstCenterPx + (portIndex * ProcRowPortSpacingPx);
    }

    private static int CountVisibleProcPorts(
        CapnpFbpComponentModel procNode,
        CapnpFbpPortModel.PortType type
    )
    {
        return procNode.Ports
            .OfType<CapnpFbpPortModel>()
            .Count(port => port.ThePortType == type && port.GetCountedLinksForUi().Any());
    }

    private static bool HasActiveLifecycle(Model node) =>
        node switch
        {
            CapnpFbpComponentModel component => component.DisplayLifecycleState
                    is not ComponentLifecycleState.Idle
                || component.AnyProcRuntimeAttached,
            CapnpFbpViewComponentModel view =>
                view.LifecycleState is not ComponentLifecycleState.Idle || view.ProcessStarted,
            CapnpFbpIipComponentModel iip => iip.LifecycleState is not ComponentLifecycleState.Idle,
            _ => false,
        };

    private async Task AddProcChildIncrementallyAsync(
        int displayIndex,
        IReadOnlyCollection<RememberCapnpPortsLinkModel> incomingLinks,
        IReadOnlyCollection<RememberCapnpPortsLinkModel> outgoingLinks
    )
    {
        var context = CreateProcChildContext(displayIndex, incomingLinks, outgoingLinks);
        if (context == null)
            return;

        _procChildContexts.Add(context);

        foreach (var incomingLink in GetProcChildIncomingLinks(context))
            await Shared.Shared.ConnectLinkToRunningProcessesAsync(incomingLink);

        if (LifecycleState == ComponentLifecycleState.Running && Editor?.ConnectionManager != null)
            await context.Node.StartProcess(Editor.ConnectionManager);
    }

    private async Task RemoveLastProcChildIncrementallyAsync()
    {
        if (_procChildContexts.Count == 0)
            return;

        var lastIndex = _procChildContexts.Count - 1;
        var context = _procChildContexts[lastIndex];
        var showStoppingState = context.Node.CanStop;

        async Task RemoveChildCoreAsync()
        {
            _procChildContexts.RemoveAt(lastIndex);

            var incomingLinks = GetProcChildIncomingLinks(context);
            var outgoingLinks = GetProcChildOutgoingLinks(context);

            foreach (var incomingLink in incomingLinks)
                await incomingLink.DisconnectProcessOutPortAsync();

            foreach (var outgoingLink in outgoingLinks)
                await outgoingLink.DisconnectProcessOutPortAsync();

            foreach (var link in outgoingLinks)
                await link.DisconnectWriterAsync();

            foreach (var link in incomingLinks)
                await link.DisconnectWriterAsync();

            foreach (var link in context.Links)
            {
                link.DetachFromPorts();
                RefreshInternalProcLinkRemoval(link);
            }

            if (_procChildContexts.Count == 0)
                _procChildrenExpanded = false;

            await context.Node.DisposeAsync();
        }

        if (showStoppingState)
            await RunWithProcAdjustmentStateAsync(ComponentLifecycleState.Stopping, RemoveChildCoreAsync);
        else
            await RemoveChildCoreAsync();
    }

    private static List<RememberCapnpPortsLinkModel> GetProcChildIncomingLinks(
        ProcChildContext context
    ) =>
        context.Links
            .Where(link => ReferenceEquals(link.InPortModel.Parent, context.Node))
            .ToList();

    private static List<RememberCapnpPortsLinkModel> GetProcChildOutgoingLinks(
        ProcChildContext context
    ) =>
        context.Links
            .Where(link => ReferenceEquals(link.OutPortModel.Parent, context.Node))
            .ToList();

    private static void RefreshInternalProcLinkRemoval(RememberCapnpPortsLinkModel link)
    {
        link.OutPortModel.SyncLinkedWriterState();
        link.OutPortModel.SyncVisibility();
        link.OutPortModel.SyncProcessConnectionState();
        link.InPortModel.SyncVisibility();
        link.OutPortModel.Refresh();
        link.InPortModel.Refresh();
        link.OutPortModel.Parent?.RefreshAll();
        link.InPortModel.Parent?.RefreshAll();
    }

    private async Task RunWithProcAdjustmentStateAsync(
        ComponentLifecycleState state,
        Func<Task> action
    )
    {
        var previousState = _procAdjustmentState;
        var wasAdjustmentInFlight = _procCountAdjustmentInFlight;
        _procCountAdjustmentInFlight = true;
        _procAdjustmentState = state;
        RefreshAll();
        RefreshLinks();

        try
        {
            await action();
        }
        finally
        {
            _procAdjustmentState = previousState;
            _procCountAdjustmentInFlight = wasAdjustmentInFlight;
            RefreshAll();
            RefreshLinks();
        }
    }

    private sealed class ProcChildContext(CapnpFbpComponentModel node)
    {
        public CapnpFbpComponentModel Node { get; } = node;
        public List<RememberCapnpPortsLinkModel> Links { get; } = [];
    }

    internal readonly record struct ProcDisplayRowLayout(
        CapnpFbpComponentModel Node,
        double TopPx,
        double HeightPx
    );
}
