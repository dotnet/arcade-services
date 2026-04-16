// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Octokit;
using BuildInsights.ReproTool.Options;
using DarcGitHubClient = Microsoft.DotNet.DarcLib.GitHubClient;
using GitHubClient = Octokit.GitHubClient;

namespace BuildInsights.ReproTool.Operations;

internal sealed class ReproOperation(
    ReproOptions options,
    HttpClient httpClient,
    GitHubClient ghClient,
    ILogger<ReproOperation> logger)
    : Operation(logger, ghClient)
{
    private const long AzurePipelinesAppId = 9426;
    private const string PrMarkerFileName = "pr.md";

    private readonly HttpClient _httpClient = httpClient;

    internal override async Task RunAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(options.TimeoutMinutes));
        CancellationToken cancellationToken = cts.Token;

        OriginalPullRequestInfo originalPullRequest = await GetOriginalPullRequestAsync();
        string reproBranchBaseSha = await GetReproBranchBaseShaAsync(originalPullRequest);

        IReadOnlyList<ReplayBuildReference> builds = await GetBuildsToReplayAsync(originalPullRequest);
        if (builds.Count == 0)
        {
            throw new InvalidOperationException($"No Azure Pipelines build checks were found for {originalPullRequest.Uri}");
        }

        Logger.LogInformation("Found {count} Azure Pipelines build(s) to replay", builds.Count);
        foreach (ReplayBuildReference build in builds)
        {
            Logger.LogInformation("  - {pipeline}: build {buildId} in {organization}/{projectId}",
                build.PipelineName,
                build.BuildId,
                build.Organization,
                build.ProjectId);
        }

        await using var reproBranch = await CreateTmpBranchAsync(ReproRepoName, reproBranchBaseSha, options.SkipCleanup);

        await CreateOrUpdateFileAsync(
            MaestroAuthTestOrgName,
            ReproRepoName,
            reproBranch.Value,
            PrMarkerFileName,
            BuildMarkerFileContents(originalPullRequest, builds),
            "Create Build Insights repro marker");

        await using var reproPr = await CreateReproPullRequestAsync(
            reproBranch.Value,
            BuildPullRequestTitle(originalPullRequest),
            BuildPullRequestDescription(originalPullRequest, builds),
            options.SkipCleanup);

        Logger.LogInformation("Created repro pull request {uri}", reproPr.Value.HtmlUrl);

        CheckRun? preliminaryCheck = await WaitForBuildInsightsCheckAsync(
            MaestroAuthTestOrgName,
            ReproRepoName,
            reproPr.Value.Head.Sha,
            TimeSpan.FromMinutes(2),
            requireCompletion: false,
            cancellationToken);

        if (preliminaryCheck == null)
        {
            Logger.LogWarning("Build Insights did not post the initial in-progress check to the repro PR within the warmup window");
        }
        else
        {
            Logger.LogInformation("Build Insights acknowledged the repro PR with status {status}", preliminaryCheck.Status);
        }

        foreach (ReplayBuildReference build in builds)
        {
            await ReplayBuildCompletedEventAsync(build, cancellationToken);
        }

        CheckRun? completedCheck = await WaitForReplayCompletionAsync(reproPr.Value, originalPullRequest, cancellationToken);
        if (completedCheck != null)
        {
            Logger.LogInformation("Observed Build Insights check completion with conclusion {conclusion}", completedCheck.Conclusion);
        }
        else
        {
            Logger.LogWarning("Timed out waiting for a completed Build Insights check after replaying the build.complete messages");
        }

        if (options.SkipCleanup)
        {
            Logger.LogInformation("Skipping cleanup. The repro PR remains open at {uri}", reproPr.Value.HtmlUrl);
            return;
        }

        Logger.LogInformation("Repro flow finished. Press enter to close the repro PR and delete the temporary branch.");
        Console.ReadLine();
    }

    private static string BuildPullRequestTitle(OriginalPullRequestInfo originalPullRequest)
        => $"[BuildInsights Repro] Replay #{originalPullRequest.Number}: {originalPullRequest.Title}";

    private static string BuildPullRequestDescription(OriginalPullRequestInfo originalPullRequest, IReadOnlyList<ReplayBuildReference> builds)
    {
        StringBuilder description = new();
        description.AppendLine("## Build Insights repro");
        description.AppendLine();
        description.AppendLine($"- Original PR: {originalPullRequest.Uri}");
        description.AppendLine($"- Original head SHA: {originalPullRequest.HeadSha}");
        description.AppendLine($"- Replay requested at: {DateTimeOffset.UtcNow:O}");
        description.AppendLine($"- Builds to replay: {builds.Count}");
        description.AppendLine();
        description.AppendLine("This PR was created by BuildInsights.ReproTool to replay existing Azure DevOps build completion events against a local Build Insights instance.");
        return description.ToString().Trim();
    }

    private static string BuildMarkerFileContents(OriginalPullRequestInfo originalPullRequest, IReadOnlyList<ReplayBuildReference> builds)
    {
        StringBuilder content = new();
        content.AppendLine("# Build Insights repro");
        content.AppendLine();
        content.AppendLine($"Original PR: {originalPullRequest.Uri}");
        content.AppendLine($"Original title: {originalPullRequest.Title}");
        content.AppendLine($"Original head SHA: {originalPullRequest.HeadSha}");
        content.AppendLine($"Replayed at: {DateTimeOffset.UtcNow:O}");
        content.AppendLine();
        content.AppendLine("Builds:");

        foreach (ReplayBuildReference build in builds)
        {
            content.AppendLine($"- {build.PipelineName}: {build.Organization}/{build.ProjectId} build {build.BuildId}");
        }

        return content.ToString();
    }

    private async Task<string> GetReproBranchBaseShaAsync(OriginalPullRequestInfo originalPullRequest)
    {
        string expectedRepository = $"{MaestroAuthTestOrgName}/{ReproRepoName}";
        if (string.Equals(originalPullRequest.RepositoryWithOwner, expectedRepository, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation("Original PR is already in the repro repository, basing the repro branch on the original PR head SHA");
            return originalPullRequest.HeadSha;
        }

        Logger.LogWarning("Original PR repository {repository} differs from the repro repository {expectedRepository}. Falling back to its {targetBranch} branch for the repro branch.",
            originalPullRequest.RepositoryWithOwner,
            expectedRepository,
            DefaultTargetBranch);

        Reference mainBranch = await GitHubClient.Git.Reference.Get(MaestroAuthTestOrgName, ReproRepoName, $"heads/{DefaultTargetBranch}");
        return mainBranch.Object.Sha;
    }

    private async Task<OriginalPullRequestInfo> GetOriginalPullRequestAsync()
    {
        (string owner, string repo, int id) = DarcGitHubClient.ParsePullRequestUri(options.PullRequestUri);

        PullRequest pullRequest = await GitHubClient.PullRequest.Get(owner, repo, id);
        return new OriginalPullRequestInfo(
            options.PullRequestUri,
            owner,
            repo,
            id,
            pullRequest.Title,
            pullRequest.Head.Sha,
            pullRequest.HtmlUrl);
    }

    private async Task<IReadOnlyList<ReplayBuildReference>> GetBuildsToReplayAsync(OriginalPullRequestInfo originalPullRequest)
    {
        CheckRunsResponse response = await GitHubClient.Check.Run.GetAllForReference(
            originalPullRequest.Owner,
            originalPullRequest.Repository,
            originalPullRequest.HeadSha);

        return response.CheckRuns
            .Where(check => check.App?.Id == AzurePipelinesAppId && !string.IsNullOrWhiteSpace(check.ExternalId))
            .Select(ParseReplayBuildOrNull)
            .Where(build => build != null)
            .Cast<ReplayBuildReference>()
            .DistinctBy(build => $"{build.Organization}/{build.ProjectId}/{build.BuildId}")
            .ToList();
    }

    private ReplayBuildReference? ParseReplayBuildOrNull(CheckRun checkRun)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(checkRun.ExternalId))
            {
                return null;
            }

            string[] parts = checkRun.ExternalId.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || !int.TryParse(parts[1], out int buildId))
            {
                return null;
            }

            string? organization = ExtractOrganizationFromCheckRun(checkRun);
            if (string.IsNullOrWhiteSpace(organization))
            {
                return null;
            }

            return new ReplayBuildReference(
                organization,
                parts[2],
                buildId,
                checkRun.Name);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse Azure Pipelines metadata from GitHub check run {checkRunId}", checkRun.Id);
            return null;
        }
    }

    private static string? ExtractOrganizationFromCheckRun(CheckRun checkRun)
    {
        foreach (string? candidate in new[] { checkRun.DetailsUrl, checkRun.Output?.Summary, checkRun.Output?.Text })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            Match match = Regex.Match(candidate, @"https://(?:dev\.azure\.com/([^/\s]+)|([^./\s]+)\.visualstudio\.com)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return !string.IsNullOrWhiteSpace(match.Groups[1].Value)
                    ? match.Groups[1].Value
                    : match.Groups[2].Value;
            }
        }

        return null;
    }

    private async Task ReplayBuildCompletedEventAsync(ReplayBuildReference build, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Replaying build.completed for build {buildId} ({pipeline})", build.BuildId, build.PipelineName);

        var message = new CompletedBuildMessageDto
        {
            EventType = "build.complete",
            Resource = new CompletedBuildResourceDto
            {
                Id = build.BuildId,
                Url = $"https://dev.azure.com/{build.Organization}/{build.ProjectId}/_apis/build/Builds/{build.BuildId}"
            },
            ResourceContainers = new CompletedBuildResourceContainersDto
            {
                Project = new CompletedBuildProjectDto
                {
                    Id = build.ProjectId
                }
            }
        };

        using HttpResponseMessage response = await HttpClientJsonExtensions.PostAsJsonAsync(_httpClient, "azdo/servicehooks/build.complete", message, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Replay request for build {build.BuildId} failed with {(int)response.StatusCode} {response.StatusCode}: {responseText}");
        }
    }

    private async Task<CheckRun?> WaitForReplayCompletionAsync(
        PullRequest reproPullRequest,
        OriginalPullRequestInfo originalPullRequest,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(options.TimeoutMinutes);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CheckRun? reproCheck = await TryGetBuildInsightsCheckAsync(MaestroAuthTestOrgName, ReproRepoName, reproPullRequest.Head.Sha);
            if (reproCheck?.Status == Octokit.CheckStatus.Completed)
            {
                return reproCheck;
            }

            if (!string.Equals(reproPullRequest.Head.Sha, originalPullRequest.HeadSha, StringComparison.OrdinalIgnoreCase))
            {
                CheckRun? originalCheck = await TryGetBuildInsightsCheckAsync(MaestroAuthTestOrgName, ReproRepoName, originalPullRequest.HeadSha);
                if (originalCheck?.Status == Octokit.CheckStatus.Completed)
                {
                    Logger.LogInformation("Observed the completed Build Insights check on the replayed source SHA {sha}", originalPullRequest.HeadSha);
                    return originalCheck;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }

        return null;
    }

    private sealed record OriginalPullRequestInfo(
        string Uri,
        string Owner,
        string Repository,
        int Number,
        string Title,
        string HeadSha,
        string HtmlUrl)
    {
        public string RepositoryWithOwner => $"{Owner}/{Repository}";
    }

    private sealed record ReplayBuildReference(string Organization, string ProjectId, int BuildId, string PipelineName);

    private sealed class CompletedBuildMessageDto
    {
        [JsonPropertyName("eventType")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("resource")]
        public CompletedBuildResourceDto Resource { get; set; } = new();

        [JsonPropertyName("resourceContainers")]
        public CompletedBuildResourceContainersDto ResourceContainers { get; set; } = new();
    }

    private sealed class CompletedBuildResourceDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    private sealed class CompletedBuildResourceContainersDto
    {
        [JsonPropertyName("project")]
        public CompletedBuildProjectDto Project { get; set; } = new();
    }

    private sealed class CompletedBuildProjectDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}
