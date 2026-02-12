// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.BuildAnalysis.Models;

#nullable enable
namespace BuildInsights.BuildAnalysis;

public class RecordNode
{
    private RecordNode? Parent { get; set; }
    private List<RecordNode> Children { get; } = [];
    public TimelineRecord? Value { get; set; }

    public static ImmutableList<TimelineRecord> GetTimelineRecordsOrdererByTreeStructure(
        IEnumerable<TimelineRecord> timeline)
    {
        // Ignore timelines without parents (as from canceled and abandoned builds).
        if (timeline.All(t => t.ParentId != null))
        {
            return [];
        }

        RecordNode rootNode = BuildTree(timeline);
        var ordered = new List<RecordNode>();
        DepthFirstTraversal(rootNode, ordered.Add);

        return [..ordered.Select(n => n.Value).Where(t => t != null).Cast<TimelineRecord>()];
    }

    public static RecordNode BuildTree(IEnumerable<TimelineRecord> timeline)
    {
        IEnumerable<TimelineRecord> timelineRecords = timeline.ToList();
        Dictionary<Guid, RecordNode> nodes =
            timelineRecords.ToDictionary(r => r.Id, r => new RecordNode { Value = r });

        var rootNode = new RecordNode();

        foreach (TimelineRecord timelineRecord in timelineRecords.OrderBy(r => r.Order ?? 0))
        {
            RecordNode recordNode = nodes[timelineRecord.Id];

            if (!timelineRecord.ParentId.HasValue)
            {
                rootNode.Children.Add(recordNode);
            }
            else if (nodes.TryGetValue(timelineRecord.ParentId.Value, out RecordNode? parent))
            {
                parent.Children.Add(recordNode);
                recordNode.Parent = parent;
            }
            else
            {
                throw new ArgumentException(
                    $"Record {recordNode.Value?.Id} has parent {timelineRecord.ParentId}, which is not in timeline");
            }
        }

        return rootNode;
    }

    private static void DepthFirstTraversal(RecordNode node, Action<RecordNode> visit)
    {
        visit(node);

        foreach (RecordNode child in node.Children)
        {
            DepthFirstTraversal(child, visit);
        }
    }
}
