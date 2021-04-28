// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    public class AugmentedTimelineIssue
    {
        public AugmentedTimelineIssue(int buildId, string timelineId, string recordId, int index, TimelineIssue raw)
        {
            BuildId = buildId;
            TimelineId = timelineId;
            RecordId = recordId;
            Index = index;
            Raw = raw;
        }

        public int BuildId { get; }
        public string TimelineId { get; }
        public string RecordId { get; }
        public int Index { get; }
        public TimelineIssue Raw { get; }
        public string AugmentedIndex { get; set; }
        public string Bucket { get; set; }
    }
}
