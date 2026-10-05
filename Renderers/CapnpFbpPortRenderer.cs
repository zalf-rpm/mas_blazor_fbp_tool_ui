using Blazor.Diagrams;
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
    private string? _lastStyle;
    private bool _shouldRefreshPort;
    private bool _shouldRender = true;
    private bool _shouldUpdateDimensions;
    private bool _updatingDimensions;

    [CascadingParameter]
    public BlazorDiagram BlazorDiagram { get; set; } = null!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = null!;

    [Parameter]
    public CapnpFbpPortModel Port { get; set; } = null!;

    [Parameter]
    public string? Class { get; set; }

    [Parameter]
    public string? SocketColor { get; set; }

    [Parameter]
    public string? IconColor { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private PortAlignment EffectiveAlignment => Port.LayoutAlignment;

    private string PortLabelText =>
        Port.IsArrayPort ? $"{Port.Name} [{Port.ConnectedChannelCount}]" : Port.Name;

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
        var iconColor = IconColor ?? CapnpFbpPortColors.ResolvePortIconColor(Port);
        var renderSignature =
            $"{EffectiveAlignment}|{Port.LayoutOffsetPx}|{Class}|{iconColor}|{SocketColor}";
        if (string.Equals(renderSignature, _lastStyle, StringComparison.Ordinal))
            return;

        _lastStyle = renderSignature;
        _shouldRender = true;
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

        var disabled = !Port.CanAcceptMoreConnections;
        var dashed = Port.Visibility == CapnpFbpPortModel.VisibilityState.Dashed;
        var shellColor = SocketColor ?? "#d4d4d4";
        var iconColor = IconColor ?? CapnpFbpPortColors.ResolvePortIconColor(Port);
        var style = new CapnpFbpPortLayout.PortPlacement(
            EffectiveAlignment,
            Port.LayoutOffsetPx
        ).ToStyle();

        builder.OpenElement(0, _isParentSvg ? "g" : "div");
        builder.AddAttribute(
            1,
            "style",
            style
                + (disabled ? "cursor: not-allowed;" : "")
                + $"--port-shell-color: {shellColor}; --port-icon-color: {iconColor};"
        );
        builder.AddAttribute(
            2,
            "class",
            "diagram-port "
                + EffectiveAlignment.ToString().ToLowerInvariant()
                + " "
                + Port.ThePortType.ToString().ToLower()
                + " "
                + (Port.ConnectedChannelCount > 0 ? "has-links" : "")
                + " "
                + (disabled ? "disabled" : "")
                + " "
                + (dashed ? "dashed" : "")
                + " "
                + Class
        );
        builder.AddAttribute(3, "data-port-id", Port.Id);
        builder.AddAttribute(
            4,
            "onpointerdown",
            EventCallback.Factory.Create<PointerEventArgs>(this, OnPointerDown)
        );
        builder.AddEventStopPropagationAttribute(5, "onpointerdown", true);
        builder.AddAttribute(
            6,
            "onpointerup",
            EventCallback.Factory.Create<PointerEventArgs>(this, OnPointerUp)
        );
        builder.AddEventStopPropagationAttribute(7, "onpointerup", true);
        builder.AddElementReferenceCapture(
            8,
            (Action<ElementReference>)(value => _element = value)
        );

        if (ChildContent != null)
        {
            builder.AddContent(9, ChildContent);
        }
        else
        {
            if (_isParentSvg)
            {
                BuildShell(builder);
            }
            else
            {
                // A real tooltip (portalled above everything, styled like the others) anchored on the
                // port icon. Block level, so the port keeps its box and position.
                builder.OpenComponent<MudBlazor.MudTooltip>(9);
                builder.AddAttribute(10, nameof(MudBlazor.MudTooltip.Inline), false);
                builder.AddAttribute(11, nameof(MudBlazor.MudTooltip.Arrow), true);
                builder.AddAttribute(12, nameof(MudBlazor.MudTooltip.Delay), 200d);
                builder.AddAttribute(13, nameof(MudBlazor.MudTooltip.Placement), MudBlazor.Placement.Bottom);
                builder.AddAttribute(14, nameof(MudBlazor.MudTooltip.TooltipContent), (RenderFragment)BuildTooltipContent);
                builder.AddAttribute(15, nameof(MudBlazor.MudTooltip.ChildContent), (RenderFragment)BuildShell);
                builder.CloseComponent();
            }

            if (!string.IsNullOrWhiteSpace(PortLabelText))
            {
                builder.OpenElement(13, "span");
                builder.AddAttribute(14, "class", "diagram-port-label");
                builder.AddContent(15, PortLabelText);
                builder.CloseElement();
            }
        }

        builder.CloseElement();
    }

    private void BuildShell(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "diagram-port-shell");
        builder.OpenElement(2, "span");
        builder.AddAttribute(
            3,
            "class",
            "diagram-port-icon " + Port.ThePortType.ToString().ToLowerInvariant()
        );
        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildTooltipContent(RenderTreeBuilder builder)
    {
        builder.OpenComponent<MudBlazor.MudText>(0);
        builder.AddAttribute(1, nameof(MudBlazor.MudText.Typo), MudBlazor.Typo.body2);
        builder.AddAttribute(2, "Style", "max-width: 420px; overflow-wrap: anywhere;");
        builder.AddAttribute(
            3,
            nameof(MudBlazor.MudText.ChildContent),
            (RenderFragment)(content => content.AddContent(0, Shared.Shared.MakePortToolTipText(Port)))
        );
        builder.CloseComponent();
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
        BlazorDiagram.TriggerPointerDown(Port, e.ToCore());
    }

    private void OnPointerUp(PointerEventArgs e)
    {
        BlazorDiagram.TriggerPointerUp(
            e.PointerType == "mouse" ? Port : FindPortOn(e.ClientX, e.ClientY),
            e.ToCore()
        );
    }

    private PortModel? FindPortOn(double clientX, double clientY)
    {
        foreach (
            var portOn in BlazorDiagram
                .Nodes.SelectMany((Func<NodeModel, IEnumerable<PortModel>>)(n => n.Ports))
                .Union(
                    BlazorDiagram.Groups.SelectMany(
                        (Func<GroupModel, IEnumerable<PortModel>>)(g => g.Ports)
                    )
                )
        )
        {
            if (!portOn.Initialized)
                continue;
            var relativeMousePoint = BlazorDiagram.GetRelativeMousePoint(clientX, clientY);
            if (portOn.GetBounds().ContainsPoint(relativeMousePoint))
                return portOn;
        }

        return null;
    }

    private Task UpdateDimensions()
    {
        if (BlazorDiagram.Container == null)
            return Task.CompletedTask;

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

        return Task.CompletedTask;
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
            await InvokeAsync(StateHasChanged);
        }
        else
        {
            await UpdateDimensions();
        }
    }
}
