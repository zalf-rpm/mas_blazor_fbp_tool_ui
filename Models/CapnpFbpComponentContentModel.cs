using Blazor.Diagrams.Core;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace BlazorDrawFBP.Models;

public class CapnpFbpComponentContentModel : NodeModel
{
    public CapnpFbpComponentContentModel(Point? position = null)
        : base(position) { }

    public string Label { get; set; } = "";
    public CapnpFbpComponentModel ComponentModel { get; set; } = null!;

    public Diagram Container { get; set; } = null!;
}
