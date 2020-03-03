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
            IOptions<DependencyUpdateErrorProcessorOptions> errorProcessor
            )
        {
            StateManager = stateManager;
            Logger = logger;
            Context = context;
            ErrorProcessor = errorProcessor.Value;
        }

        private IReliableStateManager StateManager { get; }

        private ILogger<DependencyUpdateErrorProcessor> Logger { get; }

        private BuildAssetRegistryContext Context { get; }

        private DependencyUpdateErrorProcessorOptions ErrorProcessor { get; }


        [CronSchedule("0 0/1 * 1/1 * ? *", TimeZones.PST)]
        public async Task DependencyUpdateErrorProcessing()
        {

            if (ErrorProcessor.ConfigurationRefresherdPointUri != null && ErrorProcessor.DynamicConfigs != null)
            {
                await ErrorProcessor.ConfigurationRefresherdPointUri.Refresh();
                
                bool.TryParse(ErrorProcessor.DynamicConfigs["FeatureManagement:DependencyUpdateErrorProcessor"], 
                    out var dependencyUpdateErrorProcessorFlag);
                if (dependencyUpdateErrorProcessorFlag)
                {
                    IReliableDictionary<string, DateTime> update =
                        await StateManager.GetOrAddAsync<IReliableDictionary<string, DateTime>>("update");
                    try
                    {
                        using (ITransaction tx = StateManager.CreateTransaction())
                        {
                            var previousTransaction = await update.GetOrAddAsync(
                             tx,
                             "update",
                             //DateTime.UtcNow
                             new DateTime(2002, 10, 18)
                             );
                            await tx.CommitAsync();
                            await CheckForErrorInRepositoryBranchHistoryTable(previousTransaction, ErrorProcessor.GithubUrl);
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

        private async Task CheckForErrorInRepositoryBranchHistoryTable(DateTimeOffset previousTransaction , string repository)
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
                        await CreateGitHubIssueAsync(error, repository);
                        IReliableDictionary<string, DateTime> update =
                                       await StateManager.GetOrAddAsync<IReliableDictionary<string, DateTime>>("update");
                        try
                        {
                            using (ITransaction tx = StateManager.CreateTransaction())
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
                            Logger.LogError(ex, "Failed to create a GitHub issue for the error : " + error.ErrorMessage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not create a github issue.");
            }

        }

        private async Task<GitHubClient> AuthenticateGitHubClient(string creatingIssueInRepo)
        {
            string gitHubToken = null;
            IGitHubTokenProvider gitHubTokenProvider = Context.GetService<IGitHubTokenProvider>();
            long installationId = await Context.GetInstallationId(creatingIssueInRepo);
            gitHubToken = await gitHubTokenProvider.GetTokenForInstallationAsync(installationId);

            Logger.LogInformation("GitHub token acquired for " + creatingIssueInRepo);

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
        private async Task CreateGitHubIssueAsync(RepositoryBranchUpdateHistoryEntry repositoryBranchUpdateHistory , string creatingIssueInRepo)
        {
            Logger.LogInformation("Something failed in the repository : " + repositoryBranchUpdateHistory.Repository);

            string fyiHandles = "@epananth";
            string label = "UpdateDependency";

            IReliableDictionary<string, int> gitHubIssueEvaulator =
            await StateManager.GetOrAddAsync<IReliableDictionary<string, int>>("gitHubIssueEvaulator");
            string repoBranchKey = repositoryBranchUpdateHistory.Repository + "_" + repositoryBranchUpdateHistory.Branch;

            var client = await AuthenticateGitHubClient(creatingIssueInRepo);

            StringBuilder description = new StringBuilder("Something failed during dependency update" +
                Environment.NewLine);

            switch (repositoryBranchUpdateHistory.Method)
            {
                // Only UpdateAssetAsync method has subscriptionId as one of the parameters in the arguments column. Other methods do not have this info.
                case "UpdateAssetsAsync":
                    // Get the subscription details
                    JArray arguments = JArray.Parse(repositoryBranchUpdateHistory.Arguments);
                    string subscriptionId = arguments[0].ToString();
                    Guid subscriptionGuid = GetSubscriptionGuid(subscriptionId);
                    description.Append("SubscriptionId: " + $"{ arguments[0]}" +
                        Environment.NewLine);
                    List<Maestro.Data.Models.Subscription> subscription = (from sub in 
                        Context.Subscriptions where sub.Id == subscriptionGuid select sub).ToList();
                    // Subscription might be removed 
                    if (subscription.Count == 0)
                    {
                        Logger.LogInformation("SubscriptionId :" + subscriptionId + " has been deleted.");
                        break;
                    }
                    else
                    {
                        description.Append("Source Repository :" + subscription[0].SourceRepository +
                            $"{Environment.NewLine} {Environment.NewLine}" +
                            "Target Repository :" + subscription[0].TargetRepository);
                    }
                    break;
                    // for all the other methods
                default:
                    description.Append("Repository :" + $"[{repositoryBranchUpdateHistory.Repository}](repositoryUrl)");
                    break;

            }

            description.Append(Environment.NewLine +
                "Branch Name :" + repositoryBranchUpdateHistory.Branch +
                Environment.NewLine +
                "Error Message :" +  repositoryBranchUpdateHistory.ErrorMessage +
                Environment.NewLine +
                "Method :" + repositoryBranchUpdateHistory.Method +
                Environment.NewLine +
                "Action :" + repositoryBranchUpdateHistory.Action +
                Environment.NewLine +
                $"/FYI {fyiHandles} " + 
                Environment.NewLine);

            // Get repo info used for create/ update gitHub issue
            Octokit.Repository repo = await client.Repository.Get(ErrorProcessor.Owner, ErrorProcessor.Repository);

            try
            {
                using (ITransaction tx = StateManager.CreateTransaction())
                {
                    if (await gitHubIssueEvaulator.ContainsKeyAsync(tx, repoBranchKey))
                    {
                        // issue exists so just update the issue
                        var issueNumber = await gitHubIssueEvaulator.TryGetValueAsync(tx, repoBranchKey);
                        Logger.LogInformation("Updating a gitHub issue number : " + issueNumber + " for the error : " + repositoryBranchUpdateHistory.ErrorMessage +
                            " for the repository : " + repositoryBranchUpdateHistory.Repository);
                        try
                        {
                            IssueUpdate issueUpdate = new IssueUpdate
                            {
                                Body = description.ToString(),
                            };
                            await client.Issue.Update(repo.Id, issueNumber.Value, issueUpdate);
                        }
                        catch 
                        {
                            Logger.LogError("Unable to update issue number " + issueNumber + " in GitHub.") ;
                        }
                    }
                    else
                    {
                        // issue does not exists so just create a new issue
                        Logger.LogInformation("Creating a gitHub issue for the error : " + repositoryBranchUpdateHistory.ErrorMessage +
                            " for the repository : " + repositoryBranchUpdateHistory.Repository);
                        var createIssue = new NewIssue("Update Dependency ");
                        createIssue.Body = description.ToString();
                        createIssue.Labels.Add(label);
                        try
                        {
                            var issue = await client.Issue.Create(repo.Id, createIssue);
                            Logger.LogInformation("Issue Number " + issue.Number  + " was created in " + creatingIssueInRepo);
                            await gitHubIssueEvaulator.SetAsync(tx, repoBranchKey, issue.Number);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("Unable to create an issue in GitHub" + ex.ToString());
                        }
                    }
                    await tx.CommitAsync();
                }
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, "Something failed while attempting to create an issue based on repo " + repositoryBranchUpdateHistory.Repository);
            }
        }

        // Get the subscriptionIdGuid from the subscriptionId
        private Guid GetSubscriptionGuid(string subscriptionId)
        {
            if (!Guid.TryParse(subscriptionId, out Guid subscriptionGuid))
            {
                throw new ArgumentException("Subscription id "+ subscriptionId + " is not a valid guid.");
            }
            return subscriptionGuid;
        }

        // This method runs everytime till the MaxValue is reached. Since we are implementing IServiceImplementation, this is one of the default method. Since we 
        public Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(TimeSpan.MaxValue);
        }
    }
}
