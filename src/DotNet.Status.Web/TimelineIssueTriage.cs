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
        private static readonly string[] _issueLabels = new[] { "Detected By - Ad-Hoc Testing", "First Responder", "Build Failed" };

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
                _logger.LogInformation($"Received GitHub action '{issuePayload.Action}', no triage issue handling for on such action.");
                return;
            }

            var triageItems = GetTriageItems(issuePayload.Issue.Body);
            if (!triageItems.Any())
            {
                _logger.LogInformation($"{issuePayload.Issue.Url} is not a time line triage type issue.");
                return;
            }

            IGitHubClient gitHubClient = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(issuePayload.Repository.Owner.Login, issuePayload.Repository.Name);
            var existingTriageIssues = await ExistingTriageItems(issuePayload, gitHubClient);

            if (issuePayload.Action == "opened" || issuePayload.Action == "reopened")
            {
                if (IsDuplicate(issuePayload, triageItems, existingTriageIssues, out var existingDuplicateIssue))
                {
                    await CloseAsDuplicate(issuePayload, existingDuplicateIssue, gitHubClient);
                }
                else if (ShallUpdateExistingIssue(issuePayload, triageItems, existingTriageIssues, out var existingIssueToUpdate))
                {
                    await UpdateExistingIssue(issuePayload, triageItems, existingIssueToUpdate, gitHubClient);
                }
                else
                {
                    await ProcessOpenedTriageIssue(issuePayload, triageItems, gitHubClient);
                }
            }
            else if (issuePayload.Action == "closed")
            {
                await RecatogorizeFailedBuildsIfRequested(issuePayload, triageItems, gitHubClient);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected action {issuePayload.Action} during handling time line triage issue");
            }
        }

        private bool IsDuplicate(IssuesHookData issuePayload, IList<TriageItem> triageItems, IReadOnlyList<Issue> existingTriageIssues, out Issue existingDuplicate)
        {
            existingDuplicate = existingTriageIssues.FirstOrDefault(e =>
                issuePayload.Issue.Number != e.Number &&
                IsDuplicate(triageItems, GetTriageItems(e.Body)));

            return existingDuplicate != null;
        }

        private static async Task CloseAsDuplicate(IssuesHookData issuePayload, Issue existingDuplicateIssue, IGitHubClient gitHubClient)
        {
            await gitHubClient.Issue.Comment.Create(issuePayload.Repository.Id, issuePayload.Issue.Number,
                $"Duplicate issue was detected.\n\nClosing as duplicate of {existingDuplicateIssue.HtmlUrl}\n\nFor more information see {_docLink}");
            var issueUpdate = new IssueUpdate
            {
                State = ItemState.Closed,
            };
            await gitHubClient.Issue.Update(issuePayload.Repository.Id, issuePayload.Issue.Number, issueUpdate);
        }

        private bool ShallUpdateExistingIssue(IssuesHookData issuePayload, IList<TriageItem> triageItems, IReadOnlyList<Issue> existingTriageIssues, out Issue existingIssueToUpdate)
        {
            existingIssueToUpdate = existingTriageIssues.FirstOrDefault(e => 
                issuePayload.Issue.Number != e.Number &&
                ShallExistingIssueBeUpdated(triageItems, GetTriageItems(e.Body)));

            return existingIssueToUpdate != null;
        }

        private async Task UpdateExistingIssue(IssuesHookData issuePayload, IList<TriageItem> triageItems, Issue existingIssueToUpdate, IGitHubClient gitHubClient)
        {
            var existingIssueItems = GetTriageItems(existingIssueToUpdate.Body);

            // update existing issue body
            var existintIssueUpdate = new IssueUpdate
            {
                Body = UpdateExistingIssueBody(issuePayload, triageItems, existingIssueToUpdate, existingIssueItems),
            };
            await gitHubClient.Issue.Update(issuePayload.Repository.Id, existingIssueToUpdate.Number, existintIssueUpdate);

            // insert TimelineIssuesTriage with existing issue URL
            var toBeUpdatedTriageItems = triageItems.Except(existingIssueItems).ToList();
            foreach (var triageItem in toBeUpdatedTriageItems)
            {
                triageItem.Url = existingIssueToUpdate.HtmlUrl;
                _logger.LogInformation($"buildId: {triageItem.BuildId}, recordId: {triageItem.RecordId}, index: {triageItem.Index}, category: {triageItem.UpdatedCategory}, url: {triageItem.Url}");
            }
            await IngestTriageItemsIntoKusto(toBeUpdatedTriageItems);

            await gitHubClient.Issue.Comment.Create(issuePayload.Repository.Id, existingIssueToUpdate.Number,
                $"Bot has updated the issue and 'TimelineIssuesTriage' database by data from issue {issuePayload.Issue.HtmlUrl}.\n**PowerBI reports may take up to 24 hours to refresh**\n\nSee {_docLink} for more information.");

            // add comment to opened issue and close it
            await gitHubClient.Issue.Comment.Create(issuePayload.Repository.Id, issuePayload.Issue.Number,
                $"Existing issue was detected and additional builds was added to it.\n\nClosing as updated into {existingIssueToUpdate.HtmlUrl}\n\nFor more information see {_docLink}");
            var issueUpdate = new IssueUpdate
            {
                State = ItemState.Closed,
            };
            await gitHubClient.Issue.Update(issuePayload.Repository.Id, issuePayload.Issue.Number, issueUpdate);
        }

        private async Task ProcessOpenedTriageIssue(IssuesHookData issuePayload, IList<TriageItem> triageItems, IGitHubClient gitHubClient)
        {
            var issue = await gitHubClient.Issue.Get(issuePayload.Repository.Id, issuePayload.Issue.Number);
            if (!issue.Labels.Any(l => l.Name == _markingLabelName))
            {
                var update = issue.ToUpdate();
                update.AddLabel(_markingLabelName);
                foreach (var label in _issueLabels.Except(issue.Labels.Select(l => l.Name)))
                {
                    update.AddLabel(label);
                }
                await gitHubClient.Issue.Update(issuePayload.Repository.Id, issuePayload.Issue.Number, update);

                await AddToZenHubTopic(issuePayload, gitHubClient, issue);
            }

            foreach (var triageItem in triageItems)
            {
                triageItem.Url = issuePayload.Issue.HtmlUrl;
                _logger.LogInformation($"New triage item: {triageItem}");
            }
            await IngestTriageItemsIntoKusto(triageItems);

            await gitHubClient.Issue.Comment.Create(issuePayload.Repository.Id, issuePayload.Issue.Number, $"Bot has updated the 'TimelineIssuesTriage' database.\n**PowerBI reports may take up to 24 hours to refresh**\n\nSee {_docLink} for more information.");
        }

        private async Task RecatogorizeFailedBuildsIfRequested(IssuesHookData issuePayload, IList<TriageItem> triageItems, IGitHubClient gitHubClient)
        {
            if (issuePayload.Issue.Labels.All(l => l.Name != _markingLabelName))
            {
                // do not handle issues which are not marked by label
                return;
            }

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

                foreach (var triageItem in triageItems)
                {
                    triageItem.Url = issuePayload.Issue.HtmlUrl;
                    _logger.LogInformation($"Updated category of triage item: {triageItem}");
                }

                await IngestTriageItemsIntoKusto(triageItems);

                await gitHubClient.Issue.Comment.Create(issuePayload.Repository.Id, issuePayload.Issue.Number, $"Bot has updated the 'TimelineIssuesTriage' database with new category '{updatedCategory}'.\n**PowerBI reports may take up to 24 hours to refresh**\n\nSee {_docLink} for more information.");
            }
        }

        private async Task<IReadOnlyList<Issue>> ExistingTriageItems(IssuesHookData issuePayload, IGitHubClient gitHubClient)
        {
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
            return existingTriageIssues;
        }

        private string UpdateExistingIssueBody(IssuesHookData issuePayload, IList<TriageItem> triageItems, Issue existingIssue, IList<TriageItem> existingIssueItems)
        {
            return _internal.UpdateExistingIssueBody(triageItems, issuePayload.Issue.Body, existingIssueItems, existingIssue.Body);
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

        private bool ShallExistingIssueBeUpdated(IList<TriageItem> triageItems, IList<TriageItem> existingIssueItems)
        {
            return _internal.ShallExistingIssueBeUpdated(triageItems, existingIssueItems);
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

            public bool IsDuplicate(IList<TriageItem> openedIssueItems, IList<TriageItem> existingIssueItems)
            {
                return existingIssueItems.Count == openedIssueItems.Count && !existingIssueItems.Except(openedIssueItems).Any();
            }

            public bool ShallExistingIssueBeUpdated(IList<TriageItem> openedIssueItems, IList<TriageItem> existingIssueItems)
            {
                return existingIssueItems.Intersect(openedIssueItems).Any();
            }

            public string UpdateExistingIssueBody(IList<TriageItem> openedIssueItems, string openedIssueBody, IList<TriageItem> existingIssueItems, string existingIssueBody)
            {
                var addMissing = openedIssueItems.Except(existingIssueItems).ToList();

                // if opened issue is subset of existing issue, let the existing issue kept intact
                if (!addMissing.Any())
                {
                    return existingIssueBody;
                }

                // add missing builds from opened at the end of the existing build lists
                Regex _triagePropertyRegex = new Regex(@"\[.*?]\(.*?\)\s*=>\s*\[Log\]\(.*?\)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
                var matchesOpened = _triagePropertyRegex.Matches(openedIssueBody);
                var buildsLinksToBeAdded = string.Join("\n",
                    matchesOpened.Select(m => m.Value).Where(b => addMissing.Any(i => b.Contains(i.BuildId.ToString()))));

                if (!string.IsNullOrEmpty(buildsLinksToBeAdded))
                {
                    var matchesExisting = _triagePropertyRegex.Matches(existingIssueBody);
                    int indexOfEndOfBuilds = matchesExisting.Any() ?
                        matchesExisting.Last().Index + matchesExisting.Last().Length :
                        0; // have not found build links in existing issue, maybe have been damaged by hand, lets past it at the start of body
                    existingIssueBody = existingIssueBody.Insert(indexOfEndOfBuilds, "\n" + buildsLinksToBeAdded);
                }

                // add missing at the end of references
                int indexOfDetails = existingIssueBody.LastIndexOf("</details>");
                if (indexOfDetails < 0)
                {
                    // have not found it, maybe have been damaged by hand, lets past it at the end of body
                    indexOfDetails = existingIssueBody.Length;
                }
                string newReferences = string.Join("\n", 
                    addMissing.Select(i => $"[BuildId={i.BuildId},RecordId={i.RecordId},Index={i.Index}]\n[Category={i.UpdatedCategory}]\n"));
                existingIssueBody = existingIssueBody.Insert(indexOfDetails, newReferences + "\n");

                return existingIssueBody;
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

        public override string ToString()
        {
            return $"buildId: {BuildId}, recordId: {RecordId}, index: {Index}, category: {UpdatedCategory}, url: {Url}";
        }
    }
}
