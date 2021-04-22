// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    public class AugmentedTimelineRecord
    {
        public AugmentedTimelineRecord(int buildId, string timelineId, TimelineRecord raw)
        {
            BuildId = buildId;
            TimelineId = timelineId;
            Raw = raw;
        }

        public int BuildId { get; }
        public string TimelineId { get; }
        public TimelineRecord Raw { get; }
        public string AugmentedOrder { get; set; }
    }
}
