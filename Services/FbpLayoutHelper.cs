using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;

namespace BlazorDrawFBP.Services;

public static class FbpLayoutHelper
{
    public const int ViewNodeWidth = 350;
    public const int ViewNodeHeight = 200;

    public static void SetDefaultComponentSize(NodeModel node)
    {
        node.Size = new Size(Shared.Shared.CardWidth, Shared.Shared.CardHeight);
    }

    public static void RefreshPortLayout(NodeModel node)
    {
        foreach (var relatedNode in GetNodesAffectingPortLayout(node))
        {
            CapnpFbpPortLayout.Apply(relatedNode, false);
            relatedNode.RefreshAll();
        }
    }

    public static void RefreshPortLayout(BaseLinkModel link)
    {
        foreach (var node in GetNodesAffectingPortLayout(link))
        {
            CapnpFbpPortLayout.Apply(node, false);
            node.RefreshAll();
        }
    }

    public static void QueueProcStructureSyncForLink(BaseLinkModel link, bool loadingFlow = false)
    {
        if (
            loadingFlow
            || link is not RememberCapnpPortsLinkModel { IsInternalProcLink: false } rememberedLink
        )
            return;

        if (rememberedLink.OutPortModel.Parent is CapnpFbpComponentModel sourceComponent)
            sourceComponent.QueueProcSyncForLinkChange();
        if (rememberedLink.InPortModel.Parent is CapnpFbpComponentModel targetComponent)
            targetComponent.QueueProcSyncForLinkChange();
    }

    public static async Task SyncProcStructureAsync(IEnumerable<CapnpFbpComponentModel> nodes)
    {
        foreach (
            var node in nodes.Where(node => node != null && !node.IsInternalProcChild).Distinct()
        )
            await node.EnsureProcStructureSynchronizedAsync();
    }

    public static IEnumerable<NodeModel> GetNodesAffectingPortLayout(NodeModel node)
    {
        var nodes = new List<NodeModel> { node };
        foreach (var link in Shared.Shared.AttachedLinks(node))
            nodes.AddRange(GetNodesAffectingPortLayout(link));
        return nodes.Distinct();
    }

    public static IEnumerable<NodeModel> GetNodesAffectingPortLayout(BaseLinkModel link)
    {
        var nodes = new List<NodeModel>();
        if (link.Source.Model is PortModel { Parent: { } sourceParent })
            nodes.Add(sourceParent);
        if (link.Target.Model is PortModel { Parent: { } targetParent })
            nodes.Add(targetParent);
        return nodes.Distinct();
    }
}
