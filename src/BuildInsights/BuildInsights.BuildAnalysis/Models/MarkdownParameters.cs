// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.GitHub.Models;

namespace BuildInsights.BuildAnalysis.Models;

public class MarkdownParameters
{
    public MergedBuildResultAnalysis Analysis { get; }
    public KnownIssueUrlOptions? KnownIssueUrlOptions { get; }
    public MarkdownSummarizeInstructions? SummarizeInstructions { get; }
    public Repository Repository { get; }
    public string SnapshotId { get; }
    public string PullRequest { get; set; }

    public MarkdownParameters(
        MergedBuildResultAnalysis analysis,
        string snapshotId,
        string pullRequest,
        Repository repository,
        KnownIssueUrlOptions? knownIssueUrlOptions = null,
        MarkdownSummarizeInstructions? summarizeInstructions = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        if (repository == null || string.IsNullOrEmpty(repository.Id))
        {
            throw new ArgumentException("Value cannot be null or empty.", nameof(repository.Id));
        }

        if (string.IsNullOrEmpty(snapshotId))
        {
            throw new ArgumentException("Value cannot be null or empty.", nameof(snapshotId));
        }

        Analysis = analysis;
        Repository = repository;
        PullRequest = pullRequest;
        SnapshotId = snapshotId;
        KnownIssueUrlOptions = knownIssueUrlOptions;
        SummarizeInstructions = summarizeInstructions;
    }
}
