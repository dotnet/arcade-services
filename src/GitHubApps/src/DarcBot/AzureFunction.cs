// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Kusto.Ingest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Dotnet.GitHub.Authentication;
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
        // Search for triage item identifier information, example - "[BuildId=123456,RecordId=0dc87500-1d33-11ea-8b24-4baedbda8954,Index=0]"
        private static readonly Regex _darcBotIssueIdentifierRegex = new Regex(@"\[BuildId=(?<buildid>[^,]+),RecordId=(?<recordid>[^,]+),Index=(?<index>[^\]]+)\]", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
        // Search for a darcbot property, example = "[Category=foo]"
        private static readonly Regex _darcBotPropertyRegex = new Regex(@"\[(?<key>.+)=(?<value>[^\]]+)\]", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
        private static readonly string docLink = "[DarcBot documentation](https://github.com/dotnet/arcade-services/tree/master/src/GitHubApps/src/DarcBot/Readme.md)";

        [FunctionName("GitHubWebHook")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "POST", Route = null)]
            HttpRequestMessage req, ILogger log)
        {
            dynamic data = await req.Content.ReadAsAsync<object>();

            string userType = data?.comment?.user?.type;
            if (userType == "Bot")
            {
                log.LogInformation("Comment is from DarcBot, ignoring.");
                return new OkObjectResult($"Ignoring DarcBot comment");
            }
            string gitHubAction = data?.action;

            int installationId = data?.installation?.id;
            long repositoryId = data?.repository?.id;
            int prNumber = data?.issue?.number;

            if (gitHubAction != "opened" &&
                gitHubAction != "reopened" &&
                gitHubAction != "closed")
            {
                log.LogInformation($"Received github action '{gitHubAction}', nothing to do");
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
                log.LogInformation($"{data?.issue?.url} is not a triage type issue.");
                return new OkObjectResult("No identifiable information detected");
            }

            int.TryParse(System.Environment.GetEnvironmentVariable("AppId"), out int appId);

            // Create jwt token
            // Private key is stored in Azure Key vault by downloading the private key (pem file) from GitHub, then
            // using the Azure CLI to store the value in key vault.
            // ie: az keyvault secret set --vault-name [vault name] --name GitHubApp-DarcBot-PrivateKey --encoding base64 --file [pem key file path]
            GitHubAppTokenProvider gitHubTokenProvider = new GitHubAppTokenProvider();
            var installationToken = gitHubTokenProvider.GetAppTokenFromEnvironmentVariableBase64(appId, "PrivateKey");

            // create client using jwt as a bearer token
            var userAgent = new Octokit.ProductHeaderValue("DarcBot");
            GitHubClient appClient = new GitHubClient(userAgent)
            {
                Credentials = new Credentials(installationToken, AuthenticationType.Bearer),
            };

            // using the client, create an installation token
            AccessToken token = await appClient.GitHubApps.CreateInstallationToken(installationId);

            // with the installation token, create a new GitHubClient that has the apps permissions
            var gitHubClient = new GitHubClient(new ProductHeaderValue("DarcBot-Installation"))
            {
                Credentials = new Credentials(token.Token)
            };

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
                log.LogInformation("Getting open issues");
                var issues = await gitHubClient.Issue.GetAllForRepository(repositoryId, openIssues);
                log.LogInformation($"There are {issues.Count} open issues");
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

            log.LogInformation($"buildId: {triageItem.BuildId}, recordId: {triageItem.RecordId}, index: {triageItem.Index}");
            log.LogInformation($"category: {triageItem.UpdatedCategory}");
            log.LogInformation($"url: {triageItem.Url}");

            await IngestTriageItemsIntoKusto(new[] { triageItem }, log);

            await gitHubClient.Issue.Comment.Create(repositoryId, prNumber, $"DarcBot has updated the 'TimelineIssuesTriage' database.\n**PowerBI reports may take up to 24 hours to refresh**\n\nSee {docLink} for more information and 'darcbot' usage.");
            return new OkObjectResult("Success");
        }

        private static async Task IngestTriageItemsIntoKusto(TriageItem[] triageItems, ILogger log)
        {
            log.LogInformation("Entering IngestTriageItemIntoKusto");
            string kustoIngestConnectionString = System.Environment.GetEnvironmentVariable("KustoIngestConnectionString");
            string databaseName = System.Environment.GetEnvironmentVariable("KustoDatabaseName");
            IKustoIngestClient ingestClient = KustoIngestFactory.CreateQueuedIngestClient(kustoIngestConnectionString);
            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                ingestClient,
                databaseName,
                "TimelineIssuesTriage",
                log,
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
            if (string.IsNullOrEmpty(body))
            {
                return null;
            }

            TriageItem triageItem = new TriageItem();

            Match propertyMatch = _darcBotPropertyRegex.Match(body);
            if (propertyMatch.Success)
            {
                triageItem.UpdatedCategory = GetDarcBotProperty("category", body);
            }

            Match triageIdentifierMatch = _darcBotIssueIdentifierRegex.Match(body);
            if (!triageIdentifierMatch.Success)
            {
                return null;
            }

            int.TryParse(triageIdentifierMatch.Groups["buildid"].Value, out int buildId);
            int.TryParse(triageIdentifierMatch.Groups["index"].Value, out int index);
            Guid.TryParse(triageIdentifierMatch.Groups["recordid"].Value, out Guid recordId);

            triageItem.BuildId = buildId;
            triageItem.RecordId = recordId;
            triageItem.Index = index;
            triageItem.ModifiedDateTime = DateTime.Now;
            return triageItem;
        }
    }
}
