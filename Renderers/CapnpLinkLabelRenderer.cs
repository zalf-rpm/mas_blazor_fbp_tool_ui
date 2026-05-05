using Blazor.Diagrams.Core.Extensions;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SvgPathProperties;
using System;
using Blazor.Diagrams;
using Blazor.Diagrams.Components;

namespace BlazorDrawFBP.Renderers;

public class CapnpLinkLabelRenderer : ComponentBase, IDisposable
{
  [CascadingParameter]
  public BlazorDiagram BlazorDiagram { get; set; }

  [Parameter]
  public LinkLabelModel Label { get; set; }

  [Parameter]
  public SvgPath Path { get; set; }

  public void Dispose()
  {
    Label.Changed -= OnLabelChanged;
    Label.VisibilityChanged -= OnLabelChanged;
  }

  protected override void OnInitialized()
  {
    Label.Changed += OnLabelChanged;
    Label.VisibilityChanged += OnLabelChanged;
  }

  protected override void BuildRenderTree(RenderTreeBuilder builder)
  {
    if (!Label.Visible)
      return;
    var position = FindPosition();
    var x = position.X + (Label.Offset?.X ?? 0.0);
    var y = position.Y + (Label.Offset?.Y ?? 0.0);
    var type = BlazorDiagram.GetComponent(Label);
    if ((object) type == null)
      type = typeof (DefaultLinkLabelWidget);
    var componentType = type;
    if (Label is ChannelLinkLabelModel channelLabel)
    {
      if (!channelLabel.ShowWidget)
        return;

      builder.OpenElement(0, "foreignObject");
      builder.AddAttribute(1, "class", "diagram-link-label");
      var width = channelLabel.IsExpanded
        ? ChannelLinkLabelModel.ExpandedInteractionCanvasWidth
        : ChannelLinkLabelModel.CompactInteractionCanvasWidth;
      var height = channelLabel.IsExpanded
        ? ChannelLinkLabelModel.ExpandedInteractionCanvasHeight
        : ChannelLinkLabelModel.CompactInteractionCanvasHeight;
      builder.AddAttribute(2, "x", (x - width / 2.0).ToInvariantString());
      builder.AddAttribute(3, "y", (y - height / 2.0).ToInvariantString());
      builder.AddAttribute(4, "width", width.ToString());
      builder.AddAttribute(5, "height", height.ToString());
      builder.AddAttribute(6, "style", "overflow: visible;");
      builder.OpenComponent(7, componentType);
      builder.AddAttribute(8, "Label", (object) Label);
    }
    else
    {
      builder.OpenElement(0, "foreignObject");
      builder.AddAttribute(1, "class", "diagram-link-label");
      builder.AddAttribute(2, "x", x.ToInvariantString());
      builder.AddAttribute(3, "y", y.ToInvariantString());
      builder.OpenComponent(4, componentType);
      builder.AddAttribute(5, "Label", (object) Label);
    }
    builder.CloseComponent();
    builder.CloseElement();
  }

  private void OnLabelChanged(Model _)
  {
    InvokeAsync(StateHasChanged);
  }

  private Blazor.Diagrams.Core.Geometry.Point FindPosition()
  {
    var length = Path.Length;
    var distance = Label.Distance;
    double fractionLength;
    if (distance.HasValue)
    {
      var valueOrDefault = distance.GetValueOrDefault();
      if (valueOrDefault <= 1.0)
      {
        fractionLength = valueOrDefault >= 0.0 ? Label.Distance.Value * length : length + Label.Distance.Value;
        goto label_6;
      }
      else if (valueOrDefault > 1.0)
      {
        fractionLength = Label.Distance.Value;
        goto label_6;
      }
    }

    fractionLength = length * (Label.Parent.Labels.IndexOf(Label) + 1) / (Label.Parent.Labels.Count + 1);
    label_6:
    var pointAtLength = Path.GetPointAtLength(fractionLength);
    return new Blazor.Diagrams.Core.Geometry.Point(pointAtLength.X, pointAtLength.Y);
  }
}
