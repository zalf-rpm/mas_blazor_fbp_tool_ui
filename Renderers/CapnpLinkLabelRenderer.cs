using Blazor.Diagrams;
using Blazor.Diagrams.Components;
using Blazor.Diagrams.Core.Extensions;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SvgPathProperties;

namespace BlazorDrawFBP.Renderers;

public class CapnpLinkLabelRenderer : ComponentBase, IDisposable
{
    [CascadingParameter]
    public BlazorDiagram BlazorDiagram { get; set; } = null!;

    [Parameter]
    public LinkLabelModel Label { get; set; } = null!;

    [Parameter]
    public SvgPath Path { get; set; } = null!;

    public void Dispose()
    {
        Label.Changed -= OnLabelChanged;
        Label.VisibilityChanged -= OnLabelChanged;

        // only when the link is gone; a replaced renderer instance of a living link keeps the control
        if (Label is ChannelLinkLabelModel && !BlazorDiagram.Links.Contains(Label.Parent))
            BlazorDiagram.Controls.RemoveFor(Label);
    }

    protected override void OnInitialized()
    {
        Label.Changed += OnLabelChanged;
        Label.VisibilityChanged += OnLabelChanged;

        // the expanded channel card is drawn above the nodes by the controls layer (it renders nothing
        // unless the label is expanded)
        if (Label is ChannelLinkLabelModel)
        {
            var container = BlazorDiagram.Controls.AddFor(Label);
            if (container.Count == 0)
                container.Add(new ChannelInfoCardControl());
            container.Show();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!Label.Visible)
            return;
        var position = FindPosition();
        var x = position.X + (Label.Offset?.X ?? 0.0);
        var y = position.Y + (Label.Offset?.Y ?? 0.0);
        var type = BlazorDiagram.GetComponent(Label) ?? typeof(DefaultLinkLabelWidget);
        var componentType = type;
        if (Label is ChannelLinkLabelModel channelLabel)
        {
            channelLabel.UpdateCanvasPosition(new Point(x, y));
            // expanded: the card is rendered by ChannelInfoCardControlWidget, above the nodes
            if (!channelLabel.ShowWidget || channelLabel.IsExpanded)
                return;

            builder.OpenElement(0, "foreignObject");
            builder.AddAttribute(1, "class", "diagram-link-label");
            const int width = ChannelLinkLabelModel.CompactInteractionCanvasWidth;
            const int height = ChannelLinkLabelModel.CompactInteractionCanvasHeight;
            builder.AddAttribute(2, "x", (x - width / 2.0).ToInvariantString());
            builder.AddAttribute(3, "y", (y - height / 2.0).ToInvariantString());
            builder.AddAttribute(4, "width", width.ToString());
            builder.AddAttribute(5, "height", height.ToString());
            builder.AddAttribute(6, "style", "overflow: visible;");
            builder.OpenComponent(7, componentType);
            builder.AddAttribute(8, "Label", Label);
        }
        else
        {
            builder.OpenElement(0, "foreignObject");
            builder.AddAttribute(1, "class", "diagram-link-label");
            builder.AddAttribute(2, "x", x.ToInvariantString());
            builder.AddAttribute(3, "y", y.ToInvariantString());
            builder.OpenComponent(4, componentType);
            builder.AddAttribute(5, "Label", Label);
        }

        builder.CloseComponent();
        builder.CloseElement();
    }

    private void OnLabelChanged(Model _)
    {
        InvokeAsync(StateHasChanged);
    }

    private Point FindPosition()
    {
        var length = Path.Length;
        var distance = Label.Distance;
        double fractionLength;
        if (distance is double d)
            fractionLength = d switch
            {
                <= 1.0 => d >= 0.0 ? d * length : length + d,
                _ => d,
            };
        else
            fractionLength =
                length * (Label.Parent.Labels.IndexOf(Label) + 1) / (Label.Parent.Labels.Count + 1);
        var pointAtLength = Path.GetPointAtLength(fractionLength);
        return new Point(pointAtLength.X, pointAtLength.Y);
    }
}
