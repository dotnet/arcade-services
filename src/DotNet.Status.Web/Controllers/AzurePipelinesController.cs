// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using Issue = Octokit.Issue;

namespace DotNet.Status.Web.Controllers
{
    [ApiController]
    [Route("api/azp")]
    public class AzurePipelinesController : ControllerBase
    {
        private readonly IGitHubApplicationClientFactory _gitHubApplicationClientFactory;
        private readonly IAzureDevOpsClientFactory _azureDevOpsClientFactory;
        private readonly IOptions<BuildMonitorOptions> _options;
        private readonly ILogger<AzurePipelinesController> _logger;
        private readonly Lazy<IAzureDevOpsClient> _clientLazy;
        private readonly Lazy<Task<Dictionary<string, string>>> _projectMapping;

        public AzurePipelinesController(
            IGitHubApplicationClientFactory gitHubApplicationClientFactory,
            IAzureDevOpsClientFactory azureDevOpsClientFactory,
            IOptions<BuildMonitorOptions> options,
            ILogger<AzurePipelinesController> logger)
        {
            _gitHubApplicationClientFactory = gitHubApplicationClientFactory;
            _azureDevOpsClientFactory = azureDevOpsClientFactory;
            _options = options;
            _logger = logger;
            _clientLazy = new Lazy<IAzureDevOpsClient>(BuildAzureDevOpsClient);
            _projectMapping = new Lazy<Task<Dictionary<string,string>>>(GetProjectMappingInternal);
        }

        private IAzureDevOpsClient Client => _clientLazy.Value;

        private IAzureDevOpsClient BuildAzureDevOpsClient()
        {
            BuildMonitorOptions.AzurePipelinesOptions o = _options.Value.Monitor;
            return _azureDevOpsClientFactory.CreateAzureDevOpsClient(o.BaseUrl, o.Organization, o.MaxParallelRequests, o.AccessToken);
        }

        private async Task<Dictionary<string, string>> GetProjectMappingInternal()
        {
            var projects = await Client.ListProjectsAsync();
            return projects.ToDictionary(p => p.Id, p => p.Name);
        }

        private async Task<string> GetProjectNameAsync(string id)
        {
            Dictionary<string, string> mapping = await _projectMapping.Value;
            return mapping.GetValueOrDefault(id);
        }

        [HttpPost]
        [Route("build-complete")]
        [ProducesResponseType((int) HttpStatusCode.NoContent)]
        public async Task<IActionResult> BuildComplete(AzureDevOpsEvent<AzureDevOpsMinimalBuildResource> buildEvent)
        {
            _logger.LogInformation("Build complete notification for build '{buildId}', URL: {buildUrl}",
                buildEvent.Resource.Id,
                buildEvent.Resource.Url);
            string projectName = await GetProjectNameAsync(buildEvent.ResourceContainers.Project.Id);
            if (string.IsNullOrEmpty(projectName))
            {
                _logger.LogWarning(
                    "Unknown project id '{projectId}' in collection '{collectionId}' reported in notification",
                    buildEvent.ResourceContainers.Project.Id,
                    buildEvent.ResourceContainers.Collection.Id
                );
                return NoContent();
            }

            _logger.LogInformation("Resolved build '{buildId}' for project '{project}, fetching build details",
                buildEvent.Resource.Id,
                projectName);
            Build build = await Client.GetBuildAsync(projectName, buildEvent.Resource.Id);

            if (IsIgnoredReason(build) ||
                IsIgnoredStatus(build))
            {
                return NoContent();
            }

            await ProcessBuildNotificationsAsync(build);

            return NoContent();
        }

        private bool IsIgnoredReason(Build build)
        {
            switch (build.Reason)
            {
                case "batchedCI":
                case "buildCompletion":
                case "individualCI":
                case "manual":
                case "schedule":
                case "scheduleForced":
                case "triggered":
                case "userCreated":
                    _logger.LogInformation("Reason '{reason}' detected for build '{buildId}'", build.Reason, build.Id);
                    return false;
                case "pullRequest":
                case "checkInShelveset":
                case "validateShelveset":
                    _logger.LogDebug("Ignored reason code '{reason}' found", build.Reason);
                    return true;
                default:
                    _logger.LogWarning("Undocumented reason code '{reason}' found", build.Reason);
                    return true;
            }
        }

        private bool IsIgnoredStatus(Build build)
        {
            switch (build.Result)
            {
                case "failed":
                case "partiallySucceeded":
                    _logger.LogInformation("Result '{result}' detected for build '{buildId}'", build.Result, build.Id);
                    return false;
                case "canceled":
                case "succeeded":
                    _logger.LogDebug("Ignored result code '{result}' found", build.Result);
                    return true;
                default:
                    _logger.LogWarning("Undocumented result code '{result}' found", build.Result);
                    return true;
            }
        }

