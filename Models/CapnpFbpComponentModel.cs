using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Pages;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Newtonsoft.Json.Linq;
using Process = Mas.Schema.Fbp.Process;

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
    public CapnpFbpComponentModel(Point position = null)
        : base(position) { }

    public CapnpFbpComponentModel(string id, Point position = null)
        : base(id, position) { }

    // public BlazorDispatcher Dispatcher { get; set; }

    public Editor Editor { get; set; }
    public string ComponentId { get; set; }
    public string ComponentServiceId { get; set; }
    public string ComponentName { get; set; }
    public string ProcessName { get; set; }
    public string ShortDescription { get; set; }
    public string Cmd { get; set; }
    public int InParallelCount { get; set; } = 1;
    public bool Editable { get; set; } = true;
    public static int ProcessNo { get; set; } = 0;
    public string DefaultConfigString { get; set; }

    public string ConfigString { get; set; }

    public int DisplayNoOfConfigLines { get; set; } = 3;

    public bool ProcessStarted { get; protected set; }
    public ComponentLifecycleState LifecycleState { get; private set; } = ComponentLifecycleState.Idle;
    public string LifecycleError { get; private set; }

    public bool CanStart =>
        LifecycleState is ComponentLifecycleState.Idle
            or ComponentLifecycleState.Failed
            or ComponentLifecycleState.Closed;
    public bool CanStop => LifecycleState == ComponentLifecycleState.Running;
    public bool IsLifecycleBusy =>
        LifecycleState is ComponentLifecycleState.Starting or ComponentLifecycleState.Stopping;

    public string LifecycleLabel => LifecycleState.ToString();

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
    }

    protected virtual Task ShutdownForComponentServiceSwitchAsync() => ResetExecution();

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
        await Shared.Shared.RestoreDefaultPortVisibilityOfAttachedComponent(
            this,
            Editor.Diagram,
            this
        );
        await DisposeStandardPorts();
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
}
