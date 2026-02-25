
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace BuildInsights.BuildAnalysis.Models;

public class UserSentimentParameters
{
    public int? BuildId { get; set; }
    public string Repository { get; set; }
    public string CommitHash { get; set; }
    public bool? HasUniqueTestFailures { get; set; }
    public bool? HasUniqueBuildFailures { get; set; }
    public bool? IsRetryWithUniqueTestFailures { get; set; }
    public bool? IsRetryWithUniqueBuildFailures { get; set; }
    public string SnapshotId { get; set; }
    public bool IsEmpty { get; set; }
    public int KnownIssues { get; set; }
}
