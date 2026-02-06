using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis
{
    public class RecordNode
    {
        private RecordNode Parent { get; set; }
        private List<RecordNode> Children { get; } = new List<RecordNode>();
        public TimelineRecord Value { get; set; }

        public static ImmutableList<TimelineRecord> GetTimelineRecordsOrdererByTreeStructure(
            IEnumerable<TimelineRecord> timeline)
        {
            // Ignore timelines without parents (as from canceled and abandoned builds).
            if (timeline.All(t => t.ParentId != null))
            {
                return ImmutableList<TimelineRecord>.Empty;
            }

            RecordNode rootNode = BuildTree(timeline);
            var ordered = new List<RecordNode>();
            DepthFirstTraversal(rootNode, ordered.Add);

            return ordered.Select(n => n.Value).Where(t => t != null).ToImmutableList();
        }

        public static RecordNode BuildTree(IEnumerable<TimelineRecord> timeline)
        {
            IEnumerable<TimelineRecord> timelineRecords = timeline.ToList();
            Dictionary<Guid, RecordNode> nodes =
                timelineRecords.ToDictionary(r => r.Id, r => new RecordNode {Value = r});

            var rootNode = new RecordNode();

            foreach (TimelineRecord timelineRecord in timelineRecords.OrderBy(r => r.Order ?? 0))
            {
                RecordNode recordNode = nodes[timelineRecord.Id];

                if (!timelineRecord.ParentId.HasValue)
                {
                    rootNode.Children.Add(recordNode);
                }
                else if (nodes.TryGetValue(timelineRecord.ParentId.Value, out RecordNode parent))
                {
                    parent.Children.Add(recordNode);
                    recordNode.Parent = parent;
                }
                else
                {
                    throw new ArgumentException(
                        $"Record {recordNode.Value.Id} has parent {timelineRecord.ParentId}, which is not in timeline");
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
}
