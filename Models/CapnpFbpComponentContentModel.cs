using Blazor.Diagrams.Core;

namespace BlazorDrawFBP.Models;

using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

public class CapnpFbpComponentContentModel : NodeModel
{
    public CapnpFbpComponentContentModel(Point position = null)
        : base(position) { }

    public string Label { get; set; }
    public CapnpFbpComponentModel ComponentModel { get; set; }

    public Diagram Container { get; set; }
}
