using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blazor.Diagrams;
using Blazor.Diagrams.Components.Renderers;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using Blazor.Diagrams.Extensions;
using Blazor.Diagrams.Models;
using BlazorDrawFBP.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazorDrawFBP.Renderers;

public class CapnpFbpPortRenderer : ComponentBase, IDisposable
{
    private ElementReference _element;
    private bool _isParentSvg;
    private bool _shouldRefreshPort;
    private bool _shouldRender = true;
    private bool _shouldUpdateDimensions;
    private string _lastStyle;
    private bool _updatingDimensions;

    [CascadingParameter] public BlazorDiagram BlazorDiagram { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; }

    [Parameter] public CapnpFbpPortModel Port { get; set; }

    [Parameter] public string Class { get; set; }

    [Parameter] public string BorderColor { get; set; }
    
    [Parameter] public RenderFragment ChildContent { get; set; }

    private PortAlignment EffectiveAlignment => Port.LayoutAlignment;

    public void Dispose()
    {
        Port.Changed -= OnPortChanged;
        Port.VisibilityChanged -= OnPortChanged;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Port.Changed += OnPortChanged;
        Port.VisibilityChanged += OnPortChanged;
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        _isParentSvg = Port.Parent is SvgNodeModel;
        var renderSignature = $"{EffectiveAlignment}|{Port.LayoutOffsetPx}|{Class}|{BorderColor}";
        if (string.Equals(renderSignature, _lastStyle, StringComparison.Ordinal))
        {
            return;
        }

        _lastStyle = renderSignature;
        _shouldUpdateDimensions = true;
    }

    protected override bool ShouldRender()
    {
        if (!_shouldRender)
            return false;
        _shouldRender = false;
        return true;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!Port.Visible)
            return;
        
        var visibility = Port.Visibility switch
        {
            CapnpFbpPortModel.VisibilityState.Hidden =>
                "opacity: 0.55; cursor: not-allowed; background-color: #e5e7eb; color: #6b7280;",
            CapnpFbpPortModel.VisibilityState.Dashed => "border-style: dashed;",
            CapnpFbpPortModel.VisibilityState.Visible => "",
            _ => throw new ArgumentOutOfRangeException()
        };
        var style = new CapnpFbpPortLayout.PortPlacement(
            EffectiveAlignment,
            Port.LayoutOffsetPx
        ).ToStyle(BorderColor ?? "#d4d4d4");
        
        builder.OpenElement(0, _isParentSvg ? "g" : "div");
        builder.AddAttribute(1, "style", style + visibility);
        builder.AddAttribute(2, "class",
            "diagram-port " + EffectiveAlignment.ToString().ToLowerInvariant() + " " + 
            Port.ThePortType.ToString().ToLower() + " " +
            //offsetString + " " + 
            (Port.Links.Count > 0 ? "has-links" : "") + " " +
            (Port.Visibility == CapnpFbpPortModel.VisibilityState.Hidden ? "disabled" : "") + " " +
            Class);
        builder.AddAttribute(3, "data-port-id", Port.Id);
        builder.AddAttribute(4, "onpointerdown",
            EventCallback.Factory.Create<PointerEventArgs>(this, OnPointerDown));
        builder.AddEventStopPropagationAttribute(5, "onpointerdown", true);
        builder.AddAttribute(6, "onpointerup",
            EventCallback.Factory.Create<PointerEventArgs>(this, OnPointerUp));
        builder.AddEventStopPropagationAttribute(7, "onpointerup", true);
        builder.AddElementReferenceCapture(8, (Action<ElementReference>)(value => _element = value));
        builder.AddContent(9, ChildContent);
        builder.CloseElement();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Port.Initialized && !_shouldUpdateDimensions)
            return;
        _shouldUpdateDimensions = false;
        await UpdateDimensions();
    }

    private void OnPointerDown(PointerEventArgs e)
    {
        BlazorDiagram.TriggerPointerDown(Port, EventsExtensions.ToCore(e));
    }

    private void OnPointerUp(PointerEventArgs e)
    {
        BlazorDiagram.TriggerPointerUp(
            e.PointerType == "mouse" ? Port : FindPortOn(e.ClientX, e.ClientY),
            EventsExtensions.ToCore(e));
    }

    private PortModel FindPortOn(double clientX, double clientY)
    {
        foreach (var portOn in BlazorDiagram.Nodes
                     .SelectMany((Func<NodeModel, IEnumerable<PortModel>>)(n => n.Ports))
                     .Union(BlazorDiagram.Groups.SelectMany(
                         (Func<GroupModel, IEnumerable<PortModel>>)(g => g.Ports))))
        {
            if (!portOn.Initialized) continue;
            var relativeMousePoint = BlazorDiagram.GetRelativeMousePoint(clientX, clientY);
            if (portOn.GetBounds().ContainsPoint(relativeMousePoint))
                return portOn;
        }

        return (PortModel)null;
    }

    private async Task UpdateDimensions()
    {
        Point pan;
        if (BlazorDiagram.Container == null)
        {
            pan = null;
        }
        else if (Port is CapnpFbpPortModel)
        {
            _updatingDimensions = true;
            Port.Initialized = true;
            _updatingDimensions = false;
            if (_shouldRefreshPort)
            {
                _shouldRefreshPort = false;
                Port.RefreshAll();
            }
            else
            {
                Port.RefreshLinks();
            }
        }
        else
        {
            _updatingDimensions = true;
            var zoom = BlazorDiagram.Zoom;
            pan = BlazorDiagram.Pan;
            var boundingClientRect = await JSRuntime.GetBoundingClientRect(_element);
            Port.Size = new Size(boundingClientRect.Width / zoom, boundingClientRect.Height / zoom);
            Port.Position = new Point((boundingClientRect.Left - BlazorDiagram.Container.Left - pan.X) / zoom,
                (boundingClientRect.Top - BlazorDiagram.Container.Top - pan.Y) / zoom);
            Port.Initialized = true;
            _updatingDimensions = false;
            if (_shouldRefreshPort)
            {
                _shouldRefreshPort = false;
                Port.RefreshAll();
                pan = null;
            }
            else
            {
                Port.RefreshLinks();
                pan = null;
            }
        }
    }

    private void OnPortChanged(Model model)
    {
        _ = InvokeAsync(async () =>
        {
            try
            {
                await HandlePortChangedAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        });
    }

    private async Task HandlePortChangedAsync()
    {
        if (_updatingDimensions)
            _shouldRefreshPort = true;
        if (Port.Initialized)
        {
            _shouldRender = true;
            StateHasChanged();
        }
        else
        {
            await UpdateDimensions();
        }
    }
}
