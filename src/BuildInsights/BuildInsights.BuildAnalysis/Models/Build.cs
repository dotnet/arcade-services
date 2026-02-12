// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace BuildInsights.BuildAnalysis.Models;

public class BuildLinks
{
    public BuildLinks(string web, string sourceVersion)
    {
        Web = web;
        SourceVersion = sourceVersion;
    }

    public string Web { get; }
    public string SourceVersion { get; }
}

public class Build
{
    public int Id { get; }
    public string BuildNumber { get; }
    public string Url { get; }
    public string OrganizationName { get; }
    public string ProjectName { get; }
    public string ProjectId { get; }
    public string DefinitionName { get; }
    public int DefinitionId { get; }

    public BuildResult Result { get; }

    public BuildRepository Repository { get; }
    public string CommitHash { get; }
    public string PullRequest { get; }
    public string PullRequestUrl { get; set; }
    public Branch TargetBranch { get; }
    public BuildLinks Links { get; }
    public ImmutableList<BuildValidationResult> ValidationResults { get; }
    public DateTimeOffset? FinishTime { get; }
    public bool IsComplete { get; }
    public string Reason { get; }

    public Build(
        int id = default,
        string buildNumber = null,
        string url = null,
        string projectName = null,
        string projectId = null,
        string definitionName = null,
        int definitionId = default,
        BuildResult result = default,
        string repository = null,
        string repositoryType = null,
        string commitHash = null,
        string pullRequest = null,
        string pullRequestUrl = null,
        Branch targetBranch = null,
        ImmutableDictionary<string, string> links = null,
        ImmutableList<BuildValidationResult> validationResults = null,
        DateTimeOffset? finishTime = default,
        bool isComplete = true,
        string reason = null,
        string organizationName = null)
    {
        Id = id;
        BuildNumber = buildNumber;
        Url = url;
        ProjectName = projectName;
        ProjectId = projectId;
        DefinitionName = definitionName;
        DefinitionId = definitionId;
        Result = result;
        CommitHash = commitHash;
        PullRequest = pullRequest;
        PullRequestUrl = pullRequestUrl;
        TargetBranch = targetBranch;
        Repository = new BuildRepository(repository, repositoryType);
        links ??= ImmutableDictionary<string, string>.Empty;
        Links = new BuildLinks(
            links.GetValueOrDefault("web", null),
            links.GetValueOrDefault("sourceVersionDisplayUri", null)
        );
        ValidationResults = validationResults ?? ImmutableList<BuildValidationResult>.Empty;
        FinishTime = finishTime;
        IsComplete = isComplete;
        Reason = reason;
        OrganizationName = organizationName;
    }
}
