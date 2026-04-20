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
using Tomlyn;
using Process = Mas.Schema.Fbp.Process;

namespace BlazorDrawFBP.Models;

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

    public virtual bool RemoteProcessAttached() => false;

    public virtual async Task StartProcess(ConnectionManager conMan)
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: override StartProcess!"
        );
        ProcessStarted = false;
    }

    public virtual async Task StopProcess(ConnectionManager conMan)
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} {ProcessName}: override StopProcess"
        );
        ProcessStarted = false;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpComponentModel::DisposeAsyncCore");
        Shared.Shared.RestoreDefaultPortVisibilityOfAttachedComponent(Links, Editor.Diagram);
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
}
