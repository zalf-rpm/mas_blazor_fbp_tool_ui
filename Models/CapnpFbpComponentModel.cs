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
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted,
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
    public ComponentLifecycleState LifecycleState { get; private set; } = ComponentLifecycleState.Stopped;
    public string LifecycleError { get; private set; }

    public bool CanStart => LifecycleState is ComponentLifecycleState.Stopped or ComponentLifecycleState.Faulted;
    public bool CanStop => LifecycleState == ComponentLifecycleState.Running;
    public bool IsLifecycleBusy =>
        LifecycleState is ComponentLifecycleState.Starting or ComponentLifecycleState.Stopping;

    public string LifecycleLabel => LifecycleState switch
    {
        ComponentLifecycleState.Stopped => "Stopped",
        ComponentLifecycleState.Starting => "Starting",
        ComponentLifecycleState.Running => "Running",
        ComponentLifecycleState.Stopping => "Stopping",
        ComponentLifecycleState.Faulted => "Error",
        _ => "Unknown",
    };

    public virtual bool RemoteProcessAttached() => false;
    public virtual bool CanEditCommandLine() => false;

    public virtual async Task StartProcess(ConnectionManager conMan)
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: override StartProcess!"
        );
        SetLifecycleState(ComponentLifecycleState.Stopped);
    }

    public virtual async Task StopProcess(ConnectionManager conMan)
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: override StopProcess"
        );
        SetLifecycleState(ComponentLifecycleState.Stopped);
    }

    public virtual Task ResetExecution()
    {
        SetLifecycleState(ComponentLifecycleState.Stopped, refresh: true);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpComponentModel::DisposeAsyncCore");
        Shared.Shared.RestoreDefaultPortVisibilityOfAttachedComponent(this, Editor.Diagram);
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
        LifecycleError = state == ComponentLifecycleState.Faulted ? error ?? LifecycleError : null;

        if (refresh)
        {
            RefreshAll();
            RefreshLinks();
        }
    }

    protected void SetLifecycleFault(Exception exception, bool refresh = false)
    {
        Console.Error.WriteLine(exception);
        SetLifecycleState(ComponentLifecycleState.Faulted, exception.Message, refresh);
    }
}
