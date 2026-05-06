using System.Linq;
using System.Threading.Tasks;
using Blazor.Diagrams.Core;
using Blazor.Diagrams.Core.Controls;
using Blazor.Diagrams.Core.Controls.Default;
using Blazor.Diagrams.Core.Events;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Blazor.Diagrams.Core.Positions;
using BlazorDrawFBP.Models;

namespace BlazorDrawFBP.Controls;

public class RemoveLinkControl : ExecutableControl
{
    private readonly IPositionProvider _positionProvider;

    public RemoveLinkControl(double x, double y, double offsetX = 0.0, double offsetY = 0.0)
        : this(new BoundsBasedPositionProvider(x, y, offsetX, offsetY)) { }

    public RemoveLinkControl(IPositionProvider positionProvider)
    {
        _positionProvider = positionProvider;
    }

    public override Point GetPosition(Model model)
    {
        return _positionProvider.GetPosition(model);
    }

    public override async ValueTask OnPointerDown(Diagram diagram, Model model, PointerEventArgs _)
    {
        if (!await ShouldDeleteModel(diagram, model))
        {
            return;
        }

        await DeleteModel(diagram, model);
    }

    private static async Task DeleteModel(Diagram diagram, Model model)
    {
        switch (model)
        {
            case GroupModel group:
                diagram.Groups.Delete(group);
                break;
            case NodeModel nodeModel:
                diagram.Nodes.Remove(nodeModel);
                break;
            case BaseLinkModel baseLinkModel:
                await Shared.Shared.RemoveLinkAndCleanupAsync(diagram, baseLinkModel);
                break;
        }
    }

    private static async ValueTask<bool> ShouldDeleteModel(Diagram diagram, Model model)
    {
        if (model.Locked)
        {
            return false;
        }

        var flag = model switch
        {
            GroupModel groupModel => await diagram.Options.Constraints.ShouldDeleteGroup(
                groupModel
            ),
            NodeModel nodeModel => await diagram.Options.Constraints.ShouldDeleteNode(nodeModel),
            BaseLinkModel baseLinkModel => await diagram.Options.Constraints.ShouldDeleteLink(
                baseLinkModel
            ),
            _ => false,
        };

        return flag;
    }
}
