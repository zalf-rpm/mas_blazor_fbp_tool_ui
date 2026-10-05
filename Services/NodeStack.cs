using System.Runtime.CompilerServices;
using System.Text;
using Blazor.Diagrams.Core;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;

namespace BlazorDrawFBP.Services;

/// <summary>
/// Stacking of the nodes of one diagram ("last used on top"). The library only orders nodes by
/// rendering them in <c>Order</c>, which moves DOM elements; doing that on pointer down or focus
/// interrupts the click or drag in progress and drops focus from inputs. So the order is kept here
/// and applied as CSS <c>z-index</c> per node (<see cref="BuildCss"/>), which never moves anything.
/// </summary>
public sealed class NodeStack
{
    /// <summary>Popup nodes always stay above regular nodes.</summary>
    private const int PopupBase = 1_000_000;

    private static readonly ConditionalWeakTable<Diagram, NodeStack> Stacks = new();

    private readonly Dictionary<NodeModel, int> _index = [];
    private int _last;

    private NodeStack(Diagram diagram)
    {
        foreach (var node in diagram.Nodes)
            _index[node] = ++_last;
        diagram.Nodes.Added += OnAdded;
        diagram.Nodes.Removed += OnRemoved;
    }

    /// <summary>Raised when a z-index changed, so the CSS has to be rendered again.</summary>
    public event Action? Changed;

    public static NodeStack For(Diagram diagram)
    {
        return Stacks.GetValue(diagram, d => new NodeStack(d));
    }

    /// <summary>Nodes that are shown as small popups next to a port.</summary>
    public static bool IsPopup(NodeModel node)
    {
        return node is PortOptionsNode or UpdatePortNameNode;
    }

    /// <summary>Brings the node to the front (popups stay above regular nodes).</summary>
    public void Raise(NodeModel? node)
    {
        if (node == null || !_index.TryGetValue(node, out var current))
            return;

        // already in front: nothing to render again on every click
        if (current == _last)
            return;

        _index[node] = ++_last;
        Changed?.Invoke();
    }

    public int ZIndexOf(NodeModel node)
    {
        return _index.TryGetValue(node, out var index) ? index + (IsPopup(node) ? PopupBase : 0) : 0;
    }

    public string BuildCss()
    {
        var css = new StringBuilder();
        foreach (var (node, _) in _index)
            css.Append($".diagram-node[data-node-id=\"{node.Id}\"]{{z-index:{ZIndexOf(node)}}}");
        return css.ToString();
    }

    private void OnAdded(NodeModel node)
    {
        // new nodes appear in front, as before
        _index[node] = ++_last;
        Changed?.Invoke();
    }

    private void OnRemoved(NodeModel node)
    {
        if (_index.Remove(node))
            Changed?.Invoke();
    }
}
