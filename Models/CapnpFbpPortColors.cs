using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorDrawFBP.Models;

public static class CapnpFbpPortColors
{
    public const string DefaultColor = "black";
    public const string ReadyColor = "#1ac12e";
    public const string PendingColor = "#ff0000";
    public const string TransitionColor = "#E69F00";

    public static string ResolvePortIconColor(CapnpFbpPortModel port)
    {
        if (port.Links.Count == 0)
            return DefaultColor;

        var linkColors = port
            .Links
            .OfType<RememberCapnpPortsLinkModel>()
            .Select(link => NormalizeColor(link.Color))
            .Where(static color => color != null)
            .Cast<string>()
            .ToList();

        return linkColors.Count > 0 ? PrioritizeColors(linkColors) : ResolveLinkedPortFallbackColor(port);
    }

    public static string ResolveLinkColor(CapnpFbpOutPortModel outPort, CapnpFbpInPortModel inPort)
    {
        return HasReadyChannel(outPort, inPort) ? ReadyColor : PendingColor;
    }

    public static string ResolveLifecycleFrameColor(ComponentLifecycleState state)
    {
        return state switch
        {
            ComponentLifecycleState.Starting or ComponentLifecycleState.Stopping => TransitionColor,
            ComponentLifecycleState.Running => ReadyColor,
            ComponentLifecycleState.Faulted => PendingColor,
            _ => DefaultColor,
        };
    }

    public static string ResolveComponentFrameColor(CapnpFbpComponentModel node)
    {
        return ResolveLifecycleFrameColor(node.LifecycleState);
    }

    public static string ResolveActiveFrameColor(bool isReady)
    {
        return isReady ? ReadyColor : PendingColor;
    }

    public static void ApplyLinkColor(RememberCapnpPortsLinkModel link)
    {
        var color = ResolveLinkColor(link.OutPortModel, link.InPortModel);
        if (!string.Equals(link.Color, color, StringComparison.OrdinalIgnoreCase))
            link.Color = color;

        link.Refresh();
        link.OutPortModel.Refresh();
        link.InPortModel.Refresh();
    }

    private static bool HasReadyChannel(CapnpFbpOutPortModel outPort, CapnpFbpInPortModel inPort)
    {
        return inPort.Channel != null
            || inPort.Reader != null
            || outPort.Writer != null
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
