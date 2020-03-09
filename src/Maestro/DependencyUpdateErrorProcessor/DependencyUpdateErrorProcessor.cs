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
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
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

        private IReliableStateManager _stateManager;

        private ILogger<DependencyUpdateErrorProcessor> Logger { get; }

        private BuildAssetRegistryContext Context { get; }

        private DependencyUpdateErrorProcessorOptions _options;

        //Runs every hour in staging for now, in production it will run once a day.
        [CronSchedule("0 0 0/1 1/1 * ? *", TimeZones.PST)]
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
                             DateTime.UtcNow 
                             );
                            await tx.CommitAsync();
                        }
                        await CheckForErrorInRepositoryBranchHistoryTable(previousTransaction, _options.GithubUrl);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex.Message, "Unable to get the previous transaction time from reliable services.");
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
                return;
            }
            Logger.LogInformation("Going to create the github issue.");
            try
            {
                foreach (var error in unprocessedHistoryEntries)
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
                        Logger.LogError(ex.Message, "Failed to retrieve previous transaction time.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex.Message, "Could not create a github issue.");
            }
        }

        private async Task<GitHubClient> AuthenticateGitHubClient(string issueRepo)
        {
            string gitHubToken = null;
            IGitHubTokenProvider gitHubTokenProvider = Context.GetService<IGitHubTokenProvider>();
            long installationId = await Context.GetInstallationId(issueRepo);
            gitHubToken = await gitHubTokenProvider.GetTokenForInstallationAsync(installationId);
            Logger.LogInformation("GitHub token acquired for " + issueRepo);
            Octokit.ProductHeaderValue _product;
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
            _product = new Octokit.ProductHeaderValue("Maestro", version);
            var client = new GitHubClient(_product);
            var token = new Credentials(gitHubToken);
            client.Credentials = token;
            return client;
        }

        private async Task CreateOrUpdateGithubIssueAsync(RepositoryBranchUpdateHistoryEntry repositoryBranchUpdateHistory , string issueRepo)
        {
            Logger.LogInformation("Something failed in the repository : " + repositoryBranchUpdateHistory.Repository);
            string fyiHandles = _options.FyiHandle;
            IReliableDictionary<(string, string), int> gitHubIssueEvaluator =
            await _stateManager.GetOrAddAsync<IReliableDictionary<(string,string), int>>("gitHubIssueEvaluator");
            var client = await AuthenticateGitHubClient(issueRepo);
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
                    description.Append("Repository :" + repositoryBranchUpdateHistory.Repository);
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
            description.Append("/FYI : " + fyiHandles);
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
                        string labelName = "DependencyUpdateError";
                        Logger.LogInformation("Creating a gitHub issue for the error : " + repositoryBranchUpdateHistory.ErrorMessage +
                            " for the repository : " + repositoryBranchUpdateHistory.Repository);
                        var createIssue = new NewIssue("Dependency Update Error");
                        createIssue.Body = description.ToString();
                        createIssue.Labels.Add(labelName) ;
                        try
                        { 
                            var issue = await client.Issue.Create(repo.Id, createIssue);
                            Logger.LogInformation("Issue Number " + issue.Number  + " was created in " + issueRepo);
                            await gitHubIssueEvaluator.SetAsync(tx, (repositoryBranchUpdateHistory.Repository, repositoryBranchUpdateHistory.Branch), issue.Number);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex.Message, "Unable to create an issue in GitHub for the error message : " + repositoryBranchUpdateHistory.ErrorMessage);
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
            description.Append("SubscriptionId: " + subscriptionId);
            description.Append(Environment.NewLine);
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
                    description.Append("Source Repository :" + subscription.SourceRepository);
                    description.Append(Environment.NewLine);
                    description.Append("Target Repository :" + subscription.TargetRepository);
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
