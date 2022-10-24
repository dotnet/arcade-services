// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;

namespace Microsoft.DotNet.AzureDevOpsTimeline;

public class AugmentedTimelineIssue
{
    public int BuildId { get; init; }
    public string TimelineId { get; init; }
    public string RecordId { get; init; }
    public int Index { get; init; }
    public TimelineIssue Raw { get; init; }
    public string AugmentedIndex { get; set; }
    public string Bucket { get; set; }
}
