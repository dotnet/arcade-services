// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Newtonsoft.Json.Linq;
using Octokit;
using Org.BouncyCastle.Bcpg;

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

        private readonly DependencyUpdateErrorProcessorOptions _options;

        private ILogger<DependencyUpdateErrorProcessor> Logger { get; }

        private BuildAssetRegistryContext Context { get; }

        private static readonly Regex CommentMarker = new Regex(@"(\w+): '([^,\n]+)'(?:, |\))");

        //Runs every hour in staging for now, in production it will run once a day.
        [CronSchedule("0 0 0/1 1/1 * ? *", TimeZones.PST)]
        public async Task DependencyUpdateErrorProcessing()
        {
            if (_options.ConfigurationRefresherEndPointUri == null && _options.DynamicConfigs == null)
            {
                Logger.LogInformation("Dependency Update Error processor is disabled because no App Configuration was available.");
                return;
            }
            await _options.ConfigurationRefresherEndPointUri.Refresh();
            if (bool.TryParse(_options.DynamicConfigs["FeatureManagement:DependencyUpdateErrorProcessor"],
                    out var dependencyUpdateErrorProcessorFlag) && dependencyUpdateErrorProcessorFlag)
            {
                IReliableDictionary<string, DateTimeOffset> update =
                    await _stateManager.GetOrAddAsync<IReliableDictionary<string, DateTimeOffset>>("update");
                try
                {
                    DateTimeOffset checkpoint;
                    using (ITransaction tx = _stateManager.CreateTransaction())
                    {
                         checkpoint = await update.GetOrAddAsync(
                         tx,
                         "update",
                         DateTimeOffset.UtcNow 
                         //new DateTime(2002, 10, 18)
                         );
                        await tx.CommitAsync();
                    }
                    await CheckForErrorInRepositoryBranchHistoryTable(checkpoint, _options.GithubUrl, update);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unable to get the previous checkpoint time from reliable services.");
                }
            }
        }

        private async Task CheckForErrorInRepositoryBranchHistoryTable(DateTimeOffset checkPoint, string issueRepo,
            IReliableDictionary<string, DateTimeOffset> update)
        {
            // Looking for errors from the RepositoryBranchHistory table
            var unprocessedHistoryEntries =
                Context.RepositoryBranchUpdateHistory.Where(entry => entry.Success == false && entry.Timestamp > checkPoint.UtcDateTime)
                .OrderBy(entry => entry.Timestamp).ToList();
            if (!unprocessedHistoryEntries.Any())
            {
                Logger.LogInformation("No errors found in the RepositoryBranchUpdateHistory table.");
                return;
            }
            foreach (var error in unprocessedHistoryEntries)
            {
                try
                {
                    await CreateOrUpdateGithubIssueAsync(error, issueRepo);
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
                    Logger.LogError(ex,$"Unable to create a github issue for error message : '{error.ErrorMessage}' for the repository : '{error.Repository}'");
                }
            }
        }

        private async Task<GitHubClient> AuthenticateGitHubClient(string issueRepo)
        {
            IGitHubTokenProvider gitHubTokenProvider = Context.GetService<IGitHubTokenProvider>();
            var installationId = await Context.GetInstallationId(issueRepo);
            var gitHubToken = await gitHubTokenProvider.GetTokenForInstallationAsync(installationId);
            Logger.LogInformation($"GitHub token acquired for '{issueRepo}'");
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
            Octokit.ProductHeaderValue product = new Octokit.ProductHeaderValue("Maestro", version);
            return new GitHubClient(product)
            {
                Credentials = new Credentials(gitHubToken),
            };
        }

        private async Task CreateOrUpdateGithubIssueAsync(RepositoryBranchUpdateHistoryEntry updateHistoryError, string issueRepo)
        {
            Logger.LogInformation($"Error Message : '{updateHistoryError.ErrorMessage}' in repository :  '{updateHistoryError.Repository}'");
            var gitHubIssueEvaluator =
                await _stateManager.GetOrAddAsync<IReliableDictionary<(string, string), int>>("gitHubIssueEvaluator");
            var client = await AuthenticateGitHubClient(issueRepo);
            var repo = await client.Repository.Get(_options.Owner, _options.Repository);
            var issueNumber = new ConditionalValue<int>();
            using (ITransaction tx = _stateManager.CreateTransaction())
            {
                issueNumber = await gitHubIssueEvaluator.TryGetValueAsync(tx,
                    (updateHistoryError.Repository, updateHistoryError.Branch));
                await tx.CommitAsync();
            }
            if (issueNumber.HasValue)
            {
                Logger.LogInformation(
                    $"Updating a gitHub issue number : '{issueNumber}' for the error : '{updateHistoryError.ErrorMessage}'"
                    + $"for the repository : '{updateHistoryError.Repository}'");
                var issue = await client.Issue.Get(repo.Id, issueNumber.Value);
                var descriptionUpdateFlag = false;
                var comments = await client.Issue.Comment.GetAllForIssue(repo.Id, issue.Number);
                var updateIssueDescription = CreateOrUpdateDescription(updateHistoryError, issue.Body);
                // check if the issue body has to be updated or comment body has to be updated.
                if (updateIssueDescription.Item2)
                {
                    // Issue body will be updated
                    descriptionUpdateFlag = true;
                    var issueUpdate = issue.ToUpdate();
                    var bodyTitle = $"The following errors have been detected when attempting to update dependencies in " +
                        $"'{updateHistoryError.Repository}' \n\n";
                    issueUpdate.Body = bodyTitle + updateIssueDescription.Item1 + "\n\n**/FyiHandle :** " + _options.FyiHandle;
                    issueUpdate.State = ItemState.Open;
                    Logger.LogInformation($"Updating issue body for the issue :  '{issueNumber.Value}'");
                    await client.Issue.Update(repo.Id, issueNumber.Value, issueUpdate);
                }
                foreach (var comment in comments)
                {
                    descriptionUpdateFlag = false;
                    var updateCommentDescription = CreateOrUpdateDescription(updateHistoryError, comment.Body);
                    if (updateCommentDescription.Item2)
                    {
                        // Issue's comment body will be updated
                        descriptionUpdateFlag = true;
                        Logger.LogInformation($"Updating comment body for the issue : '{issueNumber.Value}'");
                        await client.Issue.Comment.Update(repo.Id, comment.Id, updateCommentDescription.Item1);
                        continue;
                    }
                }
                // New error for the same repo-branch combination, so adding a new comment to it. 
                if (!descriptionUpdateFlag && !string.IsNullOrEmpty(updateIssueDescription.Item1))
                {
                    Logger.LogInformation($"Creating a new comment for the issue : '{issueNumber.Value}'");
                    await client.Issue.Comment.Create(repo.Id, issueNumber.Value, updateIssueDescription.Item1);
                }
            }
            else
            {
                // Create a new issue for the error
                var labelName = "DependencyUpdateError";
                Logger.LogInformation(
                    $"Dependency Update Error, for the error message  '{updateHistoryError.ErrorMessage}'" +
                    $"for the repository : '{updateHistoryError.Repository}'");
                var createIssue = new NewIssue("[Dependency Update] Errors during dependency updates to :" + updateHistoryError.Repository);
                var bodyTitle = $"The following errors have been detected when attempting to update dependencies in " + 
                    $"'{updateHistoryError.Repository}' \n\n";
                var createIssueDescription = CreateOrUpdateDescription(updateHistoryError, "");
                if (createIssueDescription.Item2)
                {
                    createIssue.Body = bodyTitle + createIssueDescription.Item1 + "\n\n**/FyiHandle :** " +
                                       _options.FyiHandle;
                    createIssue.Labels.Add(labelName);
                    var issue = await client.Issue.Create(repo.Id, createIssue);
                    Logger.LogInformation($"Issue Number '{issue.Number}' was created in '{issueRepo}'");
                    // save the newly generated issue number in the reliable dictionary.
                    using (ITransaction tx = _stateManager.CreateTransaction())
                    {
                        await gitHubIssueEvaluator.SetAsync(tx, (updateHistoryError.Repository, updateHistoryError.Branch),
                            issue.Number);
                        await tx.CommitAsync();
                    }
                }
            }
        }

        private Tuple<string, bool> CreateOrUpdateDescription(RepositoryBranchUpdateHistoryEntry updateHistoryError , string body)
        {
            var description = new StringBuilder();
            var genericDescription =
                $"[marker]: <> (method: '{updateHistoryError.Method}', errorMessage: '{updateHistoryError.ErrorMessage}')"
                + $"\n**Repository :** '{updateHistoryError.Repository}'"
                + $"\n**Branch Name :** '{updateHistoryError.Branch}''"
                + $"\n**Error Message :**  '{updateHistoryError.ErrorMessage}'"
                + $"\n**Method :**   '{updateHistoryError.Method}'"
                + $"\n**Action :**  '{updateHistoryError.Action}'"
                + $"\n**Last seen :**  '{updateHistoryError.Timestamp}'";
            switch (updateHistoryError.Method)
            {
                // Only UpdateAssetAsync method has subscriptionId as one of the parameters in the arguments column. Other methods do not have this info.
                case "UpdateAssetsAsync":
                {
                    var updateAsync = ParseUpdateAssetsAsync(updateHistoryError);
                    // If the body is empty then this is a new issue/comment. 
                    // Append to the description only if the subscription is valid else do not create the issue
                    if(string.IsNullOrEmpty(body))
                    {
                        if(!string.IsNullOrEmpty(updateAsync))
                        {
                            return new Tuple<string, bool>(description.Append(updateAsync).ToString(), true);
                        }
                        Logger.LogInformation(
                            "The subscriptionId is not valid so skipping UpdateAssetsAsync method.");
                        return new Tuple<string, bool>(string.Empty, false);
                    }
                    else
                    {
                        var argumentParser = ParseIssueCommentBody(body);
                        var subId = GetSubscriptionId(updateHistoryError.Arguments);
                            // if issue already for the same subscriptionId and errorMessage exists then append description 
                            if (string.Equals(argumentParser["subscriptionId"], subId) &&
                            string.Equals(argumentParser["errorMessage"], updateHistoryError.ErrorMessage))
                        {
                            return new Tuple<string, bool>(description.Append(updateAsync).ToString(), true);
                        }
                    }
                    // issue/comment does not exists so just append to the description and create a new issue/ comment
                    return new Tuple<string, bool>(description.Append(updateAsync).ToString(), false);
                }
                // since this is not really an error, skipping this method
                case "SynchronizePullRequestAsync":
                    Logger.LogInformation("Skipping SynchronizePullRequestAsync method. Issue creation is not necessary for this method");
                    return new Tuple<string, bool>(string.Empty, false);
                // for all the other methods
                case "ProcessPendingUpdatesAsync":
                {
                    // if body is empty then we are creating an Issue/ comment.
                    if (string.IsNullOrEmpty(body))
                    {
                        return new Tuple<string, bool>(description.Append(genericDescription).ToString(), true);
                    }
                    // the issue already exists so preparing to update the issue/ comment.
                    else
                    {
                        // Check if the error for the method ProcessPendingUpdatesAsync already exists 
                        var argumentParser = ParseIssueCommentBody(body);
                        if (string.Equals(argumentParser["method"], "ProcessPendingUpdatesAsync") &&
                            string.Equals(argumentParser["errorMessage"], updateHistoryError.ErrorMessage))
                        {
                            return new Tuple<string, bool>(description.Append(genericDescription).ToString(), true);
                        }
                    }
                    // creates a new description body since the issue does not exists previously. 
                    return new Tuple<string, bool>(description.Append(genericDescription).ToString(), false);
                }
                default:
                    return new Tuple<string, bool>(description.Append(genericDescription).ToString(), false);
            }
        }

        /*      Eg: Body of Comment :
                The following errors have been detected when attempting to update dependencies in 'https://github.com/maestro-auth-test/maestro-test2' 

                [marker]: <> (subscriptionId: 'a9f0536e-15cc-4d1f-79a4-08d79acf8bba', method: 'UpdateAssetsAsync', errorMessage: 'build: 14	Unexpected error processing action: Validation Failed')
                **SubscriptionId:** 'a9f0536e-15cc-4d1f-79a4-08d79acf8bba'
                **Source Repository :**  'https://github.com/maestro-auth-test/maestro-test1'
                **Target Repository :**  'https://github.com/maestro-auth-test/maestro-test2'
                **Branch Name :**  '38'
                **Error Message :**  'build: 14	Unexpected error processing action: Validation Failed'
                **Method :** 'UpdateAssetsAsync'
                **Action :** 'Updating assets for subscription: ee8cdcfb-ee51-4bf3-55d3-08d79538f94d'
                **Last seen :** 'Maestro.Data.RepositoryBranchUpdateHistoryEntry'

                The Regex : (\w+): '([^,\n]+)'(?:, |\)) captures (subscriptionId: 'a9f0536e-15cc-4d1f-79a4-08d79acf8bba', method: 'UpdateAssetsAsync', errorMessage: 'build: 14	Unexpected error processing action: Validation Failed')
                the above string and puts it into two groups. 

                group[1]            group[2]
                subscriptionId      a9f0536e-15cc-4d1f-79a4-08d79acf8bba
                method              UpdateAssetsAsync
                errorMessage        build: 14	Unexpected error processing action: Validation Failed

                This is put into a dictionary, where group[1] is put in as key and group[2] as value.
        */
        private static Dictionary<string, string> ParseIssueCommentBody (string body)
        {
            var arguments = new Dictionary<string, string>();
            var matches = CommentMarker.Matches(body);

            foreach (Match match in matches) {
                var group = match.Groups;
                arguments.Add(group[1].Value, group[2].Value);
            }
            return arguments;
        }

        private string ParseUpdateAssetsAsync(RepositoryBranchUpdateHistoryEntry updateHistoryError)
        {
            var description = "";
            string subscriptionId = GetSubscriptionId(updateHistoryError.Arguments);
            Guid subscriptionGuid = GetSubscriptionGuid(subscriptionId); 
            var subscription = (from sub in
                Context.Subscriptions
                where sub.Id == subscriptionGuid
                select sub).FirstOrDefault();
            // Subscription might be removed, so if the subscription does not exists then the error is no longer valid. So we can skip this error.
            if (subscription == null)
            {
                Logger.LogInformation("SubscriptionId:" + subscriptionId + " has been deleted for the repository : ");
                return string.Empty;
            }
            description =
                $"[marker]: <> (subscriptionId: '{subscriptionId}', method: '{updateHistoryError.Method}', errorMessage: '{updateHistoryError.ErrorMessage}')"
                + $"\n**SubscriptionId:** '{subscriptionId}'"
                + $"\n**Source Repository :**  '{subscription.SourceRepository}'"
                + $"\n**Target Repository :**  '{subscription.TargetRepository}'"
                + $"\n**Branch Name :**  '{updateHistoryError.Branch}'"
                + $"\n**Error Message :**  '{updateHistoryError.ErrorMessage}'"
                + $"\n**Method :** '{updateHistoryError.Method}'"
                + $"\n**Action :** '{updateHistoryError.Action}'"
                + $"\n**Last seen :** '{updateHistoryError.Timestamp}'";
            return description;
        }

        private static string GetSubscriptionId(string methodArguments)
        {
            JArray arguments = JArray.Parse(methodArguments);
            var subscriptionId = arguments[0].ToString();
            return subscriptionId;
        }

        // Get the subscriptionIdGuid from the subscriptionId
        private static Guid GetSubscriptionGuid(string subscriptionId)
        {
            if (!Guid.TryParse(subscriptionId, out var subscriptionGuid))
            {
                throw new ArgumentException($"Subscription id '{subscriptionId}' is not a valid guid.");
            }
            return subscriptionGuid;
        }

        // This will run after the MaxValue is reached. In this case it will never run.
        public Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(TimeSpan.MaxValue);
        }
    }
}
