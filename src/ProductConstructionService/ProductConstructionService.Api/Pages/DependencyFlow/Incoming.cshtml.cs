// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using Humanizer;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Octokit;

namespace ProductConstructionService.Api.Pages.DependencyFlow;

public class IncomingModel : PageModel
{
    private static readonly Regex _repoParser = new(@"https?://(www\.)?github.com/(?<owner>[A-Za-z0-9-_\.]+)/(?<repo>[A-Za-z0-9-_\.]+)");

    private readonly BuildAssetRegistryContext _context;
    private readonly IGitHubClient _github;
    private readonly ILogger<IncomingModel> _logger;

    public IncomingModel(
        BuildAssetRegistryContext context,
        IGitHubClientFactory gitHubClientFactory,
        IOptions<SlaOptions> slaOptions,
        ILogger<IncomingModel> logger)
    {
        _context = context;
        // We'll only comparing public commits, so we don't need a token.
        _github = gitHubClientFactory.CreateGitHubClient(string.Empty);
        SlaOptions = slaOptions.Value;
        _logger = logger;
    }

    public SlaOptions SlaOptions { get; }

    public IReadOnlyList<IncomingRepo>? IncomingRepositories { get; private set; }
    public RateLimit? CurrentRateLimit { get; private set; }

    public Build? Build { get; private set; }

    public string? ChannelName { get; private set; }

    public async Task<IActionResult> OnGet(int channelId, string owner, string repo)
    {
        var channel = await _context.Channels.FindAsync(channelId);

        if (channel == null)
        {
            return NotFound($"The channel with id '{channelId}' was not found.");
        }

        ChannelName = channel.Name;

        var repoUrl = $"https://github.com/{owner}/{repo}";
        var latest = await _context.Builds
            .Where(b => b.GitHubRepository == repoUrl)
            .OrderByDescending(b => b.DateProduced)
            .FirstOrDefaultAsync();

        if (latest == null)
        {
            return NotFound($"No builds found for repository '{repoUrl}'.");
        }

        var graphList = await _context.GetBuildGraphAsync(latest.Id);
        var graph = graphList.ToDictionary(b => b.Id);
        Build = graph[latest.Id];

        if (Build == null)
        {
            return NotFound($"No builds found for repository '{repoUrl}' in channel '{channel.Name}'.");
        }

        var incoming = new List<IncomingRepo>();
        foreach (var dep in Build.DependentBuildIds)
        {
            var lastConsumedBuildOfDependency = graph[dep.BuildId];

            if (lastConsumedBuildOfDependency == null)
            {
                _logger.LogWarning("Failed to find build with id '{BuildId}' in the graph", dep.BuildId);
                continue;
            }

            var gitHubInfo = GetGitHubInfo(lastConsumedBuildOfDependency);

            if (!IncludeRepo(gitHubInfo))
            {
                continue;
            }

            var (commitDistance, commitAge) = await GetCommitInfo(gitHubInfo, lastConsumedBuildOfDependency);

            var oldestPublishedButUnconsumedBuild = await GetOldestUnconsumedBuild(lastConsumedBuildOfDependency.Id);

            incoming.Add(new IncomingRepo(
                lastConsumedBuildOfDependency,
                gitHubInfo?.Repo ?? "",
                oldestPublishedButUnconsumedBuild,
                GetCommitUrl(lastConsumedBuildOfDependency),
                GetBuildUrl(lastConsumedBuildOfDependency),
                commitDistance,
                commitAge));
        }
        IncomingRepositories = incoming;

        CurrentRateLimit = _github.GetLastApiInfo()?.RateLimit;

        return Page();
    }

    private async Task<Build?> GetOldestUnconsumedBuild(int lastConsumedBuildOfDependencyId)
    {
        // Note: We fetch `build` again here so that it will have channel information, which it doesn't when coming from the graph :(
        var build = await _context.Builds.Where(b => b.Id == lastConsumedBuildOfDependencyId)
                    .Include(b => b.BuildChannels)
                    .ThenInclude(bc => bc.Channel)
                    .FirstOrDefaultAsync();

        if (build == null)
        {
            return null;
        }

        var channelId = build.BuildChannels.FirstOrDefault(bc => bc.Channel.Classification == "product" || bc.Channel.Classification == "tools")?.ChannelId;
        var publishedBuildsOfDependency = await _context.Builds
            .Include(b => b.BuildChannels)
            .Where(b => b.GitHubRepository == build.GitHubRepository &&
                   b.DateProduced >= build.DateProduced.AddSeconds(-5) &&
                   b.BuildChannels.Any(bc => bc.ChannelId == channelId))
            .OrderByDescending(b => b.DateProduced)
            .ToListAsync();

        var last = publishedBuildsOfDependency.LastOrDefault();
        if (last == null)
        {
            _logger.LogWarning("Last build didn't match last consumed build, treating dependency '{Dependency}' as up to date.", build.GitHubRepository);
            return null;
        }

        if (last.AzureDevOpsBuildId != build.AzureDevOpsBuildId)
        {
            _logger.LogWarning("Last build didn't match last consumed build.");
        }

        return publishedBuildsOfDependency.Count > 1
            ? publishedBuildsOfDependency[^2]
            : null;
    }

