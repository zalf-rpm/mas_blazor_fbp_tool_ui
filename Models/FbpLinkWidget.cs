using Blazor.Diagrams;
using Blazor.Diagrams.Components.Renderers;
using Blazor.Diagrams.Core.Extensions;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Extensions;
using BlazorDrawFBP.Behaviors;
using BlazorDrawFBP.Renderers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.CompilerServices;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDrawFBP.Models;

public class FbpLinkWidget : ComponentBase
{
    private bool _hovered;

    [CascadingParameter]
    public BlazorDiagram BlazorDiagram { get; set; } = null!;

    [Parameter]
    public LinkModel Link { get; set; } = null!;

    private RenderFragment GetSelectionHelperPath(
        string color,
        string d,
        int index,
        bool enableHoverFeedback
    )
    {
        return (RenderFragment)(
            builder =>
            {
                builder.OpenElement(0, "path");
                builder.AddAttribute(1, "class", "selection-helper");
                builder.AddAttribute(2, "stroke", color);
                builder.AddAttribute(3, "stroke-width", 12);
                builder.AddAttribute(4, nameof(d), d);
                builder.AddAttribute(5, "stroke-linecap", "butt");
                builder.AddAttribute(
                    6,
                    "stroke-opacity",
                    enableHoverFeedback && _hovered ? "0.05" : "0"
                );
                builder.AddAttribute(7, "fill", "none");
                if (enableHoverFeedback)
                {
                    builder.AddAttribute(
                        8,
                        "onmouseenter",
                        EventCallback.Factory.Create<MouseEventArgs>(this, OnMouseEnter)
                    );
                    builder.AddAttribute(
                        9,
                        "onmouseleave",
                        EventCallback.Factory.Create<MouseEventArgs>(this, OnMouseLeave)
                    );
                }

                builder.AddAttribute(
                    10,
                    "onpointerdown",
                    EventCallback.Factory.Create<PointerEventArgs>(
                        this,
                        (Action<PointerEventArgs>)(e => OnPointerDown(e, index))
                    )
                );
                builder.AddEventStopPropagationAttribute(11, "onpointerdown", Link.Segmentable);
                builder.CloseElement();
            }
        );
    }

    private void OnPointerDown(PointerEventArgs e, int index)
    {
        if (!Link.Segmentable)
            return;
        BlazorDiagram.TriggerPointerDown(CreateVertex(e.ClientX, e.ClientY, index), e.ToCore());
    }

    private void OnMouseEnter(MouseEventArgs e)
    {
        _hovered = true;
    }

    private void OnMouseLeave(MouseEventArgs e)
    {
        _hovered = false;
    }

    private LinkVertexModel CreateVertex(double clientX, double clientY, int index)
    {
        var vertex = new LinkVertexModel(
            Link,
            BlazorDiagram.GetRelativeMousePoint(clientX, clientY)
        );
        Link.Vertices.Insert(index, vertex);
        Link.Refresh();
        return vertex;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var color = Link.Selected
            ? Link.SelectedColor ?? BlazorDiagram.Options.Links.DefaultSelectedColor
            : Link.Color ?? BlazorDiagram.Options.Links.DefaultColor;
        var pathGeneratorResult = Link.PathGeneratorResult;
        if (pathGeneratorResult == null)
            return;
        var behavior = BlazorDiagram.GetBehavior<FbpDragNewLinkBehavior>();
        if (behavior == null)
            return;
        var enableSelectionHoverFeedback = !Link.Labels.OfType<ChannelLinkLabelModel>().Any();
        var d1 = pathGeneratorResult.FullPath.ToString();
        builder.OpenElement(0, "path");
        builder.AddAttribute(1, "d", d1);
        builder.AddAttribute(2, "stroke-width", Link.Width.ToInvariantString());
        builder.AddAttribute(3, "fill", "none");
        builder.AddAttribute(4, "stroke", color);
        builder.CloseElement();
        if (behavior.OngoingLink == null || behavior.OngoingLink != Link)
        {
            if (Link.Vertices.Count == 0)
                builder.AddContent(
                    5,
                    GetSelectionHelperPath(color, d1, 0, enableSelectionHoverFeedback)
                );
            else
                for (var index1 = 0; index1 < pathGeneratorResult.Paths.Length; ++index1)
                {
                    var d2 = pathGeneratorResult.Paths[index1].ToString();
                    var index2 = index1;
                    builder.AddContent(
                        6,
                        GetSelectionHelperPath(color, d2, index2, enableSelectionHoverFeedback)
                    );
                }
        }

        if (
            Link.SourceMarker != null
            && pathGeneratorResult.SourceMarkerAngle is double sourceAngle
            && pathGeneratorResult.SourceMarkerPosition is Point sourcePos
        )
        {
            builder.OpenElement(7, "g");
            builder.AddAttribute(
                8,
                "transform",
                FormattableString.Invariant(
                    $"translate({sourcePos.X}, {sourcePos.Y}) rotate({sourceAngle})"
                )
            );
            builder.OpenElement(9, "path");
            builder.AddAttribute(10, "d", Link.SourceMarker.Path);
            builder.AddAttribute(11, "fill", color);
            builder.CloseElement();
            builder.CloseElement();
        }

        if (
            Link.TargetMarker != null
            && pathGeneratorResult.TargetMarkerAngle is double targetAngle
            && pathGeneratorResult.TargetMarkerPosition is Point targetPos
        )
        {
            builder.OpenElement(12, "g");
            builder.AddAttribute(
                13,
                "transform",
                FormattableString.Invariant(
                    $"translate({targetPos.X}, {targetPos.Y}) rotate({targetAngle})"
                )
            );
            builder.OpenElement(14, "path");
            builder.AddAttribute(15, "d", Link.TargetMarker.Path);
            builder.AddAttribute(16, "fill", color);
            builder.CloseElement();
            builder.CloseElement();
        }

        if (Link.Vertices.Count > 0)
        {
            var str1 = Link.SelectedColor ?? BlazorDiagram.Options.Links.DefaultSelectedColor;
            var str2 = Link.Color ?? BlazorDiagram.Options.Links.DefaultColor;
            foreach (var vertex in Link.Vertices)
            {
                builder.OpenComponent<LinkVertexRenderer>(17);
                builder.AddAttribute(18, "Vertex", RuntimeHelpers.TypeCheck(vertex));
                builder.AddAttribute(19, "Color", (object)RuntimeHelpers.TypeCheck(str2));
                builder.AddAttribute(20, "SelectedColor", (object)RuntimeHelpers.TypeCheck(str1));
                builder.SetKey(vertex.Id);
                builder.CloseComponent();
            }
        }

        foreach (var label in Link.Labels)
        {
            var labelRendererType =
                label is ChannelLinkLabelModel
                    ? typeof(CapnpLinkLabelRenderer)
                    : typeof(LinkLabelRenderer);
            builder.OpenComponent(21, labelRendererType);
            builder.AddAttribute(22, "Label", RuntimeHelpers.TypeCheck(label));
            builder.AddAttribute(
                23,
                "Path",
                RuntimeHelpers.TypeCheck(pathGeneratorResult.FullPath)
            );
            builder.SetKey(label.Id);
            builder.CloseComponent();
        }
    }
}
