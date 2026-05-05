using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using Mas.Schema.Fbp;
using Mas.Schema.Management;

namespace BlazorDrawFBP.Models;

public class ChannelLinkLabelModel : LinkLabelModel, IDisposable
{
    public const int CompactInteractionCanvasWidth = 160;
    public const int CompactInteractionCanvasHeight = 120;
    public const int ExpandedInteractionCanvasWidth = 320;
    public const int ExpandedInteractionCanvasHeight = 320;

    // public ChannelLinkLabelModel(
    //     RememberCapnpPortsLinkModel parent,
    //     string id,
    //     string content,
    //     double? distance = null,
    //     Point offset = null
    // )
    //     : base(parent, id, content, distance, offset)
    // {
    //     _inPort = parent.InPortModel;
    // }

    public ChannelLinkLabelModel(
        RememberCapnpPortsLinkModel parent,
        string content,
        double? distance = null,
        Point offset = null
    )
        : base(parent, content, distance, offset)
    {
        _inPort = parent.InPortModel;
    }

    public bool ShowWidget => _inPort.Channel != null;
    public bool ShowStats => ShowWidget;
    public bool IsExpanded { get; private set; }
    public bool IsResizingBuffer { get; private set; }
    private readonly CapnpFbpInPortModel _inPort;

    public RememberCapnpPortsLinkModel LinkModel => Parent as RememberCapnpPortsLinkModel;
    public ulong BufferSize => _inPort.ChannelBufferSize;
    public bool CanResizeBuffer => _inPort.Channel != null;
    public string ConnectionLabel =>
        $"{FormatPortLabel(LinkModel?.OutPortModel)} -> {FormatPortLabel(LinkModel?.InPortModel)}";

    public void Expand()
    {
        if (IsExpanded)
            return;

        IsExpanded = true;
        RefreshLabel();
    }

    public void Collapse()
    {
        if (!IsExpanded)
            return;

        IsExpanded = false;
        RefreshLabel();
    }

    public void ResetExpandedState()
    {
        IsExpanded = false;
    }

    public async Task IncreaseBufferSizeAsync(ulong delta)
    {
        await ResizeBufferSizeAsync(SaturatingAdd(BufferSize, delta));
    }

    public async Task DoubleBufferSizeAsync()
    {
        var nextSize = BufferSize > ulong.MaxValue / 2 ? ulong.MaxValue : BufferSize * 2;
        await ResizeBufferSizeAsync(nextSize);
    }

    public void Dispose()
    {
        Console.WriteLine("ChannelLinkLabelModel::Dispose()");
    }

    private async Task ResizeBufferSizeAsync(ulong size)
    {
        if (!CanResizeBuffer)
            return;

        var normalizedSize = size == 0 ? 1 : size;
        if (normalizedSize == BufferSize)
            return;

        IsResizingBuffer = true;
        RefreshLabel();
        try
        {
            await _inPort.SetChannelBufferSizeAsync(normalizedSize);
        }
        finally
        {
            IsResizingBuffer = false;
            RefreshLabel();
        }
    }

    private void RefreshLabel()
    {
        if (!ShowWidget)
            IsExpanded = false;

        LinkModel?.Refresh();
    }

    private static string FormatPortLabel(CapnpFbpPortModel port)
    {
        if (port == null)
            return "unknown";

        return $"{BlazorDrawFBP.Shared.Shared.NodeNameFromPort(port)}.{port.Name}";
    }

    private static ulong SaturatingAdd(ulong current, ulong delta) =>
        delta > ulong.MaxValue - current ? ulong.MaxValue : current + delta;
}
