// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Data.Migrations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Newtonsoft.Json.Linq;
using Octokit;

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
            IOptions<DependencyUpdateErrorProcessorOptions> options
            )
        {
            _stateManager = stateManager;
            Logger = logger;
            Context = context;
            _options = options.Value;
        }

        private readonly IReliableStateManager _stateManager;

        private DependencyUpdateErrorProcessorOptions _options;

        private ILogger<DependencyUpdateErrorProcessor> Logger { get; }

        private BuildAssetRegistryContext Context { get; }

        //Runs every hour in staging for now, in production it will run once a day.
        [CronSchedule("0 0/1 * 1/1 * ? *", TimeZones.PST)]
        public async Task DependencyUpdateErrorProcessing()
        {
            if (_options.ConfigurationRefresherEndPointUri == null && _options.DynamicConfigs == null)
            {
                Logger.LogInformation("Dependency Update Error processor is disabled because no App Configuration was available.");
                return;
            }
            await _options.ConfigurationRefresherEndPointUri.Refresh();
            bool.TryParse(_options.DynamicConfigs["FeatureManagement:DependencyUpdateErrorProcessor"],
                out var dependencyUpdateErrorProcessorFlag);
            if (dependencyUpdateErrorProcessorFlag)
            {
                IReliableDictionary<string, DateTimeOffset> update =
                    await _stateManager.GetOrAddAsync<IReliableDictionary<string, DateTimeOffset>>("update");
                try
                {
                    DateTimeOffset previousTransaction;
                    using (ITransaction tx = _stateManager.CreateTransaction())
                    {
                        previousTransaction = await update.GetOrAddAsync(
                         tx,
                         "update",
                         //DateTimeOffset.UtcNow 
                         new DateTime(2002, 10, 18)
                         );
                        await tx.CommitAsync();
                    }
                    await CheckForErrorInRepositoryBranchHistoryTable(previousTransaction, _options.GithubUrl, update);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex.Message, "Unable to get the previous transaction time from reliable services.");
                }
            }
        }

        private async Task CheckForErrorInRepositoryBranchHistoryTable(DateTimeOffset previousTransaction , string issueRepo , IReliableDictionary<string, DateTimeOffset> update)
        {
            try
            {
                // Looking for errors from the RepositoryBranchHistory table
                List<RepositoryBranchUpdateHistoryEntry> unprocessedHistoryEntries = (from repoBranchUpdateHistory in Context.RepositoryBranchUpdateHistory
                    where repoBranchUpdateHistory.Success == false
                    where repoBranchUpdateHistory.Timestamp > previousTransaction.UtcDateTime
                    orderby repoBranchUpdateHistory.Timestamp ascending
                    select repoBranchUpdateHistory).ToList();
                if (!unprocessedHistoryEntries.Any())
                {
                    Logger.LogInformation("No errors found in the RepositoryBranchUpdateHistory table.");
                    return;
                }
                Logger.LogInformation("Going to create the github issue.");

                foreach (var error in unprocessedHistoryEntries)
                {
                    try
                    {
                        await CreateOrUpdateGithubIssueAsync(error, issueRepo);
                    }
                    catch(Exception ex)
                    {
                        Logger.LogError(ex.Message, "unable to create a gighub issue.");
                    }
                    using (ITransaction tx = _stateManager.CreateTransaction())
                    {
                        await update.SetAsync(
                             tx,
                             "update",
                             error.Timestamp
                             );
                        await tx.CommitAsync();
                    }
                }
            } 
            catch (Exception ex)
            {
                Logger.LogError(ex.Message, "Unable to retrieve records from RepositoryBranchUpdateHistory table.");
            }
        }

        private async Task<GitHubClient> AuthenticateGitHubClient(string issueRepo)
        {
            IGitHubTokenProvider gitHubTokenProvider = Context.GetService<IGitHubTokenProvider>();
            long installationId = await Context.GetInstallationId(issueRepo);
            string gitHubToken = await gitHubTokenProvider.GetTokenForInstallationAsync(installationId);
            Logger.LogInformation("GitHub token acquired for " + issueRepo);
            Octokit.ProductHeaderValue product;
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
            product = new Octokit.ProductHeaderValue("Maestro", version);
            var client = new GitHubClient(product);
            var token = new Credentials(gitHubToken);
            client.Credentials = token;
            return client;
        }

        private async Task CreateOrUpdateGithubIssueAsync(RepositoryBranchUpdateHistoryEntry repositoryBranchUpdateHistory, string issueRepo)
        {
            Logger.LogInformation("Something failed in the repository : " + repositoryBranchUpdateHistory.Repository);
            string fyiHandles = _options.FyiHandle;
            IReliableDictionary<(string, string), int> gitHubIssueEvaluator =
                await _stateManager.GetOrAddAsync<IReliableDictionary<(string, string), int>>("gitHubIssueEvaluator");
            var client = await AuthenticateGitHubClient(issueRepo);
            StringBuilder description = new StringBuilder();
            switch (repositoryBranchUpdateHistory.Method)
            {
                // Only UpdateAssetAsync method has subscriptionId as one of the parameters in the arguments column. Other methods do not have this info.
                case "UpdateAssetsAsync":
                    // append to the description only if the subscription is valid else do not create the issue
                    string updateAsync = UpdateAsyncArgumentsParse(repositoryBranchUpdateHistory);
                    if (String.IsNullOrEmpty(updateAsync))
                    {
                        Logger.LogInformation("The subscriptionId is not valid so skipping UpdateAssetsAsync method.");
                        return;
                    }
                    description.Append(updateAsync);
                    break;
                // since this is not really an error, skipping this method
                case "SynchronizePullRequestAsync":
                    Logger.LogInformation("Skipping SynchronizePullRequestAsync method.");
                    return;
                // for all the other methods
                default:
                    description.Append("[marker]: <> (method:" + repositoryBranchUpdateHistory.Method + ", errorMessage:" + repositoryBranchUpdateHistory.ErrorMessage + ")"
                        + "\n**Repository :** " + repositoryBranchUpdateHistory.Repository 
                        + "\n**Branch Name :** " + repositoryBranchUpdateHistory.Branch
                        + "\n**Error Message :** " + repositoryBranchUpdateHistory.ErrorMessage
                        + "\n**Method :** " + repositoryBranchUpdateHistory.Method
                        + "\n**Action :** " + repositoryBranchUpdateHistory.Action
                        + "\n**Last seen :** " + repositoryBranchUpdateHistory.Timestamp.ToString()); 
                    break;
            }
            // Get repo info used for create/ update gitHub issue
            Octokit.Repository repo = await client.Repository.Get(_options.Owner, _options.Repository);
            using (ITransaction tx = _stateManager.CreateTransaction())
            {
                if (await gitHubIssueEvaluator.ContainsKeyAsync(tx, (repositoryBranchUpdateHistory.Repository, repositoryBranchUpdateHistory.Branch)))
                {
                    // issue exists so just update the issue
                    var issueNumber = await gitHubIssueEvaluator.TryGetValueAsync(tx, (repositoryBranchUpdateHistory.Repository, repositoryBranchUpdateHistory.Branch));
                    Logger.LogInformation("Updating a gitHub issue number : " + issueNumber + " for the error : " + repositoryBranchUpdateHistory.ErrorMessage +
                        " for the repository : " + repositoryBranchUpdateHistory.Repository);
                    if (issueNumber.HasValue)
                    { 
                        var issue = await client.Issue.Get(repo.Id, issueNumber.Value);
                        bool descriptionUpdateFlag = false;
                        var comments = await client.Issue.Comment.GetAllForIssue(repo.Id, issue.Number);
                        bool updateIssueBody = UpdateIssueCommentBody(repositoryBranchUpdateHistory, issue.Body);
                        // check if the issue body has to be updated or comment body has to be updated.
                        if (updateIssueBody)
                        {
                            // Issue body will be updated
                            descriptionUpdateFlag = true;
                            var issueUpdate = issue.ToUpdate();
                            issueUpdate.Body = description.ToString() + "\n\n**/FyiHandle :** " + _options.FyiHandle;
                            issueUpdate.State = ItemState.Open;
                            Logger.LogInformation("Updating issue body for the issue :" + issueNumber.Value);
                            await client.Issue.Update(repo.Id, issueNumber.Value, issueUpdate);
                        }
                        foreach (var comment in comments)
                        {
                            bool updateCommentBody = UpdateIssueCommentBody(repositoryBranchUpdateHistory, comment.Body);
                            descriptionUpdateFlag = false;
                            if (updateCommentBody)
                            {
                                // Issue's comment body will be updated
                                descriptionUpdateFlag = true;
                                Logger.LogInformation("Updating comment body for the issue : " + issueNumber.Value);
                                await client.Issue.Comment.Update(repo.Id, comment.Id, description.ToString());
                                continue;
                            }
                        }
                        // New error for the same repo-branch combination, so adding a new comment to it. 
                        if (!descriptionUpdateFlag)
                        {
                            Logger.LogInformation("Creating a new comment for the issue :" + issueNumber.Value);
                            await client.Issue.Comment.Create(repo.Id, issueNumber.Value, description.ToString());
                        }
                    }
                }
                else
                {
                    // Create a new issue for the error
                    string labelName = "DependencyUpdateError"; 
                    Logger.LogInformation("Dependency Update Error, for the error message " + repositoryBranchUpdateHistory.ErrorMessage +
                        " for the repository : " + repositoryBranchUpdateHistory.Repository);
                    var createIssue = new NewIssue("[Dependency Update] Errors during dependency updates to :" + repositoryBranchUpdateHistory.Repository);
                    string body = "The following errors have been detected when attempting to update dependencies in " +
                        repositoryBranchUpdateHistory.Repository + "\n\n";
                    createIssue.Body = body + description.ToString() + "\n\n**/FyiHandle :** " + _options.FyiHandle;
                    createIssue.Labels.Add(labelName);
                    var issue = await client.Issue.Create(repo.Id, createIssue);
                    Logger.LogInformation("Issue Number " + issue.Number + " was created in " + issueRepo);
                    await gitHubIssueEvaluator.SetAsync(tx, (repositoryBranchUpdateHistory.Repository, repositoryBranchUpdateHistory.Branch), issue.Number);
                }
                await tx.CommitAsync();
            }
        }

        private bool UpdateIssueCommentBody(RepositoryBranchUpdateHistoryEntry repositoryBranchUpdateHistory, string body)
        {
            Dictionary<string, string> argumentParser = parseIssueCommentBody(body);
            // Check if the error for the method UpdateAssetsAsync exists 
            if (string.Equals(argumentParser["method"], "UpdateAssetsAsync"))
            {
                string subId = GetSubscriptionId(repositoryBranchUpdateHistory.Arguments);
                if (string.Equals(argumentParser["subscriptionId"], subId) &&
                    string.Equals(argumentParser["errorMessage"], repositoryBranchUpdateHistory.ErrorMessage))
                {
                    return true;
                }
            }
            // Check if the error for the method ProcessPendingUpdatesAsync exists 
            if (string.Equals(argumentParser["method"], "ProcessPendingUpdatesAsync") &&
                string.Equals(argumentParser["errorMessage"], repositoryBranchUpdateHistory.ErrorMessage))
            {
                return true;
            }
            return false;
        }

        private Dictionary<string, string> parseIssueCommentBody (string body)
        {
            Dictionary<string, string> arguments = new Dictionary<string, string>();
            Regex rx = new Regex(@"(\w+): ([^,]+)(?:, |\))",
               RegexOptions.Compiled | RegexOptions.IgnoreCase);
            MatchCollection matches = rx.Matches(body);

            foreach (Match match in matches) {
                GroupCollection group = match.Groups;
                arguments.Add(group[1].Value, group[2].Value);
            }
            return arguments;
        }
        private string UpdateAsyncArgumentsParse(RepositoryBranchUpdateHistoryEntry repositoryBranchUpdateHistory)
        {
            string description = "";
            JArray arguments = JArray.Parse(repositoryBranchUpdateHistory.Arguments);
            string subscriptionId = arguments[0].ToString();
            Guid subscriptionGuid = GetSubscriptionGuid(subscriptionId);
            Maestro.Data.Models.Subscription subscription = (from sub in
                Context.Subscriptions
                where sub.Id == subscriptionGuid
                select sub).FirstOrDefault();
            // Subscription might be removed, so if the subscription does not exists then the error is no longer valid. So we can skip this error.
            if (subscription == null)
            {
                Logger.LogInformation("SubscriptionId:" + subscriptionId + " has been deleted for the repository : ");
                return string.Empty;
            }
            description = "[marker]: <> (subscriptionId: " + subscriptionId + ", " + "method: " + repositoryBranchUpdateHistory.Method + ", "+"errorMessage: " + repositoryBranchUpdateHistory.ErrorMessage + ")"
                + "\n**SubscriptionId:** " + subscriptionId
                + "\n**Source Repository :** " + subscription.SourceRepository + "\n" + "**Target Repository :** " + subscription.TargetRepository
                + "\n**Branch Name :** " + repositoryBranchUpdateHistory.Branch + "\n**Error Message :** " + repositoryBranchUpdateHistory.ErrorMessage
                + "\n**Method :** " + repositoryBranchUpdateHistory.Method + "\n**Action :** " + repositoryBranchUpdateHistory.Action
                + "\n**Last seen :** " + repositoryBranchUpdateHistory.Timestamp.ToString();

            return description;
        }

        private string GetSubscriptionId(string methodArguments)
        {
            JArray arguments = JArray.Parse(methodArguments);
            string subscriptionId = arguments[0].ToString();
            return subscriptionId;
        }
        // Get the subscriptionIdGuid from the subscriptionId
        private Guid GetSubscriptionGuid(string subscriptionId)
        {
            if (!Guid.TryParse(subscriptionId, out Guid subscriptionGuid))
            {
                throw new ArgumentException($"Subscription id '{subscriptionId}' is not a valid guid.");
            }
            return subscriptionGuid;
        }

        // This runs for 5 mins and waits for 5 mins.
        public Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(TimeSpan.FromMinutes(5));
        }
    }
}
