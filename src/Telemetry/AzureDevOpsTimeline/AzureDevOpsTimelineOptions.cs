// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Microsoft.DotNet.AzureDevOpsTimeline;

public class AzureDevOpsProject
{
    public string Organization { get; set; }
    public string Project { get; set; }
}
public class AzureDevOpsTimelineOptions
{
    public List<AzureDevOpsProject> Projects { get; set; }
    public string InitialDelay { get; set; }
    public string Interval { get; set; }
    public string BuildBatchSize { get; set; }
    public string LogScrapingTimeout { get; set; }
}
