// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebHooks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using Octokit.Internal;

namespace DotNet.Status.Web.Controllers
{
    public class GitHubHookController : ControllerBase
    {
        private readonly Lazy<Task> _ensureLabels;
        private readonly IOptions<GitHubConnectionOptions> _githubOptions;
        private readonly ILogger<GitHubHookController> _logger;
        private readonly IGitHubApplicationClientFactory _gitHubApplicationClientFactory;
        private readonly ITimelineIssueTriage _timelineIssueTriage;
        private readonly IIssueMentionForwarder _issueMentionForwarder;

        public GitHubHookController(
            IOptions<GitHubConnectionOptions> githubOptions,
            IGitHubApplicationClientFactory gitHubApplicationClientFactory,
            ITimelineIssueTriage timelineIssueTriage,
            ILogger<GitHubHookController> logger,
            IIssueMentionForwarder issueMentionForwarder)
        {
            _githubOptions = githubOptions;
            _logger = logger;
            _issueMentionForwarder = issueMentionForwarder;
            _gitHubApplicationClientFactory = gitHubApplicationClientFactory;
            _timelineIssueTriage = timelineIssueTriage;
            _ensureLabels = new Lazy<Task>(EnsureLabelsAsync);
        }

        private async Task EnsureLabelsAsync()
        {
            GitHubConnectionOptions options = _githubOptions.Value;
            IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(options.Organization, options.Repository);
            await GitHubModifications.TryCreateAsync(
                () => client.Issue.Labels.Create(
                    options.Organization,
                    options.Repository,
                    new NewLabel(_githubOptions.Value.RcaLabel, "009999")),
                _logger
            );
        }

        private readonly SimpleJsonSerializer _serializer = new SimpleJsonSerializer();

        private async Task<T> DeserializeGitHubWebHook<T>()
        {
            // Octokit has a serializer and types built for github rest apis, use them
            string data;
            using (var reader = new StreamReader(Request.Body))
            {
                data = await reader.ReadToEndAsync();
            }

            var payload = _serializer.Deserialize<T>(data);
            return payload;
        }

