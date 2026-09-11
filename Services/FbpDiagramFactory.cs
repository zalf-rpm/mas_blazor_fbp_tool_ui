using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Behaviors;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.PathGenerators;
using Blazor.Diagrams.Core.Routers;
using Blazor.Diagrams.Options;
using BlazorDrawFBP.Behaviors;
using BlazorDrawFBP.Controls;
using BlazorDrawFBP.Models;

namespace BlazorDrawFBP.Services;

public class FbpDiagramFactory : IFbpDiagramFactory
{
    public BlazorDiagram CreateConfiguredDiagram(
        IFbpRuntimeService runtime,
        Action? onStructureChanged = null,
        Action? onDiagramInteracted = null,
        Func<Task>? onZoomToFit = null
    )
    {
        var options = new BlazorDiagramOptions
        {
            AllowMultiSelection = true,
            Zoom = { Enabled = true, Inverse = true },
            Links =
            {
                DefaultRouter = new NormalRouter(),
                DefaultPathGenerator = new SmoothPathGenerator(),
                Factory = (diagram, source, targetAnchor) =>
                {
                    if (source is not PortModel sourcePort)
                        throw new InvalidOperationException(
                            $"FBP links can only start from ports, got {source?.GetType().Name ?? "null"}."
                        );

                    return new LinkModel(
                        new SinglePortAnchor(sourcePort)
                        {
                            MiddleIfNoMarker = true,
                            UseShapeAndAlignment = false,
                        },
                        targetAnchor
                    );
                },
                TargetAnchorFactory = (diagram, link, model) =>
                {
                    if (model is not PortModel targetPort)
                        throw new InvalidOperationException(
                            $"FBP links can only target ports, got {model?.GetType().Name ?? "null"}."
                        );

                    return new SinglePortAnchor(targetPort)
                    {
                        MiddleIfNoMarker = true,
                        UseShapeAndAlignment = false,
                    };
                },
            },
            Groups = { Enabled = true },
        };

        var diagram = new BlazorDiagram(options);
        runtime.Diagram = diagram;

        var ksb = diagram.GetBehavior<KeyboardShortcutsBehavior>();
        ksb?.RemoveShortcut("Delete", false, false, false);
        ksb?.SetShortcut("Delete", false, true, false, KeyboardShortcutsDefaults.DeleteSelection);

        if (File.Exists("Data/default_components.json"))
            runtime.InitDefaultComponents(File.ReadAllText("Data/default_components.json"));

        RegisterComponents(diagram);
        RegisterDiagramEvents(diagram, runtime, onDiagramInteracted, onZoomToFit);

        diagram.UnregisterBehavior<DragNewLinkBehavior>();
        diagram.RegisterBehavior(new FbpDragNewLinkBehavior(diagram));

        if (onStructureChanged != null)
        {
            diagram.Nodes.Added += _ => onStructureChanged();
            diagram.Nodes.Removed += _ => onStructureChanged();
            diagram.Links.Added += _ => onStructureChanged();
            diagram.Links.Removed += _ => onStructureChanged();
        }

        return diagram;
    }

    private static void RegisterComponents(BlazorDiagram diagram)
    {
        diagram.RegisterComponent<CapnpFbpRunnableComponentModel, CapnpFbpComponentWidget>();
        diagram.RegisterComponent<CapnpFbpProcessComponentModel, CapnpFbpComponentWidget>();
        diagram.RegisterComponent<CapnpFbpViewComponentModel, CapnpFbpViewComponentWidget>();
        diagram.RegisterComponent<CapnpFbpIipComponentModel, CapnpFbpIipComponentWidget>();
        diagram.RegisterComponent<UpdatePortNameNode, UpdatePortNameNodeWidget>();
        diagram.RegisterComponent<PortOptionsNode, PortOptionsNodeWidget>();
        diagram.RegisterComponent<NodeInformationControl, NodeInformationControlWidget>();
        diagram.RegisterComponent<LinkInformationControl, LinkInformationControlWidget>();
        diagram.RegisterComponent<AddPortControl, AddPortControlWidget>();
        diagram.RegisterComponent<ToggleEditNodeControl, ToggleEditNodeControlWidget>();
        diagram.RegisterComponent<RemoveProcessControl, RemoveProcessControlWidget>();
        diagram.RegisterComponent<RemoveLinkControl, RemoveLinkControlWidget>();
        diagram.RegisterComponent<LinkModel, FbpLinkWidget>(true);
        diagram.RegisterComponent<ChannelLinkLabelModel, ChannelLinkLabelWidget>();
    }

