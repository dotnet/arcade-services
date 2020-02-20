// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.DotNet.Git.IssueManager;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using Microsoft.TeamFoundation.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DependencyUpdateErrorProcessor
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class DependencyUpdateErrorProcessor : IServiceImplementation
    {
        public DependencyUpdateErrorProcessor(
            IReliableStateManager stateManager,
            ILogger<DependencyUpdateErrorProcessor> logger,
            BuildAssetRegistryContext context,
            IOptions<DependencyUpdateErrorProcessorOptions> dependencyUpdateErrorProcessorOptions
            )
        {
            StateManager = stateManager;
            Logger = logger;
            Context = context;
            DependencyUpdateErrorProcessorOptions = dependencyUpdateErrorProcessorOptions.Value;
        }

        public IReliableStateManager StateManager { get; }

        public ILogger<DependencyUpdateErrorProcessor> Logger { get; }

        public BuildAssetRegistryContext Context { get; }

        public DependencyUpdateErrorProcessorOptions DependencyUpdateErrorProcessorOptions { get; }

        [CronSchedule("0 0 0/1 1/1 * ? *", TimeZones.PST)]
        public async Task DependencyUpdateErrorProcessing()
        {

            if (DependencyUpdateErrorProcessorOptions.ConfigurationRefresherdPointUri != null && DependencyUpdateErrorProcessorOptions.DynamicConfigs != null)
            {
                DependencyUpdateErrorProcessorOptions.ConfigurationRefresherdPointUri.Refresh().GetAwaiter().GetResult();
           
                bool.TryParse(DependencyUpdateErrorProcessorOptions.DynamicConfigs["FeatureManagement:DependencyUpdateErrorProcessor"], 
                    out var autoDependencyUpdateErrorProcessor);
                if (autoDependencyUpdateErrorProcessor)
                {
                    IReliableDictionary<string, DateTime> update =
                        await StateManager.GetOrAddAsync<IReliableDictionary<string, DateTime>>("update");
                    try
                    {
                        using (ITransaction tx = StateManager.CreateTransaction())
                        {
                            var previousTransactionDateTime = await update.GetOrAddAsync(
                             tx,
                             "update",
                             DateTime.Now
                             );
                            var updatedTime = await CheckForError(previousTransactionDateTime, DependencyUpdateErrorProcessorOptions.GithubUrl);
                            await update.TryUpdateAsync(
                                tx,
                                "update",
                                updatedTime.UtcDateTime,
                                previousTransactionDateTime
                                );
                            await tx.CommitAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Processing update dependency messages");
                    }
                }
            }
            else
            {
                Logger.LogInformation("Dependency Update Error processor is disabled because no App Configuration was available.");
            }
        }

        public async Task<DateTimeOffset> CheckForError(DateTimeOffset previousTransactionDateTime , string whereToCreateIssue)
        {
            List<RepositoryBranchUpdateHistoryEntry> list = (from repo in Context.RepositoryBranchUpdateHistory
                                                             where repo.Success == false
                                                             where repo.Timestamp > previousTransactionDateTime.UtcDateTime
                                                             orderby repo.Timestamp ascending
                                                             select repo).ToList();
            if (!list.Any())
            {
                return previousTransactionDateTime;
            }

            Logger.LogInformation($"Going to create the github issue");
            try
            {
                await CreateGitHubIssueAsync(previousTransactionDateTime.UtcDateTime,  list[0].Repository, list[0].Branch, 
                    list[0].Action, list[0].ErrorMessage, list[0].Method , whereToCreateIssue);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not create a github issue for the error: " + list[0].ErrorMessage);
            }
            previousTransactionDateTime = list[0].Timestamp;
            return previousTransactionDateTime;
        }

        public async Task<bool> SearchCreatedIssueAsync(DateTime transactionDateTime , string errorMessage)
        {
            var query = new StringBuilder();
            HttpClient client = new HttpClient
            {
                BaseAddress = new Uri("https://api.github.com")
            };
            client.DefaultRequestHeaders.Add("User-Agent", @"Mozilla/5.0 (Windows NT 10; Win64; x64; rv:60.0) Gecko/20100101 Firefox/60.0");

            query.Append($"is:issue+is:open+{"Error Message: " + errorMessage}in:title&order=desc");
            JObject responseContent;
            using (HttpResponseMessage response = await client.GetAsync(
                $"search/issues?q={query}"))
            {
                responseContent = JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            JArray items = JArray.Parse(responseContent["items"].ToString());
            if (!items.IsNullOrEmpty())
            {
                foreach (JObject item in items)
                {
                    var createdDate = item.GetValue("created_at");
                    var result = createdDate.ToObject<DateTime>();
                    if (result <= transactionDateTime)
                    {
                        Logger.LogInformation("The issue already exists so skipping this error");
                        return false;
                    }
                    else
                        return true;
                }
            }
            return true;
        }


        private async Task CreateGitHubIssueAsync(DateTime transactionDateTime , string repositoryUrl, string branch, 
            string action, String errorMessage, String method, string whereToCreateIssue)
        {
            Logger.LogInformation($"Something failed in the repository : {repositoryUrl} ");

            string fyiHandles = "@epananth";
            string gitHubToken = null, azureDevOpsToken = null;
            string label = "UpdateDependency";

            using (Logger.BeginScope($"Opening GitHub issue for RepositoryBranchUpdateHistory."))
            {
                try
                {
                    if (!string.IsNullOrEmpty(whereToCreateIssue))
                    {
                        IGitHubTokenProvider gitHubTokenProvider = Context.GetService<IGitHubTokenProvider>();
                        long installationId = await Context.GetInstallationId(whereToCreateIssue);
                        gitHubToken = await gitHubTokenProvider.GetTokenForInstallationAsync(installationId);

                        Logger.LogInformation($"GitHub token acquired for '{whereToCreateIssue}'!");
                    }

                    IssueManager issueManager = new IssueManager(gitHubToken, azureDevOpsToken);

                    string title = $"Repository Url :'{repositoryUrl}' Error :'{errorMessage}'";
                    string description = $"Something failed during dependency update" +
                    $"{Environment.NewLine} {Environment.NewLine}" +
                    "RepositoryUrl :" + $"[{repositoryUrl}](repositoryUrl)" +
                    $"{Environment.NewLine} {Environment.NewLine}" +
                    "Branch Name :" + $"{branch}" +
                    $"{Environment.NewLine} {Environment.NewLine}" +
                    "Error Message : " + $"{errorMessage}" +
                    $"{Environment.NewLine} {Environment.NewLine}" +
                    "Method : " + $"{method}" +
                    $"{Environment.NewLine} {Environment.NewLine}" +
                    "Action : " + $"{action}" +
                    $"{Environment.NewLine} {Environment.NewLine}" +
                    "Failure Category :" + $"{label}" + // Work on failure category will be done as a part of -> https://github.com/dotnet/arcade/issues/1196
                    $"{Environment.NewLine} {Environment.NewLine}" +
                    $"/FYI: {fyiHandles}";


                    Logger.LogInformation($"GitHub token acquired for '{whereToCreateIssue}'!");
                    //Check if the issue already exists in Github
                    if (await SearchCreatedIssueAsync(transactionDateTime, errorMessage))
                    {
                        int issueId = await issueManager.CreateNewIssueAsync(whereToCreateIssue, title, description);
                        Logger.LogInformation($"Issue {issueId} was created in '{whereToCreateIssue}'");
                        Logger.LogInformation("have to create issue");
                    }
                    else
                    {
                        Logger.LogInformation("Issue already exists for this particular error");
                    }
                }
                catch (Exception exc)
                {
                    Logger.LogError(exc, $"Something failed while attempting to create an issue based on repo '{repositoryUrl}' ");
                }
            }
        }

        public Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
