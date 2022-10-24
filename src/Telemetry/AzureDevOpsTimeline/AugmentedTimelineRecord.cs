// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;

namespace Microsoft.DotNet.AzureDevOpsTimeline;

public class AugmentedTimelineRecord
{
    public int BuildId { get; init; }
    public string TimelineId { get; init; }
    public TimelineRecord Raw { get; init; }
    public string AugmentedOrder { get; set; }
    public string ImageName { get; set; }
}
