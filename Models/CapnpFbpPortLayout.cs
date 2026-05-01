using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using SharedHelpers = BlazorDrawFBP.Shared.Shared;

namespace BlazorDrawFBP.Models;

public static class CapnpFbpPortLayout
{
    public const double PortSizePx = 20d;
    public const double PortSpacingPaddingPx = 12d;
    public const double MinPortSpacingPx = PortSizePx + PortSpacingPaddingPx;
    public const double PortCornerClearancePx = 4d;
    public const double PortCornerKeepOutPx = (PortSizePx / 2d) + PortCornerClearancePx;

    private const double IntersectionTolerance = 0.001d;
    private const double ServiceBadgeLeftPx = 3d;
    private const double ServiceBadgeBaseWidthPx = 30d;
    private const double ServiceBadgeCharacterWidthPx = 7d;
    private const double ServiceBadgeMinWidthPx = 72d;
    private const double ServiceBadgeMaxWidthRatio = 0.55d;
    private const double ServiceBadgeClearancePx = 10d;

    private static readonly FieldInfo? AlignmentField = typeof(PortModel).GetField(
        "<Alignment>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    public readonly record struct PortPlacement(PortAlignment Alignment, double OffsetPx)
    {
        public string ToStyle()
        {
            var offset = OffsetPx.ToString("0.###", CultureInfo.InvariantCulture);
            return Alignment switch
            {
                PortAlignment.Left or PortAlignment.Right => $"top: {offset}px;",
                PortAlignment.Top or PortAlignment.Bottom => $"left: {offset}px;",
                _ => string.Empty,
            };
        }
    }

    public static IReadOnlyDictionary<string, PortPlacement> Calculate(NodeModel node)
    {
        var ports = node.Ports.OfType<CapnpFbpPortModel>().ToList();
        if (ports.Count == 0)
            return new Dictionary<string, PortPlacement>();

        var nodeWidth = Math.Max(node.Size?.Width ?? SharedHelpers.CardWidth, PortSizePx);
        var nodeHeight = Math.Max(node.Size?.Height ?? SharedHelpers.CardHeight, PortSizePx);
        var space = CreatePerimeterSpace(node, nodeWidth, nodeHeight);
        var stablePorts = ports
            .OrderBy(port => port.OrderNo)
            .ThenBy(port => port.ThePortType)
            .ThenBy(port => port.Id)
            .ToList();
        var homeOffsets = CreateHomeOffsets(stablePorts.Count, space.AvailableLength);
        var descriptors = stablePorts
            .Select(
                (port, index) =>
                    CreateDescriptor(node, port, nodeWidth, nodeHeight, space, homeOffsets[index])
            )
            .ToList();

        var placements = new Dictionary<string, PortPlacement>(ports.Count);
        AssignPlacements(descriptors, nodeWidth, nodeHeight, space, placements);
        return placements;
    }

    public static IReadOnlyDictionary<string, PortPlacement> Apply(
        NodeModel node,
        bool refreshPorts = true
    )
    {
        var placements = Calculate(node);
        if (placements.Count == 0)
            return placements;

        var nodeWidth = Math.Max(node.Size?.Width ?? SharedHelpers.CardWidth, PortSizePx);
        var nodeHeight = Math.Max(node.Size?.Height ?? SharedHelpers.CardHeight, PortSizePx);

        foreach (var port in node.Ports.OfType<CapnpFbpPortModel>())
        {
            if (!placements.TryGetValue(port.Id, out var placement))
                continue;

            SynchronizeGeometry(port, node, placement, nodeWidth, nodeHeight);
            if (refreshPorts)
                port.RefreshAll();
        }

        return placements;
    }

    private static PortDescriptor CreateDescriptor(
        NodeModel currentNode,
        CapnpFbpPortModel port,
        double nodeWidth,
        double nodeHeight,
        PerimeterSpace space,
        double homeAvailableOffset
    )
    {
        var preferredAvailableOffset = GetDefaultPreferredAvailableOffset(
            port,
            nodeWidth,
            nodeHeight,
            space,
            homeAvailableOffset
        );
        var connectedNodes = GetConnectedNodes(port).Distinct().ToList();

        if (connectedNodes.Count > 0)
        {
            var (nodeCenterX, nodeCenterY) = GetNodeCenter(currentNode);
            var otherCenters = connectedNodes.Select(GetNodeCenter).ToList();
            var centroidX = otherCenters.Average(center => center.X);
            var centroidY = otherCenters.Average(center => center.Y);
            var localPoint = GetPerimeterIntersection(
                currentNode,
                nodeWidth,
                nodeHeight,
                nodeCenterX,
                nodeCenterY,
                centroidX,
                centroidY
            );

            if (localPoint != null)
            {
                var physicalPerimeterOffset = ToPhysicalPerimeterOffset(
                    localPoint.Value.X,
                    localPoint.Value.Y,
                    nodeWidth,
                    nodeHeight
                );
                preferredAvailableOffset = space.ToAvailable(
                    physicalPerimeterOffset,
                    homeAvailableOffset
                );
            }
        }

        return new PortDescriptor(port, homeAvailableOffset, preferredAvailableOffset);
    }

    private static void AssignPlacements(
        IReadOnlyList<PortDescriptor> descriptors,
        double nodeWidth,
        double nodeHeight,
        PerimeterSpace space,
        IDictionary<string, PortPlacement> placements
    )
    {
        if (descriptors.Count == 0)
            return;

        var orderedDescriptors = descriptors
            .OrderBy(descriptor => Normalize(descriptor.PreferredAvailableOffset, space.AvailableLength))
            .ThenBy(descriptor => descriptor.HomeAvailableOffset)
            .ThenBy(descriptor => descriptor.Port.OrderNo)
            .ThenBy(descriptor => descriptor.Port.ThePortType)
            .ThenBy(descriptor => descriptor.Port.Id)
            .ToList();
        var cutOffset = FindCutOffset(orderedDescriptors, space.AvailableLength);
        var linearizedDescriptors = orderedDescriptors
            .Select(
                descriptor =>
                {
                    var homeLinearOffset = Normalize(
                        descriptor.HomeAvailableOffset - cutOffset,
                        space.AvailableLength
                    );
                    var preferredLinearOffset = Normalize(
                        descriptor.PreferredAvailableOffset - cutOffset,
                        space.AvailableLength
                    );
                    return new LinearizedPortDescriptor(
                        descriptor.Port,
                        homeLinearOffset,
                        preferredLinearOffset
                    );
                }
            )
            .OrderBy(descriptor => descriptor.PreferredLinearOffset)
            .ThenBy(descriptor => descriptor.HomeLinearOffset)
            .ThenBy(descriptor => descriptor.Port.OrderNo)
            .ThenBy(descriptor => descriptor.Port.ThePortType)
            .ThenBy(descriptor => descriptor.Port.Id)
            .ToList();

        var positions = SolveLinearOffsets(linearizedDescriptors, space.AvailableLength);

        for (var i = 0; i < linearizedDescriptors.Count; i++)
        {
            var descriptor = linearizedDescriptors[i];
            var availableOffset = Normalize(positions[i] + cutOffset, space.AvailableLength);
            var physicalPerimeterOffset = space.ToPhysical(availableOffset);
            placements[descriptor.Port.Id] = ToPlacement(
                physicalPerimeterOffset,
                nodeWidth,
                nodeHeight
            );
        }
    }

    private static double[] SolveLinearOffsets(
        IReadOnlyList<LinearizedPortDescriptor> descriptors,
        double availableLength
    )
    {
        if (descriptors.Count == 0)
            return [];

        var maxLinearOffset = Math.Max(0d, availableLength - MinPortSpacingPx);
        if (descriptors.Count == 1)
        {
            return [Math.Clamp(descriptors[0].PreferredLinearOffset, 0d, maxLinearOffset)];
        }

        if ((descriptors.Count * MinPortSpacingPx) > availableLength + IntersectionTolerance)
            return DistributeEvenly(descriptors.Count, maxLinearOffset);

        var upperBound = Math.Max(0d, availableLength - (descriptors.Count * MinPortSpacingPx));
        var transformedOffsets = descriptors
            .Select(
                (descriptor, index) => descriptor.PreferredLinearOffset - (index * MinPortSpacingPx)
            )
            .ToArray();
        var adjustedOffsets = RunIsotonicRegression(transformedOffsets);
        var positions = new double[descriptors.Count];

        for (var i = 0; i < adjustedOffsets.Length; i++)
        {
            adjustedOffsets[i] = Math.Clamp(adjustedOffsets[i], 0d, upperBound);
            positions[i] = adjustedOffsets[i] + (i * MinPortSpacingPx);
        }

        return positions;
    }

    private static double[] DistributeEvenly(int count, double maxLinearOffset)
    {
        if (count <= 0)
            return [];

        var positions = new double[count];
        if (count == 1)
        {
            positions[0] = maxLinearOffset / 2d;
            return positions;
        }

        var step = maxLinearOffset / (count - 1);
        for (var i = 0; i < count; i++)
        {
            positions[i] = step * i;
        }

        return positions;
    }

    private static double[] RunIsotonicRegression(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return [];

        var starts = new List<int>();
        var ends = new List<int>();
        var weights = new List<int>();
        var means = new List<double>();

        for (var i = 0; i < values.Count; i++)
        {
            starts.Add(i);
            ends.Add(i);
            weights.Add(1);
            means.Add(values[i]);

            while (means.Count > 1 && means[^2] > means[^1] + IntersectionTolerance)
            {
                var lastIndex = means.Count - 1;
                var mergedWeight = weights[lastIndex - 1] + weights[lastIndex];
                var mergedMean =
                    ((means[lastIndex - 1] * weights[lastIndex - 1])
                        + (means[lastIndex] * weights[lastIndex]))
                    / mergedWeight;

                ends[lastIndex - 1] = ends[lastIndex];
                weights[lastIndex - 1] = mergedWeight;
                means[lastIndex - 1] = mergedMean;

                starts.RemoveAt(lastIndex);
                ends.RemoveAt(lastIndex);
                weights.RemoveAt(lastIndex);
                means.RemoveAt(lastIndex);
            }
        }

        var result = new double[values.Count];
        for (var blockIndex = 0; blockIndex < means.Count; blockIndex++)
        {
            for (var i = starts[blockIndex]; i <= ends[blockIndex]; i++)
            {
                result[i] = means[blockIndex];
            }
        }

        return result;
    }

    private static double FindCutOffset(
        IReadOnlyList<PortDescriptor> descriptors,
        double availableLength
    )
    {
        if (descriptors.Count == 0 || availableLength <= 0d)
            return 0d;

        var orderedOffsets = descriptors
            .Select(descriptor => Normalize(descriptor.PreferredAvailableOffset, availableLength))
            .OrderBy(offset => offset)
            .ToList();
        if (orderedOffsets.Count == 1)
            return orderedOffsets[0];

        var largestGap = double.MinValue;
        var cutOffset = orderedOffsets[0];
        for (var i = 0; i < orderedOffsets.Count; i++)
        {
            var currentOffset = orderedOffsets[i];
            var nextOffset =
                i == orderedOffsets.Count - 1
                    ? orderedOffsets[0] + availableLength
                    : orderedOffsets[i + 1];
            var gap = nextOffset - currentOffset;
            if (gap <= largestGap)
                continue;

            largestGap = gap;
            cutOffset = Normalize(currentOffset + (gap / 2d), availableLength);
        }

        return cutOffset;
    }

    private static void SynchronizeGeometry(
        CapnpFbpPortModel port,
        NodeModel node,
        PortPlacement placement,
        double nodeWidth,
        double nodeHeight
    )
    {
        SynchronizeAlignment(port, placement.Alignment);
        port.LayoutAlignment = placement.Alignment;
        port.LayoutOffsetPx = placement.OffsetPx;
        port.Size = new Size(PortSizePx, PortSizePx);
        port.Position = CreatePortPosition(node, placement, nodeWidth, nodeHeight);
        port.Initialized = true;
    }

    private static void SynchronizeAlignment(CapnpFbpPortModel port, PortAlignment alignment)
    {
        if (AlignmentField != null && port.Alignment != alignment)
        {
            AlignmentField.SetValue(port, alignment);
        }
    }

    private static double[] CreateHomeOffsets(int count, double availableLength)
    {
        if (count <= 0)
            return [];
        if (count == 1)
            return [availableLength / 2d];

        var step = availableLength / count;
        var offsets = new double[count];
        for (var i = 0; i < count; i++)
        {
            offsets[i] = (i + 0.5d) * step;
        }

        return offsets;
    }

    private static double GetDefaultPreferredAvailableOffset(
        CapnpFbpPortModel port,
        double nodeWidth,
        double nodeHeight,
        PerimeterSpace space,
        double homeAvailableOffset
    )
    {
        var physicalPerimeterOffset = port.ThePortType switch
        {
            CapnpFbpPortModel.PortType.In => ToPhysicalPerimeterOffset(
                0d,
                nodeHeight / 2d,
                nodeWidth,
                nodeHeight
            ),
            CapnpFbpPortModel.PortType.Out => ToPhysicalPerimeterOffset(
                nodeWidth,
                nodeHeight / 2d,
                nodeWidth,
                nodeHeight
            ),
            _ => ToPhysicalPerimeterOffset(nodeWidth / 2d, 0d, nodeWidth, nodeHeight),
        };

        return space.ToAvailable(physicalPerimeterOffset, homeAvailableOffset);
    }

    private static (double X, double Y)? GetPerimeterIntersection(
        NodeModel node,
        double nodeWidth,
        double nodeHeight,
        double originX,
        double originY,
        double targetX,
        double targetY
    )
    {
        var deltaX = targetX - originX;
        var deltaY = targetY - originY;
        if (Math.Abs(deltaX) < IntersectionTolerance && Math.Abs(deltaY) < IntersectionTolerance)
            return null;

        var left = node.Position?.X ?? 0d;
        var top = node.Position?.Y ?? 0d;
        var right = left + nodeWidth;
        var bottom = top + nodeHeight;
        var intersections = new List<(double T, double X, double Y)>();

        if (Math.Abs(deltaX) >= IntersectionTolerance)
        {
            AddIntersection(
                intersections,
                (left - originX) / deltaX,
                left,
                originY,
                deltaY,
                top,
                bottom,
                true
            );
            AddIntersection(
                intersections,
                (right - originX) / deltaX,
                right,
                originY,
                deltaY,
                top,
                bottom,
                true
            );
        }

        if (Math.Abs(deltaY) >= IntersectionTolerance)
        {
            AddIntersection(
                intersections,
                (top - originY) / deltaY,
                top,
                originX,
                deltaX,
                left,
                right,
                false
            );
            AddIntersection(
                intersections,
                (bottom - originY) / deltaY,
                bottom,
                originX,
                deltaX,
                left,
                right,
                false
            );
        }

        if (intersections.Count == 0)
            return null;

        var intersection = intersections.OrderBy(candidate => candidate.T).First();
        return (intersection.X - left, intersection.Y - top);
    }

    private static void AddIntersection(
        ICollection<(double T, double X, double Y)> intersections,
        double t,
        double fixedCoordinate,
        double originCoordinate,
        double delta,
        double min,
        double max,
        bool verticalBoundary
    )
    {
        if (t <= 0d)
            return;

        var variableCoordinate = originCoordinate + (t * delta);
        if (
            variableCoordinate < min - IntersectionTolerance
            || variableCoordinate > max + IntersectionTolerance
        )
            return;

        if (verticalBoundary)
        {
            intersections.Add((t, fixedCoordinate, Math.Clamp(variableCoordinate, min, max)));
            return;
        }

        intersections.Add((t, Math.Clamp(variableCoordinate, min, max), fixedCoordinate));
    }

    private static PortPlacement ToPlacement(
        double physicalPerimeterOffset,
        double nodeWidth,
        double nodeHeight
    )
    {
        var normalizedOffset = Normalize(
            physicalPerimeterOffset,
            2d * (nodeWidth + nodeHeight)
        );

        if (normalizedOffset < nodeWidth)
            return new PortPlacement(PortAlignment.Top, normalizedOffset);

        normalizedOffset -= nodeWidth;
        if (normalizedOffset < nodeHeight)
            return new PortPlacement(PortAlignment.Right, normalizedOffset);

        normalizedOffset -= nodeHeight;
        if (normalizedOffset < nodeWidth)
            return new PortPlacement(PortAlignment.Bottom, nodeWidth - normalizedOffset);

        normalizedOffset -= nodeWidth;
        return new PortPlacement(PortAlignment.Left, nodeHeight - normalizedOffset);
    }

    private static double ToPhysicalPerimeterOffset(
        double localX,
        double localY,
        double nodeWidth,
        double nodeHeight
    )
    {
        var x = Math.Clamp(localX, 0d, nodeWidth);
        var y = Math.Clamp(localY, 0d, nodeHeight);
        if (y <= IntersectionTolerance)
            return x;
        if (x >= nodeWidth - IntersectionTolerance)
            return nodeWidth + y;
        if (y >= nodeHeight - IntersectionTolerance)
            return nodeWidth + nodeHeight + (nodeWidth - x);
        return (2d * nodeWidth) + nodeHeight + (nodeHeight - y);
    }

    private static Point CreatePortPosition(
        NodeModel node,
        PortPlacement placement,
        double nodeWidth,
        double nodeHeight
    )
    {
        var nodeX = node.Position?.X ?? 0d;
        var nodeY = node.Position?.Y ?? 0d;
        var halfPortSize = PortSizePx / 2d;

        return placement.Alignment switch
        {
            PortAlignment.Top => new Point(
                nodeX + placement.OffsetPx - halfPortSize,
                nodeY - halfPortSize
            ),
            PortAlignment.Right => new Point(
                nodeX + nodeWidth - halfPortSize,
                nodeY + placement.OffsetPx - halfPortSize
            ),
            PortAlignment.Bottom => new Point(
                nodeX + placement.OffsetPx - halfPortSize,
                nodeY + nodeHeight - halfPortSize
            ),
            PortAlignment.Left => new Point(
                nodeX - halfPortSize,
                nodeY + placement.OffsetPx - halfPortSize
            ),
            _ => new Point(nodeX, nodeY),
        };
    }

    private static IEnumerable<NodeModel> GetConnectedNodes(CapnpFbpPortModel port)
    {
        foreach (var link in port.Links.OfType<RememberCapnpPortsLinkModel>())
        {
            if (ReferenceEquals(link.OutPortModel, port))
            {
                yield return link.InPortModel.Parent;
            }
            else if (ReferenceEquals(link.InPortModel, port))
            {
                yield return link.OutPortModel.Parent;
            }
        }
    }

    private static (double X, double Y) GetNodeCenter(NodeModel node)
    {
        var width = node.Size?.Width ?? SharedHelpers.CardWidth;
        var height = node.Size?.Height ?? SharedHelpers.CardHeight;
        var x = node.Position?.X ?? 0d;
        var y = node.Position?.Y ?? 0d;
        return (x + (width / 2d), y + (height / 2d));
    }

    private static PerimeterSpace CreatePerimeterSpace(
        NodeModel node,
        double nodeWidth,
        double nodeHeight
    )
    {
        var physicalLength = 2d * (nodeWidth + nodeHeight);
        var intervals = new List<PerimeterInterval>();
        AddCornerIntervals(intervals, nodeWidth, nodeHeight);
        if (TryGetBadgeInterval(node, nodeWidth, out var badgeInterval))
            intervals.Add(badgeInterval);

        var space = new PerimeterSpace(physicalLength, intervals);
        return space.AvailableLength > 0d ? space : new PerimeterSpace(physicalLength, []);
    }

    private static bool TryGetBadgeInterval(
        NodeModel node,
        double nodeWidth,
        out PerimeterInterval interval
    )
    {
        interval = default;
        if (node is not CapnpFbpComponentModel component || component.Editor == null)
            return false;

        var serviceName = component.Editor.GetComponentServiceName(component.ComponentServiceId) ?? "";
        var badgeWidth = Math.Clamp(
            ServiceBadgeBaseWidthPx + (serviceName.Length * ServiceBadgeCharacterWidthPx),
            ServiceBadgeMinWidthPx,
            nodeWidth * ServiceBadgeMaxWidthRatio
        );
        var start = 0d;
        var end = Math.Min(nodeWidth, ServiceBadgeLeftPx + badgeWidth + ServiceBadgeClearancePx);
        if (end - start <= IntersectionTolerance)
            return false;

        interval = new PerimeterInterval(start, end);
        return true;
    }

    private static void AddCornerIntervals(
        ICollection<PerimeterInterval> intervals,
        double nodeWidth,
        double nodeHeight
    )
    {
        var horizontalKeepOut = Math.Min(PortCornerKeepOutPx, nodeWidth / 2d);
        var verticalKeepOut = Math.Min(PortCornerKeepOutPx, nodeHeight / 2d);

        AddInterval(intervals, 0d, horizontalKeepOut);
        AddInterval(intervals, nodeWidth - horizontalKeepOut, nodeWidth);

        AddInterval(intervals, nodeWidth, nodeWidth + verticalKeepOut);
        AddInterval(intervals, nodeWidth + nodeHeight - verticalKeepOut, nodeWidth + nodeHeight);

        AddInterval(intervals, nodeWidth + nodeHeight, nodeWidth + nodeHeight + horizontalKeepOut);
        AddInterval(intervals, (2d * nodeWidth) + nodeHeight - horizontalKeepOut, (2d * nodeWidth) + nodeHeight);

        AddInterval(intervals, (2d * nodeWidth) + nodeHeight, (2d * nodeWidth) + nodeHeight + verticalKeepOut);
        AddInterval(
            intervals,
            (2d * (nodeWidth + nodeHeight)) - verticalKeepOut,
            2d * (nodeWidth + nodeHeight)
        );
    }

    private static void AddInterval(
        ICollection<PerimeterInterval> intervals,
        double start,
        double end
    )
    {
        if (end - start <= IntersectionTolerance)
            return;

        intervals.Add(new PerimeterInterval(start, end));
    }

    private static double Normalize(double offset, double length)
    {
        if (length <= 0d)
            return 0d;

        var normalizedOffset = offset % length;
        return normalizedOffset < 0d ? normalizedOffset + length : normalizedOffset;
    }

    private sealed record PortDescriptor(
        CapnpFbpPortModel Port,
        double HomeAvailableOffset,
        double PreferredAvailableOffset
    );

    private sealed record LinearizedPortDescriptor(
        CapnpFbpPortModel Port,
        double HomeLinearOffset,
        double PreferredLinearOffset
    );

    private readonly record struct PerimeterInterval(double Start, double End)
    {
        public double Length => Math.Max(0d, End - Start);

        public bool Contains(double offset) => offset >= Start && offset <= End;
    }

    private sealed class PerimeterSpace(double physicalLength, IReadOnlyList<PerimeterInterval> intervals)
    {
        private readonly List<PerimeterInterval> _intervals = MergeIntervals(intervals);

        public double PhysicalLength { get; } = physicalLength;

        public double AvailableLength =>
            Math.Max(0d, PhysicalLength - _intervals.Sum(interval => interval.Length));

        public double ToAvailable(double physicalOffset, double referenceAvailableOffset)
        {
            var normalizedPhysicalOffset = Normalize(physicalOffset, PhysicalLength);
            var containingInterval = _intervals.FirstOrDefault(
                interval => interval.Contains(normalizedPhysicalOffset)
            );

            if (containingInterval.Length <= IntersectionTolerance)
                return PhysicalToAvailable(normalizedPhysicalOffset);

            var beforeAvailableOffset =
                containingInterval.Start <= IntersectionTolerance
                    ? Math.Max(0d, AvailableLength - IntersectionTolerance)
                    : PhysicalToAvailable(containingInterval.Start);
            var afterAvailableOffset = PhysicalToAvailable(containingInterval.End);

            return Math.Abs(beforeAvailableOffset - referenceAvailableOffset)
                <= Math.Abs(afterAvailableOffset - referenceAvailableOffset)
                ? beforeAvailableOffset
                : afterAvailableOffset;
        }

        public double ToPhysical(double availableOffset)
        {
            if (AvailableLength <= 0d)
                return 0d;

            var physicalOffset = Normalize(availableOffset, AvailableLength);
            foreach (var interval in _intervals)
            {
                if (physicalOffset >= interval.Start)
                    physicalOffset += interval.Length;
            }

            return Normalize(physicalOffset, PhysicalLength);
        }

        private double PhysicalToAvailable(double physicalOffset)
        {
            var removedLength = 0d;
            foreach (var interval in _intervals)
            {
                if (physicalOffset >= interval.End)
                    removedLength += interval.Length;
            }

            return physicalOffset - removedLength;
        }

        private static List<PerimeterInterval> MergeIntervals(
            IReadOnlyList<PerimeterInterval> intervals
        )
        {
            var orderedIntervals = intervals
                .Where(interval => interval.Length > IntersectionTolerance)
                .OrderBy(interval => interval.Start)
                .ToList();
            if (orderedIntervals.Count == 0)
                return [];

            var mergedIntervals = new List<PerimeterInterval> { orderedIntervals[0] };
            for (var i = 1; i < orderedIntervals.Count; i++)
            {
                var currentInterval = orderedIntervals[i];
                var lastInterval = mergedIntervals[^1];
                if (currentInterval.Start <= lastInterval.End + IntersectionTolerance)
                {
                    mergedIntervals[^1] = new PerimeterInterval(
                        lastInterval.Start,
                        Math.Max(lastInterval.End, currentInterval.End)
                    );
                    continue;
                }

                mergedIntervals.Add(currentInterval);
            }

            return mergedIntervals;
        }
    }
}