        [GitHubWebHook(EventName = "pull_request_review_comment")]
        public async Task<IActionResult> PullRequestReviewComment()
        {
            var payload = await DeserializeGitHubWebHook<PullRequestCommentPayloadWithChanges>();

            string repo = payload.Repository.Owner.Login + "/" + payload.Repository.Name;
            int number = payload.PullRequest.Number;
            _logger.LogInformation("Received webhook for pull request {repo}#{number}", repo, number);

            string title = payload.PullRequest.Title;
            string uri = payload.Comment.HtmlUrl;
            string username = payload.PullRequest.User.Login;
            DateTimeOffset date = payload.PullRequest.UpdatedAt;
            try
            {
                bool sent = false;
                switch (payload.Action)
                {
                    case "created":
                        sent = await _issueMentionForwarder.HandleIssueBody(null, payload.Comment.Body, title, uri, username, date);
                        break;
                    case "edited" when !string.IsNullOrEmpty(payload.Changes.Body?.From):
                        sent = await _issueMentionForwarder.HandleIssueBody(payload.Changes.Body.From, payload.Comment.Body, title, uri, username, date);
                        break;
                }

                if (sent)
                {
                    _logger.LogInformation("Sent teams notification for pull request {repo}#{number}", repo, number);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Unable to send teams notification for pull request {repo}#{number}", repo, number);
            }

            return Ok();
        }

        [GitHubWebHook(EventName = "issue_comment")]
        public async Task<IActionResult> IssueComment()
        {
            var payload = await DeserializeGitHubWebHook<IssueCommentPayloadWithChanges>();

            string repo = payload.Repository.Name;
            int number = payload.Issue.Number;
            _logger.LogInformation("Received comment webhook for issue {repo}#{number}", repo, number);

            string title = payload.Issue.Title;
            string uri = payload.Comment.HtmlUrl;
            string username = payload.Issue.User.Login;
            DateTimeOffset date = payload.Issue.UpdatedAt ?? DateTimeOffset.UtcNow;
            try
            {
                bool sent = false;
                switch (payload.Action)
                {
                    case "created":
                        sent = await _issueMentionForwarder.HandleIssueBody(null, payload.Comment.Body, title, uri, username, date);
                        break;
                    case "edited" when !string.IsNullOrEmpty(payload.Changes.Body?.From):
                        sent = await _issueMentionForwarder.HandleIssueBody(payload.Changes.Body.From, payload.Comment.Body, title, uri, username, date);
                        break;
                }

                if (sent)
                {
                    _logger.LogInformation("Sent teams notification for issue {repo}#{number}", repo, number);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Unable to send teams notification for issue {repo}#{number}", repo, number);
            }

            return Ok();
        }

        [GitHubWebHook(EventName = "pull_request")]
        public async Task<IActionResult> PullRequestHook()
        {
            var payload = await DeserializeGitHubWebHook<PullRequestEventPayloadWithChanges>();

            string repo = payload.Repository.Owner.Login + "/" + payload.Repository.Name;
            int number = payload.PullRequest.Number;
            _logger.LogInformation("Received webhook for pull request {repo}#{number}", repo, number);
            try
            {
                string title = payload.PullRequest.Title;
                string uri = payload.PullRequest.HtmlUrl;
                string username = payload.PullRequest.User.Login;
                DateTimeOffset date = payload.PullRequest.UpdatedAt;
                bool sent = false;
                switch (payload.Action)
                {
                    case "opened":
                        sent = await _issueMentionForwarder.HandleIssueBody(null, payload.PullRequest.Body, title, uri, username,
                            date);
                        break;
                    case "edited":
                        sent = await _issueMentionForwarder.HandleIssueBody(payload.Changes.Body.From, payload.PullRequest.Body, title, uri, username, date);
                        break;
                }

                if (sent)
                {
                    _logger.LogInformation("Sent teams notification for pull request {repo}#{number}", repo, number);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Unable to send teams notification for issue {repo}#{number}", repo, number);
            }

            return Ok();
        }

        [GitHubWebHook(EventName = "issues")]
        public async Task<IActionResult> IssuesHook()
        {
            var issueEvent = await DeserializeGitHubWebHook<IssuesHookData>();

            string action = issueEvent.Action;
            _logger.LogInformation("Processing issues action '{action}' for issue {repo}/{number}", issueEvent.Action, issueEvent.Repository.Name, issueEvent.Issue.Number);

            await ProcessNotifications(issueEvent);
            await ProcessRcaRulesAsync(issueEvent, action);
            await ProcessTimelineIssueTriageAsync(issueEvent, action);

            return NoContent();
        }

        private async Task ProcessNotifications(IssuesHookData issueEvent)
        {
            string repo = issueEvent.Repository.Owner.Login + "/" + issueEvent.Repository.Name;
            int number = issueEvent.Issue.Number;
            try
            {
                bool sent = false;
                string title = issueEvent.Issue.Title;
                string uri = issueEvent.Issue.HtmlUrl;
                string username = issueEvent.Issue.User.Login;
                DateTimeOffset date = issueEvent.Issue.UpdatedAt ?? DateTimeOffset.UtcNow;
                switch (issueEvent.Action)
                {
                    case "opened":
                        sent = await _issueMentionForwarder.HandleIssueBody(null, issueEvent.Issue.Body, title, uri, username,
                            date);
                        break;
                    case "edited":
                        sent = await _issueMentionForwarder.HandleIssueBody(issueEvent.Changes.Body.From,
                            issueEvent.Issue.Body, title, uri, username, date);
                        break;
                }

                if (sent)
                {
                    _logger.LogInformation("Sent teams notification for issue {repo}#{number}", repo, number);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Unable to send teams notification for issue {repo}#{number}", repo, number);
            }
        }

        public static JsonSerializerOptions SerializerOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

            return options;
        }

        private async Task ProcessTimelineIssueTriageAsync(IssuesHookData data, string action)
        {
            await _timelineIssueTriage.ProcessIssueEvent(data);
        }

        private async Task ProcessRcaRulesAsync(IssuesHookData data, string action)
        {
            if (!ShouldOpenRcaIssue(data, action, out string triggeringLabel))
            {
                return;
            }

            GitHubConnectionOptions options = _githubOptions.Value;

            int issueNumber = data.Issue.Number;
            string issueTitle = data.Issue.Title;
            string assignee = data.Issue.Assignee?.Login;

            string[] copiedLabels = Array.Empty<string>();

            if (options.RcaCopyLabelPrefixes != null && options.RcaCopyLabelPrefixes.Length > 0)
            {
                copiedLabels = data.Issue
                    .Labels
                    .Select(l => l.Name)
                    .Where(n => options.RcaCopyLabelPrefixes.Any(o =>
                        n.StartsWith(o, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                _logger.LogInformation($"Copying labels: '{string.Join("', '", copiedLabels)}'");
            }

            if (string.IsNullOrEmpty(assignee))
            {
                _logger.LogInformation("Issue was not assigned, using event sender");
                assignee = data.Sender.Login;
            }

            string issueRepo = data.Repository.Name;
            string issueOrg = data.Repository.Owner.Login;
            _logger.LogInformation("Opening connection to open issue to {org}/{repo}", options.Organization, options.Repository);
            IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(options.Organization, options.Repository);

            var issue = new NewIssue($"RCA: {issueTitle} ({issueNumber})")
            {
                Body =
                    $@"An issue, {issueOrg}/{issueRepo}#{issueNumber}, that was marked with the '{triggeringLabel}' label was recently closed.

Please fill out the following root cause analysis, and then close the issue.

Filling it out promptly after resolving an issue ensures things are fresh in your mind.

For help filling out this form, see the [Root Cause Analysis](https://github.com/dotnet/core-eng/wiki/Root-Cause-Analysis).

## Describe the scope of the problem

## Brief description of root cause of the problem

## Diagnostic / Monitoring
### Links to any queries or metrics used for diagnosis

### What additional diagnostics would have reduced the time to fix the issue?

### What additional [telemetry](https://github.com/dotnet/core-eng/blob/master/Documentation/Alerting.md) would have allowed us to catch the issue sooner?

### What additional [testing or validation](https://github.com/dotnet/core-eng/tree/master/Documentation/Validation) would have caught this error before rollout?

",
            };

            if (!string.IsNullOrEmpty(assignee))
            {
                _logger.LogInformation("Setting assignee");
                issue.Assignees.Add(assignee);
            }

            await _ensureLabels.Value;

            if (options.RcaLabel != null)
            {
                _logger.LogTrace("Adding label '{label}'", options.RcaLabel);
                issue.Labels.Add(options.RcaLabel);
            }

            foreach (string toCopy in copiedLabels)
            {
                issue.Labels.Add(toCopy);
            }

            _logger.LogInformation("Sending issue create request...");
            Issue createdIssue = await client.Issue.Create(options.Organization, options.Repository, issue);

            _logger.LogInformation("Created RCA issue {number}", createdIssue.Number);
        }

        private bool ShouldOpenRcaIssue(IssuesHookData data, string action, out string triggeringLabel)
        {
            triggeringLabel = null;
            GitHubConnectionOptions options = _githubOptions.Value;
            if (options.RcaRequestedLabels == null || options.RcaRequestedLabels.Length == 0)
            {
                return false;
            }

            switch (action)
            {
                case "closed":
                    HashSet<string> names = data.Issue.Labels.Select(l => l.Name).ToHashSet();
                    names.IntersectWith(options.RcaRequestedLabels);
                    triggeringLabel = names.FirstOrDefault();
                    if (names.Count == 0)
                    {
                        _logger.LogTrace("Issue {repo}/{number} is closed by has no RCA label, taking no RCA action", data.Repository.Name, data.Issue.Number);
                        return false;
                    }

                    _logger.LogInformation("Issue closed with label '{label}', RCA required", triggeringLabel);
                    return true;

                case "labeled":

                    triggeringLabel = data.Label.Name;
                    if (data.Issue.State == ItemState.Open)
                    {
                        _logger.LogInformation("Issue {repo}/{number} is labeled with {label} but still open, taking no RCA action", data.Repository.Name, data.Issue.Number, triggeringLabel);
                        return false;
                    }

                    if (!options.RcaRequestedLabels.Contains(data.Label.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.LogTrace("Label '{label}' irrelevant, taking no RCA action", triggeringLabel);
                        return false;
                    }
                    
                    _logger.LogInformation("Previously closed labeled with '{label}', RCA required", triggeringLabel);
                    return true;

                default:
                    _logger.LogTrace("Issues hook with '{action}' action, no RCA action taken", action);
                    return false;
            }
        }

        [GitHubWebHook]
        public IActionResult AcceptHook()
        {
            // Ignore them, none are interesting
            return NoContent();
        }
    }
}