        private async Task ProcessBuildNotificationsAsync(Build build)
        {
            const string fullBranchPrefix = "refs/heads/";
            foreach (var monitor in _options.Value.Monitor.Builds)
            {
                if (!string.Equals(build.Project.Name, monitor.Project, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(monitor.DefinitionPath, $"{build.Definition.Path}\\{build.Definition.Name}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (monitor.Branches.All(mb => !string.Equals($"{fullBranchPrefix}{mb}",
                    build.SourceBranch,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (monitor.Tags != null && monitor.Tags.Any() && !(monitor.Tags.Intersect(build.Tags).Any()))
                {
                    // We should only skip processing if tags were specified in the monitor, and none of those tags were found in the build
                    continue;
                }

                string prettyBranch = build.SourceBranch;
                if (prettyBranch.StartsWith(fullBranchPrefix))
                {
                    prettyBranch = prettyBranch.Substring(fullBranchPrefix.Length);
                }

                string prettyTags = (monitor.Tags != null && monitor.Tags.Any()) ? $"{string.Join(", ", build.Tags)}" : "";

                _logger.LogInformation(
                    "Build '{buildNumber}' in project '{projectName}' with definition '{definitionPath}', tags '{prettyTags}', and branch '{branch}' matches monitoring criteria, sending notification",
                    build.BuildNumber,
                    build.Project.Name,
                    build.Definition.Path,
                    prettyTags,
                    build.SourceBranch);
                
                _logger.LogInformation("Fetching timeline messages...");
                string timelineMessage = await BuildTimelineMessage(build);
                
                _logger.LogInformation("Fetching changes messages...");
                string changesMessage = await BuildChangesMessage(build);

                BuildMonitorOptions.IssuesOptions repo = _options.Value.Issues.SingleOrDefault(i => string.Equals(monitor.IssuesId, i.Id, StringComparison.OrdinalIgnoreCase));

                if (repo != null)
                {
                    IGitHubClient github = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(repo.Owner, repo.Name);
                    
                    DateTimeOffset? finishTime = DateTimeOffset.TryParse(build.FinishTime, out var parsedFinishTime) ?parsedFinishTime: (DateTimeOffset?) null;
                    DateTimeOffset? startTime = DateTimeOffset.TryParse(build.StartTime, out var parsedStartTime) ? parsedStartTime:(DateTimeOffset?) null;

                    string timeString = "";
                    string durationString = "";
                    if (finishTime.HasValue)
                    {
                        timeString = finishTime.Value.ToString("R");
                        if (startTime.HasValue)
                        {
                            durationString = ((int) (finishTime.Value - startTime.Value).TotalMinutes) + " minutes";
                        }
                    }

                    string icon = build.Result == "failed" ? ":x:" : ":warning:";

                    string body = @$"Build [#{build.BuildNumber}]({build.Links.Web.Href}) {build.Result}

## {icon} : {build.Project.Name} / {build.Definition.Name} {build.Result}

### Summary
**Finished** - {timeString}
**Duration** - {durationString}
**Requested for** - {build.RequestedFor.DisplayName}
**Reason** - {build.Reason}

### Details

{timelineMessage}

### Changes

{changesMessage}
";
                    string issueTitlePrefix = $"Build failed: {build.Definition.Name}/{prettyBranch} {prettyTags}";

                    if (repo.UpdateExisting)
                    {
                        // There is no way to get the username of our bot directly from the GithubApp with the C# api.
                        // Issue opened in Octokit: https://github.com/octokit/octokit.net/issues/2335
                        // We do, however, have access to the HtmlUrl, which ends with the name of the bot.
                        // Additionally, when the bot opens issues, the username used ends with [bot], which isn't strictly
                        // part of the name anywhere else. So, to get the correct creator name, get the HtmlUrl, grab
                        // the bot's name from it, and append [bot] to that string.
                        string creator = (await github.GitHubApps.GetCurrent()).HtmlUrl.Split("/").Last();
                        RepositoryIssueRequest issueRequest = new RepositoryIssueRequest {
                            Creator = $"{creator}[bot]",
                            State = ItemStateFilter.Open,
                            SortProperty = IssueSort.Created,
                            SortDirection = SortDirection.Descending
                        };

                        foreach (string label in repo.Labels.OrEmpty())
                        {
                            issueRequest.Labels.Add(label);
                        }

                        foreach (string label in monitor.Labels.OrEmpty())
                        {
                            issueRequest.Labels.Add(label);
                        }

                        List<Issue> matchingIssues = (await github.Issue.GetAllForRepository(repo.Owner, repo.Name, issueRequest)).ToList();
                        Issue matchingIssue = matchingIssues.FirstOrDefault(i => i.Title.StartsWith(issueTitlePrefix));

                        if (matchingIssue != null)
                        {
                            // Add a new comment to the issue with the body
                            IssueComment newComment = await github.Issue.Comment.Create(repo.Owner, repo.Name, matchingIssue.Id, body);
                            _logger.LogInformation("Logged comment in {owner}/{repo}#{issueNumber} for build failure", repo.Owner, repo.Name, matchingIssue.Number);

                            return;
                        }
                        else
                        {
                            _logger.LogInformation("Matching issues for {issueTitlePrefix} not found. Creating a new issue.", issueTitlePrefix);
                        }
                    }

                    // Create new issue if repo.UpdateExisting is false or there were no matching issues
                    var newIssue =
                        new NewIssue($"{issueTitlePrefix} #{build.BuildNumber}")
                        {
                            Body = body,
                        };

                    if (!string.IsNullOrEmpty(monitor.Assignee))
                    {
                        newIssue.Assignees.Add(monitor.Assignee);
                    }
                    
                    foreach (string label in repo.Labels.OrEmpty())
                    {
                        newIssue.Labels.Add(label);
                    }

                    foreach (string label in monitor.Labels.OrEmpty())
                    {
                        newIssue.Labels.Add(label);
                    }

                    Issue issue = await github.Issue.Create(repo.Owner, repo.Name, newIssue);

                    _logger.LogInformation("Logged issue {owner}/{repo}#{issueNumber} for build failure", repo.Owner, repo.Name, issue.Number);
                }
                else
                {
                    _logger.LogWarning("Could not find a matching repo for {issuesId}", monitor.IssuesId);
                }
            }
        }

        private async Task<string> BuildChangesMessage(Build build)
        {
            (BuildChange[] changes, int truncatedChangeCount) = await Client.GetBuildChangesAsync(build.Project.Name, build.Id, CancellationToken.None);
            StringBuilder b = new StringBuilder();
            foreach (BuildChange change in changes.OrEmpty())
            {
                string url = change.DisplayUri;

                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute) &&
                    change.Type == "TfsGit" &&
                    Uri.IsWellFormedUriString(change.Location, UriKind.Absolute))
                {
                    // Azure DevOps Repositories don't include a display URL, we need to go fetch it ourselves
                    BuildChangeDetail changeDetail = await Client.GetChangeDetails(change.Location);
                    url = changeDetail.Links.Web.Href;
                }

                if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    b.Append("- [");
                    b.Append(change.Id, 0, Math.Min(8, change.Id.Length));
                    b.Append("](");
                    b.Append(url);
                    b.Append(") - ");
                }
                else
                {
                    b.Append("- ");
                    b.Append(change.Id, 0, Math.Min(8, change.Id.Length));
                    b.Append(" - ");
                }

                b.Append(change.Author.DisplayName);
                b.Append(" - ");
                b.AppendLine(change.Message);
            }

            if (truncatedChangeCount > 0)
            {
                b.Append("- ... and ");
                b.Append(truncatedChangeCount);
                b.AppendLine(" more ...");
            }

            return b.ToString();
        }

