using System.Collections.Generic;
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

public class AddPortControl : ExecutableControl
{
    private readonly IPositionProvider _positionProvider;
    public string Label { get; set; } = "Port";

    public CapnpFbpPortModel.PortType PortType { get; set; } = CapnpFbpPortModel.PortType.In;

    public CapnpFbpComponentModel NodeModel { get; set; } = null;

    public AddPortControl(double x, double y, double offsetX = 0.0, double offsetY = 0.0)
        : this(new BoundsBasedPositionProvider(x, y, offsetX, offsetY)) { }

    public AddPortControl(IPositionProvider positionProvider)
    {
        _positionProvider = positionProvider;
    }

    public override Point GetPosition(Model model)
    {
        return _positionProvider.GetPosition(model);
    }

    public override async ValueTask OnPointerDown(Diagram diagram, Model model, PointerEventArgs _)
    {
        var ports = NodeModel
            .Ports.Where(p => p is CapnpFbpPortModel cp && cp.ThePortType == PortType)
            .OrderBy(p => p is CapnpFbpPortModel cp ? cp.OrderNo : 0)
            .ToList();
        var newOrderNo =
            ports.LastOrDefault() is CapnpFbpPortModel lastPort ? lastPort.OrderNo + 1 : 0;
        CreateAndAddPort(NodeModel, PortType, newOrderNo);
        NodeModel.RefreshAll();
    }

    public static CapnpFbpPortModel CreateAndAddPort(
        NodeModel node,
        CapnpFbpPortModel.PortType portType,
        int orderNo,
        string name = null,
        string contentType = null,
        string description = null
    )
    {
        var alignment =
            portType == CapnpFbpPortModel.PortType.In ? PortAlignment.Left : PortAlignment.Right;
        CapnpFbpPortModel port = portType switch {
            CapnpFbpPortModel.PortType.In => new CapnpFbpInPortModel(node, alignment) {
                Name = name ?? "IN",
                ContentType = contentType ?? "?",
                Description = description ?? "",
                OrderNo = orderNo,
            },
            CapnpFbpPortModel.PortType.Out => new CapnpFbpOutPortModel(node, alignment) {
                Name = name ?? "OUT",
                ContentType = contentType ?? "?",
                Description = description ?? "",
                OrderNo = orderNo,
            },
            _ => null
        };

        if (port == null) return port;
        node.AddPort(port);
        CapnpFbpPortLayout.Apply(node, refreshPorts: false);
        node.RefreshAll();
        return port;
    }
}
