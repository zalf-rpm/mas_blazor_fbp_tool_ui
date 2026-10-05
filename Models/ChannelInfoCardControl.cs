using Blazor.Diagrams.Core.Controls;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models.Base;

namespace BlazorDrawFBP.Models;

/// <summary>
/// Shows the expanded channel card of a <see cref="ChannelLinkLabelModel"/>. Link labels are drawn
/// in the links layer, which is below the nodes; controls are rendered after the nodes, so the card
/// ends up in front of them. The widget positions the card itself (see
/// <see cref="ChannelLinkLabelModel.CanvasPosition"/>), so it always follows the link.
/// </summary>
public class ChannelInfoCardControl : Control
{
    public override Point? GetPosition(Model model)
    {
        return new Point(0, 0);
    }
}