    private static void RegisterDiagramEvents(
        BlazorDiagram diagram,
        IFbpRuntimeService runtime,
        Action? onDiagramInteracted,
        Func<Task>? onZoomToFit
    )
    {
        diagram.Links.Added += async l =>
        {
            diagram.Controls.AddFor(l).Add(new RemoveLinkControl(0.5, 0.5));
            if (l is RememberCapnpPortsLinkModel rememberedLink)
            {
                if (runtime.CurrentChannelStarterService is { } css)
                    await Shared.Shared.ConnectLinkToRunningProcessesAsync(
                        runtime.ConnectionManager,
                        css,
                        rememberedLink
                    );
                FbpLayoutHelper.RefreshPortLayout(l);
                FbpLayoutHelper.QueueProcStructureSyncForLink(rememberedLink);
                return;
            }

            switch (l.Source.Model)
            {
                case CapnpFbpInPortModel sourceInPort:
                {
                    l.TargetChanged += (link, oldTarget, newTarget) =>
                    {
                        if (newTarget.Model is not CapnpFbpOutPortModel outPort)
                            return;
                        var nl = new RememberCapnpPortsLinkModel(outPort, sourceInPort);
                        CapnpFbpPortColors.ApplyLinkColor(nl);
                        var cllm = new ChannelLinkLabelModel(nl, "Channel", 0.5);
                        if (sourceInPort.Channel != null)
                            _ = nl.EnsureWriterFromChannelAsync();
                        nl.Labels.Add(cllm);
                        diagram.Links.Add(nl);
                        diagram.Links.Remove(l);
                        outPort.SyncVisibility();
                        sourceInPort.SyncVisibility();
                        sourceInPort.Refresh();
                        outPort.Refresh();
                        FbpLayoutHelper.RefreshPortLayout(nl);
                    };
                    break;
                }
                case CapnpFbpOutPortModel sourceOutPort:
                {
                    l.TargetChanged += (link, oldTarget, newTarget) =>
                    {
                        if (newTarget.Model is not CapnpFbpInPortModel inPort)
                            return;
                        var nl = new RememberCapnpPortsLinkModel(sourceOutPort, inPort);
                        CapnpFbpPortColors.ApplyLinkColor(nl);
                        var cllm = new ChannelLinkLabelModel(nl, "Channel", 0.5);
                        if (inPort.Channel != null)
                            _ = nl.EnsureWriterFromChannelAsync();
                        nl.Labels.Add(cllm);
                        diagram.Links.Add(nl);
                        diagram.Links.Remove(l);
                        sourceOutPort.SyncVisibility();
                        inPort.SyncVisibility();
                        sourceOutPort.Refresh();
                        inPort.Refresh();
                        FbpLayoutHelper.RefreshPortLayout(nl);
                    };
                    break;
                }
            }
        };

        diagram.Links.Removed += l =>
        {
            FbpLayoutHelper.QueueProcStructureSyncForLink(l);
        };

        diagram.PointerClick += (m, e) =>
        {
            if (m is CapnpFbpPortModel port)
            {
                var relativePt = diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);
                var ct =
                    port.ThePortType == CapnpFbpPortModel.PortType.In
                        ? "expects [content type]"
                        : "sends [content type]";
                var node = new PortOptionsNode(relativePt)
                {
                    NameLabel = $"Change {port.Name}",
                    ContentTypeLabel = $"{port.Name} {ct}",
                    DescriptionLabel = "Description",
                    PortModel = port,
                    NodeModel = port.Parent,
                    Container = diagram,
                };
                diagram.Nodes.Add(node);
            }

            onDiagramInteracted?.Invoke();
        };

        diagram.PointerDoubleClick += (m, e) =>
        {
            if (m is LinkModel link)
            {
                if (
                    link.Source.Model is CapnpFbpPortModel source
                    && link.Target.Model is CapnpFbpPortModel target
                )
                {
                    var relativePt = diagram.GetRelativeMousePoint(e.ClientX, e.ClientY);
                    var sourceToPoint = relativePt.DistanceTo(source.MiddlePosition);
                    var targetToPoint = relativePt.DistanceTo(target.MiddlePosition);
                    var portModel = sourceToPoint < targetToPoint ? source : target;

                    var node = new UpdatePortNameNode(relativePt)
                    {
                        Label = $"Change {portModel.Name}",
                        PortName = portModel.Name,
                        PortModel = portModel,
                        Container = diagram,
                    };
                    diagram.Nodes.Add(node);
                }
            }
            else if (m == null && onZoomToFit != null)
            {
                _ = onZoomToFit();
            }

            onDiagramInteracted?.Invoke();
        };
    }
}
