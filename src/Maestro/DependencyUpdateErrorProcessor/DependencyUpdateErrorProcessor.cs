// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Newtonsoft.Json.Linq;
using Octokit;
using System.Linq.Expressions;
using Castle.DynamicProxy.Generators;

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

        private IReliableStateManager _stateManager;

        private ILogger<DependencyUpdateErrorProcessor> Logger { get; }

        private BuildAssetRegistryContext Context { get; }

        private DependencyUpdateErrorProcessorOptions _options;


        [CronSchedule("0 0/1 * 1/1 * ? *", TimeZones.PST)]
        public async Task DependencyUpdateErrorProcessing()
        {
           if (_options.ConfigurationRefresherdEndPointUri != null && _options.DynamicConfigs != null)
            {
                await _options.ConfigurationRefresherdEndPointUri.Refresh();
                bool.TryParse(_options.DynamicConfigs["FeatureManagement:DependencyUpdateErrorProcessor"], 
                    out var dependencyUpdateErrorProcessorFlag);
                if (dependencyUpdateErrorProcessorFlag)
                {
                    IReliableDictionary<string, DateTime> update =
                        await _stateManager.GetOrAddAsync<IReliableDictionary<string, DateTime>>("update");
                    DateTimeOffset previousTransaction;
                    try
                    {
                        using (ITransaction tx = _stateManager.CreateTransaction())
                        {
                            previousTransaction = await update.GetOrAddAsync(
                             tx,
                             "update",
                             //DateTime.UtcNow
                             new DateTime(2002, 10, 18)
                             );
                            await tx.CommitAsync();

                        }
                        await CheckForErrorInRepositoryBranchHistoryTable(previousTransaction, _options.GithubUrl);
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

        private async Task CheckForErrorInRepositoryBranchHistoryTable(DateTimeOffset previousTransaction , string issueRepo)
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
            }

            Logger.LogInformation("Going to create the github issue.");
            try
            {
                foreach (var error in unprocessedHistoryEntries)
                {
                    //Skipping creating gitHub issue for SynchonizePullRequest method as it is not considered as an error.
                    if (!error.Method.Equals("SynchronizePullRequestAsync"))
                    {
                        await CreateOrUpdateGithubIssueAsync(error, issueRepo);
                        IReliableDictionary<string, DateTime> update =
                                       await _stateManager.GetOrAddAsync<IReliableDictionary<string, DateTime>>("update");
                        try
                        {
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
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Failed to retrieve previous transaction time");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not create a github issue.");
            }
        }

        private GitHubClient AuthenticateGitHubClient(string creatingIssueInRepo)
        {
            string githubPat =  _options.GitHubPat;
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
            Octokit.ProductHeaderValue product = new Octokit.ProductHeaderValue("Maestro", version);
            var client = new GitHubClient(product);
            client.Credentials = new Credentials("fake", githubPat);
            return client;
        }

        private async Task CreateOrUpdateGithubIssueAsync(RepositoryBranchUpdateHistoryEntry repositoryBranchUpdateHistory , string issueRepo)
        {
            Logger.LogInformation("Something failed in the repository : " + repositoryBranchUpdateHistory.Repository);

            string fyiHandles = "@epananth";
            string label = "DependencyUpdateError";

            IReliableDictionary<(string, string), int> gitHubIssueEvaluator =
            await _stateManager.GetOrAddAsync<IReliableDictionary<(string,string), int>>("gitHubIssueEvaluator");
            //string repoBranchKey = repositoryBranchUpdateHistory.Repository + "_" + repositoryBranchUpdateHistory.Branch;
            var client = AuthenticateGitHubClient(issueRepo);
            StringBuilder description = new StringBuilder("Something failed during dependency update" +
                Environment.NewLine + Environment.NewLine);
            switch (repositoryBranchUpdateHistory.Method)
            {
                // Only UpdateAssetAsync method has subscriptionId as one of the parameters in the arguments column. Other methods do not have this info.
                case "UpdateAssetsAsync":
                    // append to the description only if the subscription is valid else do not create the issue
                    if (!String.IsNullOrEmpty(UpdateAsyncArgumentsParse(repositoryBranchUpdateHistory.Arguments)))
                    {
                        description.Append(UpdateAsyncArgumentsParse(repositoryBranchUpdateHistory.Arguments));
                        break;
                    }
                    return;
                // since this is not really an error, skipping this method
                case "SynchronizePullRequestAsync":
                    return;
                // for all the other methods
                default:
                    description.Append("Repository :" + $"[{repositoryBranchUpdateHistory.Repository}](repositoryUrl)");
                    break;
            }
            description.Append(Environment.NewLine);
            description.Append("Branch Name : " + repositoryBranchUpdateHistory.Branch);
            description.Append(Environment.NewLine);
            description.Append("Error Message : " + repositoryBranchUpdateHistory.ErrorMessage);
            description.Append(Environment.NewLine);
            description.Append("Method : " + repositoryBranchUpdateHistory.Method);
            description.Append(Environment.NewLine);
            description.Append("Action : " + repositoryBranchUpdateHistory.Action);
            description.Append(Environment.NewLine);
            description.Append($"/FYI {fyiHandles}");
            description.Append(Environment.NewLine + Environment.NewLine + Environment.NewLine);

            // Get repo info used for create/ update gitHub issue
            Octokit.Repository repo = await client.Repository.Get(_options.Owner, _options.Repository);

            try
            {
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
                            try
                            {
                                var issue = await client.Issue.Get(_options.Owner, _options.Repository, issueNumber.Value);
                                var issueUpdate = issue.ToUpdate();
                                issueUpdate.AddLabel("UpdateDependency");
                                issueUpdate.Body = issue.Body + Environment.NewLine 
                                    + Environment.NewLine + description.ToString();
                                issueUpdate.State = ItemState.Open;
                                await client.Issue.Update(_options.Owner ,_options.Repository, issueNumber.Value, issueUpdate);
                            }
                            catch(Exception ex)
                            {
                                Logger.LogError(ex.Message,"Unable to update issue number " + issueNumber + " in GitHub.");
                            }
                        }
                        else
                        {
                            Logger.LogInformation("Unable to retrive the issuenumber from the reliable services.");
                        }
                    }
                    else
                    {
                        // issue does not exists so just create a new issue
                        Logger.LogInformation("Creating a gitHub issue for the error : " + repositoryBranchUpdateHistory.ErrorMessage +
                            " for the repository : " + repositoryBranchUpdateHistory.Repository);
                        
                        var createIssue = new NewIssue("Update Dependency ");
                        createIssue.Body = description.ToString();
                        createIssue.Labels.Add("UpdateDependency") ;
                        try
                        {
                            var newLabel = new NewLabel(label, "e4e669");
                            await client.Issue.Labels.Create(_options.Owner, _options.Repository, newLabel);
                            var issue = await client.Issue.Create(repo.Id, createIssue);
                            Logger.LogInformation("Issue Number " + issue.Number  + " was created in " + issueRepo);
                            await gitHubIssueEvaluator.SetAsync(tx, (repositoryBranchUpdateHistory.Repository, repositoryBranchUpdateHistory.Branch), issue.Number);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex.Message, "Unable to create an issue in GitHub for the error message :" + repositoryBranchUpdateHistory.ErrorMessage);
                        }
                    }
                    await tx.CommitAsync();
                }
            }
            catch (Exception exc)
            {
                Logger.LogError(exc.Message, "Something went wrong, while trying to retrieve data from reliable service.");
            }
        }

        private string UpdateAsyncArgumentsParse(string methodArguments)
        {
            StringBuilder description = new StringBuilder();
            JArray arguments = JArray.Parse(methodArguments);
            string subscriptionId = arguments[0].ToString();
            Guid subscriptionGuid = GetSubscriptionGuid(subscriptionId);
            description.Append("SubscriptionId: " + $"{ subscriptionId}" +
                Environment.NewLine);
            if (subscriptionGuid != null)
            {
                Maestro.Data.Models.Subscription subscription = (from sub in
                    Context.Subscriptions where sub.Id == subscriptionGuid select sub).FirstOrDefault();
                // Subscription might be removed, so if the subscription does not exists then the error is no longer valid. So we can skip this error.
                if (subscription == null)
                {
                    Logger.LogInformation("SubscriptionId :" + subscriptionId + " has been deleted for the repository : ");
                    return string.Empty;
                }
                else
                {
                    description.Append("Source Repository :" + subscription.SourceRepository +
                        Environment.NewLine +
                        "Target Repository :" + subscription.TargetRepository);
                }
            }
            return description.ToString();
        }

        // Get the subscriptionIdGuid from the subscriptionId
        private Guid GetSubscriptionGuid(string subscriptionId)
        {
            var guid = new Guid();
            try
            {
                if (Guid.TryParse(subscriptionId, out Guid subscriptionGuid))
                {
                    return subscriptionGuid;
                }
            }
            catch(Exception ex)
            {
                Logger.LogError(ex.Message, ("Subscription id " + subscriptionId + " is not a valid guid."));
            }
            return guid;
        }

        // This runs for 5 mins and waits for 5 mins.
        public Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(TimeSpan.FromMinutes(5));
        }
    }
}