    public GitHubInfo? GetGitHubInfo(Build? build)
    {
        GitHubInfo? gitHubInfo = null;
        if (!string.IsNullOrEmpty(build?.GitHubRepository))
        {
            var match = _repoParser.Match(build.GitHubRepository);
            if (match.Success)
            {
                gitHubInfo = new GitHubInfo(
                    match.Groups["owner"].Value,
                    match.Groups["repo"].Value);
            }
        }

        return gitHubInfo;
    }

    public string GetBuildUrl(Build? build)
        => build == null
            ? "(unknown)"
            : $"https://dev.azure.com/{build.AzureDevOpsAccount}/{build.AzureDevOpsProject}/_build/results?buildId={build.AzureDevOpsBuildId}&view=results";

    private static bool IncludeRepo(GitHubInfo? gitHubInfo)
    {
        if (string.Equals(gitHubInfo?.Owner, "dotnet", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(gitHubInfo?.Repo, "blazor", StringComparison.OrdinalIgnoreCase))
        {
            // We don't want to track dependency staleness of the Blazor repo
            // because it's not part of our process of automated dependency PRs.
            return false;
        }

        return true;
    }

    public string GetCommitUrl(Build? build)
    {
        return build switch
        {
            null => "unknown",
            _ => string.IsNullOrEmpty(build.GitHubRepository)
                   ? $"{build.AzureDevOpsRepository}/commits?itemPath=%2F&itemVersion=GC{build.Commit}"
                   : $"{build.GitHubRepository}/commits/{build.Commit}",
        };
    }

    public string GetDateProduced(Build? build)
    {
        return build switch
        {
            null => "unknown",
            _ => build.DateProduced.Humanize()
        };
    }

    private async Task<CompareResult?> GetCommitsBehindAsync(GitHubInfo gitHubInfo, Build build)
    {
        try
        {
            var comparison = await _github.Repository.Commit.Compare(gitHubInfo.Owner, gitHubInfo.Repo, build.Commit, build.GitHubBranch);

            return comparison;
        }
        catch (NotFoundException)
        {
            _logger.LogWarning("Failed to compare commit history for '{owner}/{repo}' between '{commit}' and '{branch}'.",
                gitHubInfo.Owner,
                gitHubInfo.Repo,
                build.Commit,
                build.GitHubBranch);
            return null;
        }
    }

    private async Task<(int? commitDistance, DateTimeOffset? commitAge)> GetCommitInfo(GitHubInfo? gitHubInfo, Build lastConsumedBuild)
    {
        DateTimeOffset? commitAge = null;
        int? commitDistance = null;
        if (gitHubInfo != null)
        {
            var comparison = await GetCommitsBehindAsync(gitHubInfo, lastConsumedBuild);

            // We're using the branch as the "head" so "ahead by" is actually how far the branch (i.e. "master") is
            // ahead of the commit. So it's also how far **behind** the commit is from the branch head.
            commitDistance = comparison?.AheadBy;

            if (comparison != null && comparison.Commits.Count > 0)
            {
                // Follow the first parent starting at the last unconsumed commit until we find the commit directly after our current consumed commit
                var nextCommit = comparison.Commits[^1];
                while (nextCommit.Parents[0].Sha != lastConsumedBuild.Commit)
                {
                    var foundCommit = false;
                    foreach (var commit in comparison.Commits)
                    {
                        if (commit.Sha == nextCommit.Parents[0].Sha)
                        {
                            nextCommit = commit;
                            foundCommit = true;
                            break;
                        }
                    }

                    if (foundCommit == false)
                    {
                        // Happens if there are over 250 commits
                        // We would need to use a paging API to follow commit history over 250 commits
                        _logger.LogDebug("Failed to follow commit parents and find correct commit age. Falling back to the date the build was produced");
                        return (commitDistance, null);
                    }
                }

                commitAge = nextCommit.Commit.Committer.Date;
            }
        }
        return (commitDistance, commitAge);
    }
}
