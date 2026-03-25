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
        if (ports.Any())
        {
            if (ports.Last() is CapnpFbpPortModel lastPort)
            {
                var newOrderNo = lastPort.OrderNo + 1;
                CreateAndAddPort(NodeModel, PortType, newOrderNo);
            }

            NodeModel.RefreshAll();
        }
    }

    public const int NoOfLeftRightPorts = 6;
    public const int MaxLeftRightPortOrderNo = NoOfLeftRightPorts - 1;
    public const int NoOfTopBottomPorts = 9;
    public const int MaxTopBottomPortOrderNo = NoOfTopBottomPorts - 1;

    public static CapnpFbpPortModel CreateAndAddPort(
        NodeModel node,
        CapnpFbpPortModel.PortType portType,
        int orderNo,
        string name = null,
        string contentType = null,
        string description = null
    )
    {
        if (orderNo > NoOfLeftRightPorts + NoOfTopBottomPorts - 1)
            return null;
        var alignment = PortAlignmentForOrderNo(portType, orderNo);
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
        node.RefreshAll();
        return port;
    }

    private static PortAlignment PortAlignmentForOrderNo(
        CapnpFbpPortModel.PortType portType,
        int orderNo
    )
    {
        if (portType == CapnpFbpPortModel.PortType.In)
            return orderNo switch
            {
                MaxLeftRightPortOrderNo => PortAlignment.TopLeft,
                > MaxLeftRightPortOrderNo => PortAlignment.Top,
                _ => PortAlignment.Left,
            };
        else
            return orderNo switch
            {
                MaxLeftRightPortOrderNo => PortAlignment.BottomRight,
                > MaxLeftRightPortOrderNo => PortAlignment.Bottom,
                _ => PortAlignment.Right,
            };
    }

    private static readonly Dictionary<int, List<int>> LeftRightOffsetsMap = new()
    {
        {
            1,
            new List<int> { 0 }
        },
        {
            2,
            new List<int> { 10, -10 }
        },
        {
            3,
            new List<int> { 20, 0, -20 }
        },
        {
            4,
            new List<int> { 30, 10, -10, -30 }
        },
        {
            5,
            new List<int> { 30, 15, 0, -15, -30 }
        },
        {
            6,
            new List<int> { 30, 15, 0, -15, -30, -45 }
        },
        {
            7,
            new List<int> { 30, 20, 10, 0, -10, -20, -30 }
        },
        {
            8,
            new List<int> { 35, 25, 15, 5, -5, -15, -25, -35 }
        },
        {
            9,
            new List<int> { 40, 30, 20, 10, 0, -10, -20, -30, -40 }
        },
        {
            10,
            new List<int> { -45, -35, -25, -15, -5, 5, 15, 25, 35, 45 }
        },
        {
            11,
            new List<int> { 50, 40, 30, 20, 10, 0, -10, -20, -30, -40, -50 }
        },
    };

    private static readonly List<int> TopBottomOffsets = new()
    {
        -40,
        -30,
        -20,
        -10,
        0,
        10,
        20,
        30,
        40,
    };
}
