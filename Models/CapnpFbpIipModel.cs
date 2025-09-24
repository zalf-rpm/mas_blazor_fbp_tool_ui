using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace BlazorDrawFBP.Models;

public class CapnpFbpIipModel : NodeModel
{
    public CapnpFbpIipModel(Point position = null) : base(position)
    {
    }

    public string ComponentId { get; set; }

    public string ShortDescription { get; set; }
    public string Content { get; set; }

    public int DisplayNoOfLines { get; set; } = 3;
}