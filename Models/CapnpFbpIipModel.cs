using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorDrawFBP.Pages;
using Capnp;
using Capnp.Rpc;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Microsoft.AspNetCore.Components;
using Exception = System.Exception;

namespace BlazorDrawFBP.Models;

using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

public class CapnpFbpIipModel : NodeModel, IDisposable
{
    public CapnpFbpIipModel(Point position = null)
        : base(position) { }

    public Editor Editor { get; set; }

    public string ComponentId { get; set; }

    public string ShortDescription { get; set; }
    public string Content { get; set; }

    public (bool, Mas.Schema.Common.StructuredText.Type) PlainTextOrContentType { get; set; } =
        (true, StructuredText.Type.unstructured);

    public int DisplayNoOfLines { get; set; } = 3;

    private CancellationTokenSource _cancellationTokenSource;
    private Task _iipTask;

    public async Task SendIip(ConnectionManager conMan)
    {
        try
        {
            if (Editor.CurrentChannelStarterService == null)
            {
                return;
            }

            Console.WriteLine($"T{Thread.CurrentThread.ManagedThreadId} IIP: SendIip");

            _cancellationTokenSource = new CancellationTokenSource();
            var cancelToken = _cancellationTokenSource.Token;

            // collect SRs from IN and OUT ports and for IIPs send it into the channel
            Debug.Assert(Links.Count < 2);
            foreach (var pl in Links)
            {
                if (pl is not RememberCapnpPortsLinkModel rcplm)
                    continue;
                if (rcplm.OutPortModel is not CapnpFbpOutPortModel iippm)
                    continue;

                _iipTask = Task.Run(
                    async () =>
                    {
                        Console.WriteLine(
                            $"T{Thread.CurrentThread.ManagedThreadId} IIP: async code for automatically writing IIP: '{Content}' to channel"
                        );

                        var retryCount = 5;
                        while (retryCount > 0 && iippm.Writer == null)
                        {
                            Console.WriteLine(
                                $"T{Thread.CurrentThread.ManagedThreadId} IIP: waiting for connected channel. Retrying {retryCount} more times."
                            );
                            await Task.Delay(1000);
                            retryCount--;
                        }
                        if (retryCount == 0)
                            return;

                        Console.WriteLine(
                            $"T{Thread.CurrentThread.ManagedThreadId} IIP: writing IIP: '{Content}' to channel"
                        );
                        await iippm.Writer.Write(
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
                    },
                    cancelToken
                );
            }

            RefreshAll();
            RefreshLinks();
        }
        catch (Exception e)
        {
            Console.WriteLine($"T{Environment.CurrentManagedThreadId} IIP: Caught exception: " + e);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;
        Console.WriteLine($"CapnpFbpIipModel::Disposing");

        //cancel task
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _iipTask?.Dispose();
        _iipTask = null;

        foreach (var baseLinkModel in Links)
        {
            Shared.Shared.RestoreDefaultPortVisibility(Editor.Diagram, baseLinkModel);
        }

        foreach (var port in Ports)
        {
            if (port is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
