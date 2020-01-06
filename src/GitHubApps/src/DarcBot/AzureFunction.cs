// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Kusto.Ingest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Logging;
using Octokit;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DarcBot
{
    public static class GitHubWebHook
    {
        private static ILogger _log;
        private static Regex _darcBotIssueIdentifierRegex = new Regex(@"\[BuildId=(?<buildid>[^,]+),RecordId=(?<recordid>[^,]+),Index=(?<index>[^\]]+)\]", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
        private static Regex _darcBotPropertyRegex = new Regex(@"\[(?<key>.+)=(?<value>[^\]]+)\]", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
        private static readonly string docLink = "[DarcBot documentation](https://github.com/dotnet/arcade-services/tree/master/src/GitHubApps/src/DarcBot/Readme.md)";

        [FunctionName("GitHubWebHook")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route = null)]
            HttpRequestMessage req, ILogger log)
        {
            _log = log;

            dynamic data = await req.Content.ReadAsAsync<object>();

            string userType = data?.comment?.user?.type;
            if (userType == "Bot")
            {
                _log.LogInformation("Comment is from DarcBot, ignoring.");
                return new OkObjectResult($"Ignoring DarcBot comment");
            }
            string gitHubAction = data?.action;

            int installationId = data?.installation?.id;
            long repositoryId = data?.repository?.id;
            int prNumber = data?.issue?.number;

            // Check for darcbot directed comments
            if (gitHubAction == "created")
            {
                /* Nothing to do, this is the section where you would handle user comments if they contained explicit directives to darcbot.
                   Note: be sure to ignore comments from issue?.user?.type == "Bot" so you don't get stuck in a payload delivery loop */
            }

            if (gitHubAction != "opened" &&
                gitHubAction != "reopened" &&
                gitHubAction != "closed")
            {
                _log.LogInformation($"Received github action '{gitHubAction}', nothing to do");
                return new OkObjectResult($"DarcBot has nothing to do with github issue action '{gitHubAction}'");
            }

            // Determine identifiable information for triage item
            string issueBody = data?.issue?.body;
            TriageItem triageItem = GetTriageItemProperties(issueBody);

            string issueUrl = data?.issue?.html_url;
            triageItem.Url = issueUrl;

            if (triageItem == null)
            {
                /* Item is not a triage item (does not contain identifiable information), do nothing */
                _log.LogInformation($"{data?.issue?.url} is not a triage type issue.");
                return new OkObjectResult("No identifiable information detected");
            }

            IGitHubClient gitHubClient = new DarcBotGitHubClient(installationId, _log);

            if (gitHubAction == "opened" ||
                gitHubAction == "reopened")
            {
                
                // First, look for duplicate issues that are open
                var openIssues = new RepositoryIssueRequest
                {
                    Filter = IssueFilter.All,
                    State = ItemStateFilter.Open,
                    SortProperty = IssueSort.Created,
                    SortDirection = SortDirection.Ascending
                };
                _log.LogInformation("Getting open issues");
                var issues = gitHubClient.Issue.GetAllForRepository(repositoryId, openIssues).Result;
                _log.LogInformation("Acquired open issues");
                _log.LogInformation($"There are {issues.Count} open issues");
                foreach (var issue in issues)
                {
                    if (issue.Number != prNumber)
                    {
                        TriageItem issueItem = GetTriageItemProperties(issue.Body);
                        if (triageItem.Equals(issueItem))
                        {
                            await gitHubClient.Issue.Comment.Create(repositoryId, prNumber, $"DarcBot has detected a duplicate issue.\n\nClosing as duplicate of {issue.HtmlUrl}\n\nFor more information see {docLink}");
                            var issueUpdate = new IssueUpdate
                            {
                                State = ItemState.Closed,
                            };
                            await gitHubClient.Issue.Update(repositoryId, prNumber, issueUpdate);
                            return new OkObjectResult($"Resolved as duplicate of {issue.Number}");
                        }
                    }
                }

                // No duplicates, move issue to triage
                triageItem.UpdatedCategory = "InTriage";
            }

            if (gitHubAction == "closed")
            {
                IReadOnlyList<IssueComment> comments = gitHubClient.Issue.Comment.GetAllForIssue(repositoryId, prNumber).Result;

                foreach (var comment in comments)
                {
                    // Look for category information in comment
                    string category = GetDarcBotProperty("category", comment.Body);
                    if (!string.IsNullOrEmpty(category))
                    {
                        triageItem.UpdatedCategory = category;
                    }
                }
            }

            _log.LogInformation($"buildId: {triageItem.BuildId}, recordId: {triageItem.RecordId}, index: {triageItem.Index}");
            _log.LogInformation($"category: {triageItem.UpdatedCategory}");
            _log.LogInformation($"url: {triageItem.Url}");

            await IngestTriageItemsIntoKusto(new[] { triageItem });

            await gitHubClient.Issue.Comment.Create(repositoryId, prNumber, $"DarcBot has updated the 'TimelineIssuesTriage' database.\n**PowerBI reports may take up to 24 hours to refresh**\n\nSee {docLink} for more information and 'darcbot' usage.");
            return new OkObjectResult("Success");
        }

        private static async Task IngestTriageItemsIntoKusto(TriageItem[] triageItems)
        {
            _log.LogInformation("Entering IngestTriageItemIntoKusto");
            string kustoIngestConnectionString = System.Environment.GetEnvironmentVariable("KustoIngestConnectionString");
            string databaseName = System.Environment.GetEnvironmentVariable("KustoDatabaseName");
            IKustoIngestClient ingestClient = KustoIngestFactory.CreateQueuedIngestClient(kustoIngestConnectionString);
            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                ingestClient,
                databaseName,
                "TimelineIssuesTriage",
                _log,
                triageItems,
                b => new[]
                {
                    new KustoValue("ModifiedDateTime", b.ModifiedDateTime.ToString(), KustoDataTypes.DateTime),
                    new KustoValue("BuildId", b.BuildId.ToString(), KustoDataTypes.Int),
                    new KustoValue("RecordId", b.RecordId.ToString(), KustoDataTypes.Guid),
                    new KustoValue("Index", b.Index.ToString(), KustoDataTypes.Int),
                    new KustoValue("UpdatedCategory", b?.UpdatedCategory, KustoDataTypes.String),
                    new KustoValue("Url", b?.Url, KustoDataTypes.String)
                });
        }

        private static string GetDarcBotProperty(string propertyName, string body)
        {
            MatchCollection propertyMatches = _darcBotPropertyRegex.Matches(body);
            foreach (Match propertyMatch in propertyMatches)
            {
                if (propertyMatch.Success)
                {
                    string key = propertyMatch.Groups["key"].Value;
                    string value = propertyMatch.Groups["value"].Value;
                    if (propertyName.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }
            }
            return null;
        }

        private static TriageItem GetTriageItemProperties(string body)
        {
            TriageItem triageItem = new TriageItem();

            if (!string.IsNullOrEmpty(body))
            {
                Match propertyMatch = _darcBotPropertyRegex.Match(body);
                if (propertyMatch.Success)
                {
                    triageItem.UpdatedCategory = GetDarcBotProperty("category", body);
                }

                Match triageIdentifierMatch = _darcBotIssueIdentifierRegex.Match(body);
                if (triageIdentifierMatch.Success)
                {
                    triageItem.BuildId = int.Parse(triageIdentifierMatch.Groups["buildid"].Value);
                    triageItem.RecordId = Guid.Parse(triageIdentifierMatch.Groups["recordid"].Value);
                    triageItem.Index = int.Parse(triageIdentifierMatch.Groups["index"].Value);
                }
                else
                {
                    return null;
                }
                triageItem.ModifiedDateTime = DateTime.Now;
                return triageItem;
            }
            return null;
        }
    }
}
