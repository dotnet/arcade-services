// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using QueueInsights.Models;

namespace QueueInsights.Services;

/// <summary>
///     Generates the markdown for Helix queue insights.
/// </summary>
public interface IQueueInsightsMarkdownGenerator
{
    /// <summary>
    ///     Generates markdown text from the given markdown view.
    /// </summary>
    /// <param name="view">The view to generate markdown for.</param>
    /// <returns>The compiled markdown template, based off the view.</returns>
    public string GenerateMarkdown(MarkdownView view);

    /// <summary>
    /// Generates a markdown text explaining that queue insights is pending, as we're waiting to be notified from AzDo about the build status.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="commitHash">The SHA hash of the PR commit.</param>
    /// <param name="pullRequest">The pull request number.</param>
    /// <returns>The compiled markdown.</returns>
    public string GeneratePendingMarkdown(string repo, string commitHash, string pullRequest);
}
