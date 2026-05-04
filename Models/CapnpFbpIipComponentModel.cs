using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Pages;
using Capnp;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Microsoft.AspNetCore.Components;
using Exception = System.Exception;

namespace BlazorDrawFBP.Models;

public class CapnpFbpIipComponentModel : NodeModel, IAsyncDisposable
{
    public CapnpFbpIipComponentModel(Point position = null)
        : base(position) { }

    // public BlazorDispatcher Dispatcher { get; set; }

    public Editor Editor { get; set; }

    public string ComponentId { get; set; }

    public string ShortDescription { get; set; }
    public string Content { get; set; }

    public (bool, StructuredText.Type) PlainTextOrContentType { get; set; } =
        (true, StructuredText.Type.unstructured);

    public int DisplayNoOfLines { get; set; } = 3;
    public ComponentLifecycleState LifecycleState { get; private set; } =
        ComponentLifecycleState.Stopped;
    public string LifecycleError { get; private set; }
    public bool CanStart => !IsLifecycleBusy;
    public bool CanStop => false;
    public bool IsLifecycleBusy =>
        LifecycleState is ComponentLifecycleState.Starting or ComponentLifecycleState.Stopping;
    public ComponentLifecycleState DisplayLifecycleState => LifecycleState switch
    {
        ComponentLifecycleState.Faulted => ComponentLifecycleState.Faulted,
        ComponentLifecycleState.Starting => ComponentLifecycleState.Starting,
        _ => IsConnectedToChannel ? ComponentLifecycleState.Running : ComponentLifecycleState.Stopped,
    };
    public string LifecycleLabel => DisplayLifecycleState switch
    {
        ComponentLifecycleState.Starting => "Sending",
        ComponentLifecycleState.Running => "Ready",
        ComponentLifecycleState.Faulted => "Error",
        _ => "Disconnected",
    };

    private CancellationTokenSource _cancellationTokenSource;
    private Task _iipTask;

    private bool IsConnectedToChannel =>
        Ports.OfType<CapnpFbpOutPortModel>()
            .Any(port => port.Writer != null || port.WriterSturdyRef != null || port.Connected);

    public async Task SendIip(ConnectionManager conMan)
    {
        if (!CanStart)
            return;

        if (Editor.CurrentChannelStarterService == null)
        {
            SetLifecycleFault(
                new InvalidOperationException("No channel service connected."),
                refresh: true
            );
            return;
        }

        SetLifecycleState(ComponentLifecycleState.Starting, refresh: true);
        try
        {
            Console.WriteLine($"T{Thread.CurrentThread.ManagedThreadId} IIP: SendIip");

            await CancelAndDisposeIipTaskAsync();
            _cancellationTokenSource = new CancellationTokenSource();
            var cancelToken = _cancellationTokenSource.Token;

            Debug.Assert(Shared.Shared.AttachedLinkCount(this) < 2);
            var iipPort = Shared.Shared
                .AttachedLinks(this)
                .OfType<RememberCapnpPortsLinkModel>()
                .Select(link => link.OutPortModel)
                .OfType<CapnpFbpOutPortModel>()
                .FirstOrDefault(port => port.Parent == this);

            if (iipPort == null)
            {
                SetLifecycleState(ComponentLifecycleState.Stopped, refresh: true);
                return;
            }

            _iipTask = SendIipToPortAsync(iipPort, cancelToken);
            await _iipTask;
            SetLifecycleState(
                IsConnectedToChannel
                    ? ComponentLifecycleState.Running
                    : ComponentLifecycleState.Stopped,
                refresh: true
            );

            RefreshAll();
            RefreshLinks();
        }
        catch (OperationCanceledException)
        {
            SetLifecycleState(ComponentLifecycleState.Stopped, refresh: true);
        }
        catch (Exception e)
        {
            Console.WriteLine($"T{Environment.CurrentManagedThreadId} IIP: Caught exception: " + e);
            SetLifecycleFault(e, refresh: true);
        }
        finally
        {
            await CancelAndDisposeIipTaskAsync();
        }
    }

    public async Task ResetExecution()
    {
        await CancelAndDisposeIipTaskAsync();
        SetLifecycleState(ComponentLifecycleState.Stopped, refresh: true);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        Console.WriteLine("CapnpFbpIipModel::Disposing");

        await ResetExecution();
        Shared.Shared.RestoreDefaultPortVisibilityOfAttachedComponent(this, Editor.Diagram);

        foreach (var port in Ports)
        {
            if (port is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
        }
    }

    private async Task SendIipToPortAsync(
        CapnpFbpOutPortModel iipPort,
        CancellationToken cancelToken
    )
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} IIP: writing IIP: '{Content}' to channel"
        );

        var retryCount = 5;
        while (retryCount > 0 && iipPort.Writer == null)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} IIP: waiting for connected channel. Retrying {retryCount} more times."
            );
            await Task.Delay(1000, cancelToken);
            retryCount--;
        }

        if (iipPort.Writer == null)
        {
            throw new InvalidOperationException(
                $"IIP '{ComponentId ?? "unknown"}' could not connect to an output channel."
            );
        }

        await iipPort.Writer.Write(
            new Channel<IP>.Msg
            {
                Value = new IP
                {
                    Content = PlainTextOrContentType.Item1
                        ? Content
                        : new StructuredText
                        {
                            TheType = PlainTextOrContentType.Item2,
                            Value = Content,
                        },
                },
            },
            cancelToken
        );
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} IIP: wrote IIP: '{Content}' to channel"
        );
    }

    private async Task CancelAndDisposeIipTaskAsync()
    {
        if (_cancellationTokenSource != null)
            await _cancellationTokenSource.CancelAsync();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        if (_iipTask != null)
        {
            await _iipTask.ContinueWith(t => t.Dispose());
            _iipTask = null;
        }
    }

    private void SetLifecycleState(
        ComponentLifecycleState state,
        string error = null,
        bool refresh = false
    )
    {
        LifecycleState = state;
        LifecycleError = state == ComponentLifecycleState.Faulted ? error ?? LifecycleError : null;

        if (refresh)
        {
            RefreshAll();
            RefreshLinks();
        }
    }

    private void SetLifecycleFault(Exception exception, bool refresh = false)
    {
        Console.Error.WriteLine(exception);
        SetLifecycleState(ComponentLifecycleState.Faulted, exception.Message, refresh);
    }
}
