// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Maestro.MergePolicies;

public record PullRequestUpdateSummary
{
    public PullRequestUpdateSummary(
        string url,
        bool? coherencyCheckSuccessful,
        List<CoherencyErrorDetails> coherencyErrors,
        List<DependencyUpdateSummary> requiredUpdates,
        List<SubscriptionUpdateSummary> containedUpdates,
        string headBranch,
        string repoUrl,
        bool isCodeFlowPR)
    {
        Url = url;
        CoherencyCheckSuccessful = coherencyCheckSuccessful;
        CoherencyErrors = coherencyErrors;
        RequiredUpdates = requiredUpdates;
        ContainedUpdates = containedUpdates;
        HeadBranch = headBranch;
        TargetRepoUrl = repoUrl;
        IsCodeFlowPR = isCodeFlowPR;
    }

    public string Url { get; set; }

    /// <summary>
    /// Indicates whether the last coherency update is successful.
    /// </summary>
    public bool? CoherencyCheckSuccessful { get; set; }

    /// <summary>
    /// In case of coherency algorithm failure,
    /// provides a list of dependencies that caused the failure.
    /// </summary>
    public List<CoherencyErrorDetails> CoherencyErrors { get; set; }

    public List<DependencyUpdateSummary> RequiredUpdates { get; set; }

    public List<SubscriptionUpdateSummary> ContainedUpdates { get; set; }

    public string HeadBranch { get; set; }

    public string TargetRepoUrl { get; set; }

    public bool IsCodeFlowPR { get; set; } = false;
}
