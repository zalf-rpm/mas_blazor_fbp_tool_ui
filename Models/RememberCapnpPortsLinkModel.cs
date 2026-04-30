using System;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;
using Mas.Schema.Fbp;

namespace BlazorDrawFBP.Models;

public class RememberCapnpPortsLinkModel : LinkModel, IDisposable
{
    public RememberCapnpPortsLinkModel(CapnpFbpOutPortModel outPort, CapnpFbpInPortModel inPort)
        : base(CreatePortAnchor(outPort), CreatePortAnchor(inPort))
    {
        OutPortModel = outPort;
        InPortModel = inPort;
    }

    public CapnpFbpOutPortModel OutPortModel { get; set; }
    public CapnpFbpInPortModel InPortModel { get; set; }

    public Mas.Schema.Fbp.Channel<IP>.StatsCallback.Stats Stats { get; set; } = new();

    private static SinglePortAnchor CreatePortAnchor(CapnpFbpPortModel port) =>
        new(port)
        {
            MiddleIfNoMarker = true,
            UseShapeAndAlignment = false,
        };

    public void Dispose()
    {
        Console.WriteLine("RememberCapnpPortsLinkModel::Dispose()");
    }
}
