// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace DotNet.Status.Web.Controllers
{
    [ApiController]
    [Route("api/alert")]
    public class AlertHookController : ControllerBase
    {
        public const string NotificationIdLabel = "Grafana Alert";
        public const string ActiveAlertLabel = "Active Alert";
        public const string InactiveAlertLabel = "Inactive Alert";
        public const string BodyLabelTextFormat = "Grafana-Automated-Alert-Id-{0}";
        public const string NotificationTagName = "NotificationId";

        private static bool s_labelsCreated;
        private static readonly SemaphoreSlim s_labelLock = new SemaphoreSlim(1);

        private readonly IOptions<GitHubConnectionOptions> _githubOptions;
        private readonly IOptions<GitHubClientOptions> _githubClientOptions;
        private readonly ILogger _logger;
        private readonly IGitHubTokenProvider _tokenProvider;
        private readonly ZenHubClient _zenHub;

        public AlertHookController(
            IGitHubTokenProvider tokenProvider,
            ZenHubClient zenHub,
            IOptions<GitHubConnectionOptions> githubOptions,
            IOptions<GitHubClientOptions> githubClientOptions,
            ILogger<AlertHookController> logger)
        {
            _tokenProvider = tokenProvider;
            _zenHub = zenHub;
            _githubOptions = githubOptions;
            _githubClientOptions = githubClientOptions;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> NotifyAsync(GrafanaNotification notification)
        {
            switch (notification.State)
            {
                case "ok":
                    await CloseExistingNotificationAsync(notification);
                    break;
                case "alerting":
                case "no_data":
                    await OpenNewNotificationAsync(notification);
                    break;
            }

            return NoContent();
        }

        private async Task OpenNewNotificationAsync(GrafanaNotification notification)
        {
            string org = _githubOptions.Value.Organization;
            string repo = _githubOptions.Value.Repository;
            _logger.LogInformation(
                "Alert state detected for {ruleUrl} in stage {ruleState}, porting to github repo {org}/{repo}",
                notification.RuleUrl,
                notification.State,
                org,
                repo);

            IGitHubClient client = await GetGitHubClientAsync(_githubOptions.Value.Organization, _githubOptions.Value.Repository);
            Issue issue = await GetExistingIssueAsync(client, notification);
            await EnsureLabelsAsync(client, org, repo);
            if (issue == null)
            {
                _logger.LogInformation("No existing issue found, creating new active issue with {label}",
                    ActiveAlertLabel);
                issue = await client.Issue.Create(org, repo, GenerateNewIssue(notification));
                _logger.LogInformation("Github issue {org}/{repo}#{issueNumber} created", org, repo, issue.Number);

                NotificationEpicOptions epic = _githubOptions.Value.NotificationEpic;
                if (epic != null)
                {
                    (Repository epicRepoData, Repository issueRepoData) = await Task.WhenAll(
                        client.Repository.Get(org, epic.Repository),
                        client.Repository.Get(org, repo)
                    );

                    _logger.LogInformation("Adding new issue to ZenHub...");
                    await _zenHub.AddIssueToEpicAsync(
                        new ZenHubClient.IssueIdentifier(issueRepoData.Id, issue.Number),
                        new ZenHubClient.IssueIdentifier(epicRepoData.Id, epic.IssueNumber)
                    );
                }
                else
                {
                    _logger.LogInformation("No ZenHub epic configured, skipping...");
                }
            }
            else
            {
                _logger.LogInformation(
                    "Found existing issue {org}/{repo}#{issueNumber}, replacing {inactiveTag} with {activeTag}",
                    org,
                    repo,
                    issue.Number,
                    InactiveAlertLabel,
                    ActiveAlertLabel);

                await GitHubModifications.TryRemoveAsync(() => client.Issue.Labels.RemoveFromIssue(org, repo, issue.Number, InactiveAlertLabel), _logger);
                await GitHubModifications.TryCreateAsync(() =>
                    client.Issue.Labels.AddToIssue(org, repo, issue.Number, new[] {ActiveAlertLabel}),
                    _logger);

                _logger.LogInformation("Adding recurrence comment to  {org}/{repo}#{issueNumber}",
                    org,
                    repo,
                    issue.Number);
                IssueComment comment = await client.Issue.Comment.Create(org,
                    repo,
                    issue.Number,
                    GenerateNewNotificationComment(notification));
                _logger.LogInformation("Created comment {org}/{repo}#{issue}-issuecomment-{comment}",
                    org,
                    repo,
                    issue.Id,
                    comment.Id);
            }
        }

        private string GenerateNewNotificationComment(GrafanaNotification notification)
        {
            var metricText = new StringBuilder();
            foreach (GrafanaNotificationMatch match in notification.EvalMatches)
            {
                metricText.AppendLine($"  - *{match.Metric}* {match.Value}");
            }
            
            string icon = GetIcon(notification);

            return $@":{icon}: Metric state changed to *{notification.State}*

> {notification.Message?.Replace("\n", "\n> ")}

{metricText}

![Metric Graph]({notification.ImageUrl})

[Go to rule]({notification.RuleUrl})".Replace("\r\n","\n");
        }

        private NewIssue GenerateNewIssue(GrafanaNotification notification)
        {
            var metricText = new StringBuilder();
            foreach (GrafanaNotificationMatch match in notification.EvalMatches)
            {
                metricText.AppendLine($"  - *{match.Metric}* {match.Value}");
            }

            string icon = GetIcon(notification);

            string issueTitle = notification.Title;

            GitHubConnectionOptions options = _githubOptions.Value;
            string prefix = options.TitlePrefix;
            if (prefix != null)
            {
                issueTitle = prefix + issueTitle;
            }

            var issue = new NewIssue(issueTitle)
            {
                Body = $@":{icon}: Metric state changed to *{notification.State}*

> {notification.Message?.Replace("\n", "\n> ")}

{metricText}

![Metric Graph]({notification.ImageUrl})

[Go to rule]({notification.RuleUrl})

@{options.NotificationTarget}, please investigate

{options.SupplementalBodyText}

<details>
<summary>Automation information below, do not change</summary>

{string.Format(BodyLabelTextFormat, GetUniqueIdentifier(notification))}

</details>
".Replace("\r\n","\n")
            };

            issue.Labels.Add(NotificationIdLabel);
            issue.Labels.Add(ActiveAlertLabel);
            foreach (string label in options.AlertLabels.OrEmpty())
            {
                issue.Labels.Add(label);
            }

            foreach (string label in options.EnvironmentLabels.OrEmpty())
            {
                issue.Labels.Add(label);
            }

            return issue;
        }

        private static string GetIcon(GrafanaNotification notification)
        {
            string icon;
            switch (notification.State)
            {
                case "ok":
                    icon = "green_heart";
                    break;
                case "alerting":
                    icon = "broken_heart";
                    break;
                case "no_data":
                    icon = "heavy_multiplication_x";
                    break;
                case "paused":
                    icon = "wavy_dash";
                    break;
                default:
                    icon = "grey_question";
                    break;
            }

            return icon;
        }

        private async Task EnsureLabelsAsync(IGitHubClient client, string org, string repo)
        {
            // Assume someone didn't delete the labels, it's an expensive call to make every time
            if (s_labelsCreated)
            {
                return;
            }

            await s_labelLock.WaitAsync();
            try
            {
                if (s_labelsCreated)
                {
                    return;
                }

                var desiredLabels = new[]
                {
                    new NewLabel(NotificationIdLabel, "f957b6"),
                    new NewLabel(ActiveAlertLabel, "d73a4a"),
                    new NewLabel(InactiveAlertLabel, "e4e669"),
                };

                await GitHubModifications.CreateLabelsAsync(client, org, repo, _logger, desiredLabels);

                s_labelsCreated = true;
            }
            finally
            {
                s_labelLock.Release();
            }
        }

        private async Task CloseExistingNotificationAsync(GrafanaNotification notification)
        {
            string org = _githubOptions.Value.Organization;
            string repo = _githubOptions.Value.Repository;
            IGitHubClient client = await GetGitHubClientAsync(org, repo);
            Issue issue = await GetExistingIssueAsync(client, notification);
            if (issue == null)
            {
                _logger.LogInformation("No active issue found for alert '{ruleName}', ignoring", notification.RuleName);
                return;
            }

            _logger.LogInformation(
                "Found existing issue {org}/{repo}#{issueNumber}, replacing {activeTag} with {inactiveTag}",
                org,
                repo,
                issue.Number,
                ActiveAlertLabel,
                InactiveAlertLabel);

            await GitHubModifications.TryRemoveAsync(() => client.Issue.Labels.RemoveFromIssue(org, repo, issue.Number, ActiveAlertLabel), _logger);
            await GitHubModifications.TryCreateAsync(() =>
                client.Issue.Labels.AddToIssue(org, repo, issue.Number, new[] {InactiveAlertLabel}),
                _logger);

            _logger.LogInformation("Adding recurrence comment to  {org}/{repo}#{issueNumber}",
                org,
                repo,
                issue.Number);
            IssueComment comment = await client.Issue.Comment.Create(org,
                repo,
                issue.Number,
                GenerateNewNotificationComment(notification));
            _logger.LogInformation("Created comment {org}/{repo}#{issue}-issuecomment-{comment}",
                org,
                repo,
                issue.Id,
                comment.Id);
        }

        private async Task<Issue> GetExistingIssueAsync(IGitHubClient client, GrafanaNotification notification)
        {
            string id = GetUniqueIdentifier(notification);

            var searchedLabels = new List<string>
            {
                NotificationIdLabel
            };

            searchedLabels.AddRange(_githubOptions.Value.EnvironmentLabels.OrEmpty());

            string automationId = string.Format(BodyLabelTextFormat, id);
            var request = new SearchIssuesRequest(automationId)
            {
                Labels = searchedLabels,
                Order = SortDirection.Descending,
                SortField = IssueSearchSort.Created,
                Type = IssueTypeQualifier.Issue,
                In = new[] {IssueInQualifier.Body},
                State = ItemState.Open,
            };

            SearchIssuesResult issues = await client.Search.SearchIssues(request);

            return issues.Items.FirstOrDefault();
        }

        private static string GetUniqueIdentifier(GrafanaNotification notification)
        {
            string id = null;
            if (notification.Tags?.TryGetValue(NotificationTagName, out id) ?? false)
            {
                return id;
            }

            return notification.RuleId.ToString();
        }

        private async Task<IGitHubClient> GetGitHubClientAsync(string org, string repo)
        {
            return new GitHubClient(_githubClientOptions.Value.ProductHeader)
            {
                Credentials = new Credentials(await _tokenProvider.GetTokenForRepository(org, repo))
            };
        }
    }
}