        private async Task<string> BuildTimelineMessage(Build build)
        {
            Timeline timeline = await Client.GetTimelineAsync(build.Project.Name, build.Id, CancellationToken.None);
            StringBuilder builder = new StringBuilder();
            IEnumerable<TimelineRecord> stages = timeline?.Records.OrEmpty().Where(r => r.Type == "Stage");
            foreach (TimelineRecord stage in stages.OrEmpty())
            {
                var issues = new List<(TimelineIssue issue, string log)>();
                PopulateIssuesUnder(stage, timeline, issues);

                if (issues.Count == 0)
                {
                    continue;
                }

                builder.Append("#### ");
                builder.AppendLine(stage.Name);

                foreach ((TimelineIssue issue, string log) in issues)
                {
                    switch (issue.Type)
                    {
                        case "error":
                            builder.Append("- :x: - ");
                            break;
                        case "warning":
                            builder.Append("- :warning: - ");
                            break;
                    }

                    if (Uri.IsWellFormedUriString(log, UriKind.Absolute))
                    {
                        builder.Append("[[Log]](");
                        builder.Append(log);
                        builder.Append(") - ");
                    }

                    builder.AppendLine(issue.Message);
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private void PopulateIssuesUnder(TimelineRecord parent, Timeline timeline, List<(TimelineIssue issue, string log)> issues)
        {
            foreach (var child in timeline.Records.Where(r => r.ParentId == parent.Id).OrderBy(r => r.Order))
            {
                if (child.Issues != null)
                {
                    issues.AddRange(child.Issues.Select(i => (i, child.Log.Url)));
                }

                PopulateIssuesUnder(child, timeline, issues);
            }
        }


        /// <summary>
        /// Minimal version of
        ///     https://docs.microsoft.com/en-us/azure/devops/service-hooks/events?view=azure-devops#build.complete
        /// </summary>
        public class AzureDevOpsEvent<T>
        {
            public T Resource { get; set; }
            public AzureDevOpsResourceContainers ResourceContainers { get; set; }
        }

        public class AzureDevOpsResourceContainers
        {
            public HasId Collection { get; set; }
            public HasId Account { get; set; }
            public HasId Project { get; set; }
        }

        public class HasId
        {
            public string Id { get; set; }
        }

        public class AzureDevOpsMinimalBuildResource
        {
            public long Id { get; set; }
            public string Url { get; set; }
        }
    }
}
