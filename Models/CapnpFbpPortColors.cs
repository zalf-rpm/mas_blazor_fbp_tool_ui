using System;
using System.Collections.Generic;
using System.Linq;
using ProcessSchema = Mas.Schema.Fbp.Process;

namespace BlazorDrawFBP.Models;

public static class CapnpFbpPortColors
{
    public const string DefaultColor = "black";
    public const string ReadyColor = "#1ac12e";
    public const string PendingColor = "#ff0000";
    public const string TransitionColor = "#E69F00";
    public const string WaitingInputColor = "#2563eb";
    public const string WaitingOutputColor = "#7c3aed";
    public const string ClosingColor = "#6b7280";

    public static string ResolvePortIconColor(CapnpFbpPortModel port)
    {
        var shellColor = ResolvePortShellColor(port);
        if (string.IsNullOrWhiteSpace(shellColor))
            return DefaultColor;

        if (string.Equals(shellColor, PendingColor, StringComparison.OrdinalIgnoreCase))
            return PendingColor;

        if (string.Equals(shellColor, ReadyColor, StringComparison.OrdinalIgnoreCase))
            return ReadyColor;

        return DefaultColor;
    }

    public static string ResolvePortShellColor(CapnpFbpPortModel port)
    {
        if (port.ConnectedChannelCount == 0)
            return null;

        var linkColors = GetLinkedPortColors(port);
        return linkColors.Count > 0 ? PrioritizeColors(linkColors) : ResolveLinkedPortFallbackColor(port);
    }

    public static string ResolveLinkColor(RememberCapnpPortsLinkModel link)
    {
        return HasReadyChannel(link) ? ReadyColor : PendingColor;
    }

    public static string ResolveLifecycleFrameColor(ComponentLifecycleState state)
    {
        return state switch
        {
            ComponentLifecycleState.Starting or ComponentLifecycleState.Stopping => TransitionColor,
            ComponentLifecycleState.Running => ReadyColor,
            ComponentLifecycleState.Failed => PendingColor,
            _ => DefaultColor,
        };
    }

    public static string ResolveComponentFrameColor(CapnpFbpComponentModel node)
    {
        return ResolveLifecycleFrameColor(node.DisplayLifecycleState);
    }

    public static string ResolveActivityColor(ProcessSchema.ActivityState activityState)
    {
        return activityState switch
        {
            ProcessSchema.ActivityState.waitingInput => WaitingInputColor,
            ProcessSchema.ActivityState.processing => ReadyColor,
            ProcessSchema.ActivityState.waitingOutput => WaitingOutputColor,
            ProcessSchema.ActivityState.closing => ClosingColor,
            _ => DefaultColor,
        };
    }

    public static string ResolveActiveFrameColor(bool isReady)
    {
        return isReady ? ReadyColor : PendingColor;
    }

    public static void ApplyLinkColor(RememberCapnpPortsLinkModel link)
    {
        var color = ResolveLinkColor(link);
        if (!string.Equals(link.Color, color, StringComparison.OrdinalIgnoreCase))
            link.Color = color;

        link.Refresh();
        link.OutPortModel.Refresh();
        link.InPortModel.Refresh();
    }

    private static bool HasReadyChannel(RememberCapnpPortsLinkModel link)
    {
        var outPort = link.OutPortModel;
        var inPort = link.InPortModel;
        return inPort.Channel != null
            || inPort.Reader != null
            || link.Writer != null
            || link.WriterSturdyRef != null
            || inPort.Connected
            || outPort.Connected;
    }

    private static string ResolveLinkedPortFallbackColor(CapnpFbpPortModel port)
    {
        return port switch
        {
            CapnpFbpInPortModel { Channel: not null } => ReadyColor,
            CapnpFbpInPortModel { Reader: not null } => ReadyColor,
            CapnpFbpOutPortModel { Writer: not null } => ReadyColor,
            { Connected: true } => ReadyColor,
            _ => PendingColor,
        };
    }

    private static List<string> GetLinkedPortColors(CapnpFbpPortModel port)
    {
        return port
            .GetCountedLinksForUi()
            .Select(link => NormalizeColor(link.Color))
            .Where(static color => color != null)
            .Cast<string>()
            .ToList();
    }

    private static string PrioritizeColors(IReadOnlyCollection<string> colors)
    {
        if (
            colors.Any(color =>
                string.Equals(color, PendingColor, StringComparison.OrdinalIgnoreCase)
            )
        )
            return PendingColor;

        if (
            colors.Any(color =>
                string.Equals(color, ReadyColor, StringComparison.OrdinalIgnoreCase)
            )
        )
            return ReadyColor;

        if (
            colors.Any(color =>
                string.Equals(color, DefaultColor, StringComparison.OrdinalIgnoreCase)
            )
        )
            return DefaultColor;

        return colors.First();
    }

    private static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;

        if (string.Equals(color, "#111827", StringComparison.OrdinalIgnoreCase))
            return DefaultColor;

        return color;
    }
}
