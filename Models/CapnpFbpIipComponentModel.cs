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
        ComponentLifecycleState.Idle;
    public string LifecycleError { get; private set; }
    public bool CanStart => !IsLifecycleBusy;
    public bool CanStop => false;
    public bool IsLifecycleBusy =>
        LifecycleState is ComponentLifecycleState.Starting or ComponentLifecycleState.Stopping;
    public ComponentLifecycleState DisplayLifecycleState => LifecycleState switch
    {
        ComponentLifecycleState.Failed => ComponentLifecycleState.Failed,
        ComponentLifecycleState.Starting => ComponentLifecycleState.Starting,
        _ => IsConnectedToChannel ? ComponentLifecycleState.Running : ComponentLifecycleState.Idle,
    };
    public string LifecycleLabel => DisplayLifecycleState.ToString();

    private CancellationTokenSource _cancellationTokenSource;
    private Task _iipTask;

    private bool IsConnectedToChannel =>
        Shared.Shared.AttachedLinks(this)
            .OfType<RememberCapnpPortsLinkModel>()
            .Any(link =>
                link.OutPortModel.Parent == this
                && (
                    link.Writer != null
                    || link.WriterSturdyRef != null
                    || link.OutPortModel.Connected
                )
            );

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
            var iipLink = Shared.Shared
                .AttachedLinks(this)
                .OfType<RememberCapnpPortsLinkModel>()
                .FirstOrDefault(link => link.OutPortModel.Parent == this);

            if (iipLink == null)
            {
                SetLifecycleState(ComponentLifecycleState.Idle, refresh: true);
                return;
            }

            _iipTask = SendIipToPortAsync(iipLink, cancelToken);
            await _iipTask;
            SetLifecycleState(
                IsConnectedToChannel
                    ? ComponentLifecycleState.Running
                    : ComponentLifecycleState.Idle,
                refresh: true
            );

            RefreshAll();
            RefreshLinks();
        }
        catch (OperationCanceledException)
        {
            SetLifecycleState(ComponentLifecycleState.Idle, refresh: true);
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
        SetLifecycleState(ComponentLifecycleState.Idle, refresh: true);
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
        await Shared.Shared.RestoreDefaultPortVisibilityOfAttachedComponent(
            this,
            Editor.Diagram,
            this
        );

        foreach (var port in Ports)
        {
            if (port is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
        }
    }

    private async Task SendIipToPortAsync(
        RememberCapnpPortsLinkModel iipLink,
        CancellationToken cancelToken
    )
    {
        Console.WriteLine(
            $"T{Environment.CurrentManagedThreadId} IIP: writing IIP: '{Content}' to channel"
        );

        if (iipLink.RetrieveWriterFromChannelTask != null)
            await iipLink.RetrieveWriterFromChannelTask;

        var retryCount = 5;
        while (retryCount > 0 && iipLink.Writer == null)
        {
            Console.WriteLine(
                $"T{Environment.CurrentManagedThreadId} IIP: waiting for connected channel. Retrying {retryCount} more times."
            );
            await Task.Delay(1000, cancelToken);
            retryCount--;
        }

        if (iipLink.Writer == null)
        {
            throw new InvalidOperationException(
                $"IIP '{ComponentId ?? "unknown"}' could not connect to an output channel."
            );
        }

        await iipLink.Writer.Write(
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
        LifecycleError = state == ComponentLifecycleState.Failed ? error ?? LifecycleError : null;

        if (refresh)
        {
            RefreshAll();
            RefreshLinks();
        }
    }

    private void SetLifecycleFault(Exception exception, bool refresh = false)
    {
        Console.Error.WriteLine(exception);
        SetLifecycleState(ComponentLifecycleState.Failed, exception.Message, refresh);
    }
}
