using System;
using System.Collections.Generic;
using System.Linq;
using BlazorDrawFBP.Pages;
using Mas.Schema.Common;

namespace BlazorDrawFBP.Models;

using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

public class CapnpFbpIipModel : NodeModel, IDisposable
{
    public CapnpFbpIipModel(Point position = null)
        : base(position) { }

    public Editor Editor { get; set; }

    public string ComponentId { get; set; }

    public string ShortDescription { get; set; }
    public string Content { get; set; }

    public Mas.Schema.Common.StructuredText.Type ContentType { get; set; } = StructuredText.Type.unstructured;

    public int DisplayNoOfLines { get; set; } = 3;

    public void Dispose()
    {
        Console.WriteLine($"CapnpFbpIipModel::Disposing");

        foreach (var baseLinkModel in Links)
        {
            Shared.Shared.RestoreDefaultPortVisibility(Editor.Diagram, baseLinkModel);
        }

        foreach (var port in Ports)
        {
            if (port is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
