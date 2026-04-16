using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Pages;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Persistence;
using Newtonsoft.Json.Linq;
using Tomlyn;
using Process = Mas.Schema.Fbp.Process;

namespace BlazorDrawFBP.Models;

public class CapnpFbpComponentModel : NodeModel, IDisposable
{
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
            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: override StartProcess!"
        );
        ProcessStarted = false;
    }

    public virtual async Task StopProcess(ConnectionManager conMan)
    {
        Console.WriteLine(
            $"T{Thread.CurrentThread.ManagedThreadId} {ProcessName}: override StopProcess"
        );
        ProcessStarted = false;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        Console.WriteLine($"{ProcessName}: CapnpFbpComponentModel::Dispose");
        if (!disposing)
            return;
        foreach (var baseLinkModel in Links)
        {
            Shared.Shared.RestoreDefaultPortVisibility(Editor.Diagram, baseLinkModel);
        }
        DisposeStandardPorts();
    }

    private void DisposeStandardPorts()
    {
        Console.WriteLine(
            $"{ProcessName}: CapnpFbpComponentModel::FreeRemoteChannelsAttachedToPorts"
        );
        foreach (var port in Ports)
        {
            if (port is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
