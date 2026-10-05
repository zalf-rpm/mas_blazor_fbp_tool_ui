using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Models;

public class ChannelLinkLabelModel : LinkLabelModel, IDisposable
{
    public const int CompactInteractionCanvasWidth = 160;
    public const int CompactInteractionCanvasHeight = 120;
    private readonly CapnpFbpInPortModel _inPort;

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
        Point? offset = null
    )
        : base(parent, content, distance, offset)
    {
        _inPort = parent.InPortModel;
    }

    public bool ShowWidget => _inPort.Channel != null;
    public bool ShowStats => ShowWidget;
    public bool IsExpanded { get; private set; }
    public bool IsResizingBuffer { get; private set; }

    /// <summary>
    /// Where the label sits on the link, in diagram coordinates. Set while rendering the link, so the
    /// expanded card (rendered above the nodes by the controls layer) can follow it.
    /// </summary>
    public Point? CanvasPosition { get; private set; }

    public Channel<IP>.StatsCallback.Stats Stats => LinkModel.Stats;
    public ulong MessagesInBuffer => Stats.NoOfIpsInQueue;
    public double QueueFillPercent =>
        BufferSize == 0 ? 0 : Math.Min(100d, (double)MessagesInBuffer / BufferSize * 100d);
    public string BufferSummaryText => $"{MessagesInBuffer} / {BufferSize}";
    public string QueueFillText => $"{QueueFillPercent:0}% full";

    public RememberCapnpPortsLinkModel LinkModel => (RememberCapnpPortsLinkModel)Parent;
    public ulong BufferSize => _inPort.ChannelBufferSize;
    public bool CanResizeBuffer => _inPort.Channel != null;

    public string ConnectionLabel =>
        $"{FormatPortLabel(LinkModel.OutPortModel)} -> {FormatPortLabel(LinkModel.InPortModel)}";

    public void Dispose()
    {
        Console.WriteLine("ChannelLinkLabelModel::Dispose()");
    }

    public void UpdateCanvasPosition(Point position)
    {
        if (
            CanvasPosition is { } current
            && Math.Abs(current.X - position.X) < 0.01
            && Math.Abs(current.Y - position.Y) < 0.01
        )
            return;

        CanvasPosition = position;
        if (IsExpanded)
            Refresh();
    }

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

        LinkModel.Refresh();
    }

    private static string FormatPortLabel(CapnpFbpPortModel port)
    {
        return $"{Shared.Shared.NodeNameFromPort(port)}.{port.Name}";
    }

    private static ulong SaturatingAdd(ulong current, ulong delta)
    {
        return delta > ulong.MaxValue - current ? ulong.MaxValue : current + delta;
    }
}
