// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebHooks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace DotNet.Status.Web.Controllers
{
    public class GitHubHookController : ControllerBase
    {
        private readonly Lazy<Task> _ensureLabels;
        private readonly IOptions<GitHubConnectionOptions> _githubOptions;
        private readonly ILogger<GitHubHookController> _logger;
        private readonly IGitHubClientFactory _gitHubClientFactory;

        public GitHubHookController(
            IOptions<GitHubConnectionOptions> githubOptions,
            IGitHubClientFactory gitHubClientFactory,
            ILogger<GitHubHookController> logger)
        {
            _githubOptions = githubOptions;
            _logger = logger;
            _gitHubClientFactory = gitHubClientFactory;
            _ensureLabels = new Lazy<Task>(EnsureLabelsAsync);
        }

        private async Task EnsureLabelsAsync()
        {
            GitHubConnectionOptions options = _githubOptions.Value;
            IGitHubClient client = await _gitHubClientFactory.CreateGitHubClientAsync(options.Organization, options.Repository);
            await GitHubModifications.TryCreateAsync(
                () => client.Issue.Labels.Create(
                    options.Organization,
                    options.Repository,
                    new NewLabel(_githubOptions.Value.RcaLabel, "009999")),
                _logger
            );
        }

        [GitHubWebHook(EventName = "issues")]
        public async Task<IActionResult> IssuesHook(IssuesHookData data)
        {
            string action = data.Action;
            _logger.LogInformation("Processing issues action '{action}' for issue {repo}/{number}", data.Action, data.Repository.Name, data.Issue.Number);

            await ProcessRcaRulesAsync(data, action);

            return NoContent();
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
            IGitHubClient client = await _gitHubClientFactory.CreateGitHubClientAsync(options.Organization, options.Repository);

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

    public class IssuesHookData
    {
        public string Action { get; set; }
        public IssuesHookIssue Issue { get; set; }
        public IssuesHookUser Sender { get; set; }
        public IssuesHookRepository Repository { get; set; }
        public IssuesHookLabel Label { get; set; }
    }

    public class IssuesHookRepository
    {
        public string Name { get; set; }
        public IssuesHookUser Owner { get; set; }
    }

    public class IssuesHookIssue
    {
        public int Number { get; set; }
        public string Title { get; set; }
        public IssuesHookUser Assignee { get; set; }
        public ImmutableArray<IssuesHookLabel> Labels { get; set; }
        public ItemState State { get; set; }
    }

    public class IssuesHookLabel
    {
        public string Name { get; set; }
    }

    public class IssuesHookUser
    {
        public string Login { get; set; }
    }
}
