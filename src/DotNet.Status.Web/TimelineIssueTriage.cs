// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.


using DotNet.Status.Web.Controllers;
using DotNet.Status.Web.Options;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DotNet.Status.Web
{
    public class TimelineIssueTriage : ITimelineIssueTriage
    {
        private static readonly string _docLink = "[Documentation](https://github.com/dotnet/arcade-services/blob/master/docs/BuildFailuresIssueTriage.md)";
        private static readonly string _markingLabelName = "darcbot";
        private static readonly IList<string> _issueLabels = new[] { "Detected By - Customer", "First Responder", "Build Failed" };

        private readonly ILogger<TimelineIssueTriage> _logger;
        private readonly IGitHubApplicationClientFactory _gitHubApplicationClientFactory;
        private readonly IKustoIngestClientFactory _kustoIngestClientFactory;
        private readonly IOptions<KustoOptions> _kustoOptions;
        private readonly IOptions<GitHubConnectionOptions> _githubOptions;
        private readonly ZenHubClient _zenHub;
        private readonly TimelineIssueTriageInternal _internal;

        public TimelineIssueTriage(
            IGitHubApplicationClientFactory gitHubApplicationClientFactory,
            IKustoIngestClientFactory clientFactory,
            IOptions<KustoOptions> kustoOptions,
            IOptions<GitHubConnectionOptions> githubOptions,
            ZenHubClient zenHub,
            ILogger<TimelineIssueTriage> logger)
        {
            _logger = logger;
            _gitHubApplicationClientFactory = gitHubApplicationClientFactory;
            _kustoIngestClientFactory = clientFactory;
            _kustoOptions = kustoOptions;
            _githubOptions = githubOptions;
            _zenHub = zenHub;
            _internal = new TimelineIssueTriageInternal();
        }

        public async Task ProcessIssueEvent(IssuesHookData issuePayload)
        {
            if (issuePayload.Action != "opened" &&
                issuePayload.Action != "reopened" &&
                issuePayload.Action != "closed")
            {
                _logger.LogInformation($"Received github action '{issuePayload.Action}', nothing to do");
                return;
            }

            // Determine identifiable information for triage items
            var triageItems = GetTriageItems(issuePayload.Issue.Body);

            if (!triageItems.Any())
            {
                /* Item is not a triage item (does not contain identifiable information), do nothing */
                _logger.LogInformation($"{issuePayload.Issue.Url} is not a triage type issue.");

                return;
            }

            IGitHubClient gitHubClient = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(issuePayload.Repository.Owner.Login, issuePayload.Repository.Name);

            if (issuePayload.Action == "opened" || issuePayload.Action == "reopened")
            {
                // First, look for duplicate issues that are open
                var openIssues = new RepositoryIssueRequest
                {
                    Filter = IssueFilter.All,
                    State = ItemStateFilter.Open,
                    SortProperty = IssueSort.Created,
                    SortDirection = SortDirection.Ascending,
                };
                openIssues.Labels.Add(_markingLabelName);

                _logger.LogInformation("Getting open issues");
                var existingTriageIssues = await gitHubClient.Issue.GetAllForRepository(issuePayload.Repository.Id, openIssues);
                _logger.LogInformation($"There are {existingTriageIssues.Count} open issues with the '{_markingLabelName}' label");
                foreach (var existingIssue in existingTriageIssues)
                {
                    if (existingIssue.Number != issuePayload.Issue.Number)
                    {
                        var existingIssueItems = GetTriageItems(existingIssue.Body);
                        if (IsDuplicate(triageItems, existingIssueItems))
                        {
                            await gitHubClient.Issue.Comment.Create(issuePayload.Repository.Id, issuePayload.Issue.Number, $"Duplicate issue was detected.\n\nClosing as duplicate of {existingIssue.HtmlUrl}\n\nFor more information see {_docLink}");
                            var issueUpdate = new IssueUpdate
                            {
                                State = ItemState.Closed,
                            };
                            await gitHubClient.Issue.Update(issuePayload.Repository.Id, issuePayload.Issue.Number, issueUpdate);

                            return;
                        }
                    }
                }

                // No duplicates, add label and move issue to triage
                var issue = await gitHubClient.Issue.Get(issuePayload.Repository.Id, issuePayload.Issue.Number);
                if (!issue.Labels.Any(l => l.Name == _markingLabelName))
                {
                    var update = issue.ToUpdate();
                    update.AddLabel(_markingLabelName);
                    foreach(var label in _issueLabels)
                    {
                        update.AddLabel(label);
                    }
                    await gitHubClient.Issue.Update(issuePayload.Repository.Id, issuePayload.Issue.Number, update);

                    await AddToZenHubTopic(issuePayload, gitHubClient, issue);
                }
            }

            if (issuePayload.Action == "closed")
            {
                IReadOnlyList<IssueComment> comments = gitHubClient.Issue.Comment.GetAllForIssue(issuePayload.Repository.Id, issuePayload.Issue.Number).Result;

                // find the latest comment with category command
                string updatedCategory = null;
                foreach (var comment in comments)
                {
                    string category = GetTriageIssueProperty("category", comment.Body);
                    if (!string.IsNullOrEmpty(category))
                    {
                        updatedCategory = category;
                    }
                }
                if (updatedCategory != null)
                {
                    foreach (var triageItem in triageItems)
                    {
                        triageItem.UpdatedCategory = updatedCategory;
                    }
                }
            }

            foreach (var triageItem in triageItems)
            {
                triageItem.Url = issuePayload.Issue.HtmlUrl;
                _logger.LogInformation($"buildId: {triageItem.BuildId}, recordId: {triageItem.RecordId}, index: {triageItem.Index}, category: {triageItem.UpdatedCategory}, url: {triageItem.Url}");
            }

            await IngestTriageItemsIntoKusto(triageItems);

            await gitHubClient.Issue.Comment.Create(issuePayload.Repository.Id, issuePayload.Issue.Number, $"Bot has updated the 'TimelineIssuesTriage' database.\n**PowerBI reports may take up to 24 hours to refresh**\n\nSee {_docLink} for more information.");

            return;
        }

        private async Task AddToZenHubTopic(IssuesHookData issuePayload, IGitHubClient gitHubClient, Issue issue)
        {
            // add into notification epic -> currently 8/2020 it's First Response epic
            NotificationEpicOptions epic = _githubOptions.Value.NotificationEpic;

            if (epic != null)
            {
                var epicRepoData = await gitHubClient.Repository.Get(_githubOptions.Value.Organization, epic.Repository);

                _logger.LogInformation("Adding the issue to ZenHub Epic...");
                await _zenHub.AddIssueToEpicAsync(
                    new ZenHubClient.IssueIdentifier(issuePayload.Repository.Id, issue.Number),
                    new ZenHubClient.IssueIdentifier(epicRepoData.Id, epic.IssueNumber)
                );
            }
            else
            {
                _logger.LogInformation("No ZenHub epic configured, skipping...");
            }
        }

        private async Task IngestTriageItemsIntoKusto(ICollection<TriageItem> triageItems)
        {
            _logger.LogInformation("Entering IngestTriageItemIntoKusto");
            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                _kustoIngestClientFactory.GetClient(),
                _kustoOptions.Value.Database,
                "TimelineIssuesTriage",
                _logger,
                triageItems,
                b => new[]
                {
                    new KustoValue("ModifiedDateTime", b.ModifiedDateTime, KustoDataType.DateTime),
                    new KustoValue("BuildId", b.BuildId, KustoDataType.Int),
                    new KustoValue("RecordId", b.RecordId, KustoDataType.Guid),
                    new KustoValue("Index", b.Index, KustoDataType.Int),
                    new KustoValue("UpdatedCategory", b.UpdatedCategory, KustoDataType.String),
                    new KustoValue("Url", b.Url, KustoDataType.String)
                });
        }

        private bool IsDuplicate(IList<TriageItem> triageItems, IList<TriageItem> existingIssueItems)
        {
            return _internal.IsDuplicate(triageItems, existingIssueItems);
        }

        private string GetTriageIssueProperty(string propertyName, string body)
        {
            return _internal.GetTriageIssueProperty(propertyName, body);
        }

        private IList<TriageItem> GetTriageItems(string body)
        {
            return _internal.GetTriageItems(body);
        }

        public class TimelineIssueTriageInternal
        {
            // Search for triage item identifier information, example - "[BuildId=123456,RecordId=0dc87500-1d33-11ea-8b24-4baedbda8954,Index=0]"
            private static readonly Regex _triageIssueIdentifierRegex = new Regex(@"\[BuildId=(?<buildid>[^,]+),RecordId=(?<recordid>[^,]+),Index=(?<index>[^\]]+)\]\s+\[Category=(?<category>[^\]]+)\]", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
            // Search for a triage item property, example = "[Category=foo]"
            private static readonly Regex _triagePropertyRegex = new Regex(@"\[(?<key>[^=]+)=(?<value>[^\]]+)\]", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

            public IList<TriageItem> GetTriageItems(string body)
            {
                var items = new List<TriageItem>();

                var matches = _triageIssueIdentifierRegex.Matches(body ?? "");

                foreach (Match match in matches)
                {
                    TriageItem triageItem = new TriageItem();
                    int.TryParse(match.Groups["buildid"].Value, out int buildId);
                    int.TryParse(match.Groups["index"].Value, out int index);
                    Guid.TryParse(match.Groups["recordid"].Value, out Guid recordId);
                    var category = match.Groups["category"].Value;

                    triageItem.BuildId = buildId;
                    triageItem.RecordId = recordId;
                    triageItem.Index = index;
                    triageItem.UpdatedCategory = category;
                    triageItem.ModifiedDateTime = DateTime.UtcNow;

                    items.Add(triageItem);
                }

                return items;
            }

            public string GetTriageIssueProperty(string propertyName, string body)
            {
                MatchCollection propertyMatches = _triagePropertyRegex.Matches(body);
                foreach (Match propertyMatch in propertyMatches)
                {
                    if (propertyMatch.Success)
                    {
                        string key = propertyMatch.Groups["key"].Value;
                        if (propertyName.Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            return propertyMatch.Groups["value"].Value;
                        }
                    }
                }
                return null;
            }

            public bool IsDuplicate(IList<TriageItem> triageItems, IList<TriageItem> existingIssueItems)
            {
                return existingIssueItems.Count == triageItems.Count && !existingIssueItems.Except(triageItems).Any();
            }
        }
    }

    public class TriageItem
    {
        public DateTime ModifiedDateTime { get; set; }
        public int BuildId { get; set; }
        public Guid RecordId { get; set; }
        public int Index { get; set; }
        public string UpdatedCategory { get; set; }
        public string Url { get; set; }

        public override bool Equals(object obj)
        {
            TriageItem compareItem = obj as TriageItem;
            return (!Object.ReferenceEquals(null, compareItem)) &&
                   (BuildId == compareItem.BuildId) &&
                   (RecordId == compareItem.RecordId) &&
                   (Index == compareItem.Index);
        }

        public override int GetHashCode() => new { BuildId, RecordId, Index }.GetHashCode();
    }
}
