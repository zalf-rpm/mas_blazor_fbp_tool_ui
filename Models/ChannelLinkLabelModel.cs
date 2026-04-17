using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using BlazorDrawFBP.Models;
using Mas.Schema.Fbp;
using Mas.Schema.Management;

namespace BlazorDrawFBP.Models;

public class ChannelLinkLabelModel : LinkLabelModel, IDisposable
{
    // public ChannelLinkLabelModel(
    //     RememberCapnpPortsLinkModel parent,
    //     string id,
    //     string content,
    //     double? distance = null,
    //     Point offset = null
    // )
    //     : base(parent, id, content, distance, offset)
    // {
    //     _inPort = parent.InPortModel;
    // }

    public ChannelLinkLabelModel(
        RememberCapnpPortsLinkModel parent,
        string content,
        double? distance = null,
        Point offset = null
    )
        : base(parent, content, distance, offset)
    {
        _inPort = parent.InPortModel;
    }

    public bool ShowStats => _inPort.ReceivingStats;
    private readonly CapnpFbpInPortModel _inPort;

    public RememberCapnpPortsLinkModel LinkModel => Parent as RememberCapnpPortsLinkModel;

    public void Dispose()
    {
        Console.WriteLine("ChannelLinkLabelModel::Dispose()");
    }
}
