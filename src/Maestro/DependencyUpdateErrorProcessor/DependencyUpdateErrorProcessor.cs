// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            _context = context;
            _options = options.Value;
            Logger = logger;
        }

        private readonly IReliableStateManager _stateManager;

        private readonly DependencyUpdateErrorProcessorOptions _options;

        private ILogger<DependencyUpdateErrorProcessor> Logger { get; }

        private readonly BuildAssetRegistryContext _context;

        // captures (subscriptionId: 'a9f0536e-15cc-4d1f-79a4-08d79acf8bba', method: 'UpdateAssetsAsync', errorMessage: 'build: 14	Unexpected error processing action: Validation Failed')
        // from the issue or comment body and divides into 2 groups
        private static readonly Regex CommentMarker = new Regex(@"(\w+): '([^,\n]+)'(?:, |\))");

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
                IReliableDictionary<string, DateTimeOffset> checkpointEvaluator =
                    await _stateManager.GetOrAddAsync<IReliableDictionary<string, DateTimeOffset>>("checkpointEvaluator");
                try
                {
                    DateTimeOffset checkpoint;
                    using (ITransaction tx = _stateManager.CreateTransaction())
                    {
                         checkpoint = await checkpointEvaluator.GetOrAddAsync(
                         tx,
                         "checkpointEvaluator",
                         DateTimeOffset.UtcNow 
                         );
                        await tx.CommitAsync();
                    }
                    await CheckForErrorsInRepositoryBranchHistoryTableAsync(checkpoint, _options.GithubUrl, checkpointEvaluator);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unable to get the previous checkpoint time from reliable services.");
                }
            }
        }

        private async Task CheckForErrorsInRepositoryBranchHistoryTableAsync(DateTimeOffset checkPoint, string issueRepo,
            IReliableDictionary<string, DateTimeOffset> checkpointEvaluator)
        {
            // Looking for errors from the RepositoryBranchHistory table
            var unprocessedHistoryEntries =
                _context.RepositoryBranchUpdateHistory.Where(entry => entry.Success == false && entry.Timestamp > checkPoint.UtcDateTime)
                .OrderBy(entry => entry.Timestamp).ToList();
            if (!unprocessedHistoryEntries.Any())
            {
                Logger.LogInformation($"No errors found in the RepositoryBranchUpdateHistory table. The last checkpoint time was : '{checkPoint.DateTime}'");
                return;
            }
            foreach (var error in unprocessedHistoryEntries)
            {
                try
                {
                    await GetIssueDescription(error, issueRepo);
                    using (ITransaction tx = _stateManager.CreateTransaction())
                    {
                        await checkpointEvaluator.SetAsync(
                            tx,
                            "checkpointEvaluator",
                            error.Timestamp
                        );
                        await tx.CommitAsync();
                    }
                }
                catch (TimeoutException exe)
                {
                    Logger.LogError(exe, $"Unable to update the last processed error timestamp : '{error.Timestamp}");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Unable to create a github issue for error message : '{error.ErrorMessage}' for the repository : '{error.Repository}'");
                }
            }
        }

        private async Task<GitHubClient> AuthenticateGitHubClient(string issueRepo)
        {
            IGitHubTokenProvider gitHubTokenProvider = _context.GetService<IGitHubTokenProvider>();
            long installationId = await _context.GetInstallationId(issueRepo);
            string gitHubToken = await gitHubTokenProvider.GetTokenForInstallationAsync(installationId);
            Logger.LogInformation($"GitHub token acquired for '{issueRepo}'");
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
            ProductHeaderValue product = new ProductHeaderValue("Maestro", version);
            return new GitHubClient(product)
            {
                Credentials = new Credentials(gitHubToken),
            };
        }

        private async Task CreateOrUpdateGithubIssueAsync(RepositoryBranchUpdateHistoryEntry updateHistoryError, 
            string issueRepo, Func<string, string, bool> replaceDescription, string description)
        {
            Logger.LogInformation($"Error Message : '{updateHistoryError.ErrorMessage}' in repository :  '{updateHistoryError.Repository}'");
            var gitHubIssueEvaluator =
                await _stateManager.GetOrAddAsync<IReliableDictionary<(string repository, string branch), int>>("gitHubIssueEvaluator");
            GitHubClient client = await AuthenticateGitHubClient(issueRepo);
            Repository repo = await client.Repository.Get(_options.Owner, _options.Repository);
            var issueNumber = new ConditionalValue<int>();
            using (ITransaction tx = _stateManager.CreateTransaction())
            {
                issueNumber = await gitHubIssueEvaluator.TryGetValueAsync(tx,
                    (updateHistoryError.Repository, updateHistoryError.Branch));
                await tx.CommitAsync();
            }
            if (issueNumber.HasValue)
            {
                var issue = await client.Issue.Get(repo.Id, issueNumber.Value);
                // check if the issue is open only then update it else create a new issue and update the dictionary.
                if (issue.State.Equals("Open"))
                {
                    Logger.LogInformation(
                        $"Updating a gitHub issue number : '{issueNumber}' for the error : '{updateHistoryError.ErrorMessage}'"
                        + $"for the repository : '{updateHistoryError.Repository}'");
                    await UpdateIssueAsync(client, updateHistoryError, replaceDescription,
                        description, issue, repo.Id);
                }
                else
                {
                    // create a new issue for the existing repo-branch combo as the existing issue is closed.
                    Logger.LogInformation(
                        $"Older issue {issue.Number} is closed so creating a new gitHub issue. " +
                        $"Creating a new gitHub issue for dependency Update Error, for the error message  '{updateHistoryError.ErrorMessage}'" +
                        $"for the repository : '{updateHistoryError.Repository}'");
                    await CreateIssueAsync(client, updateHistoryError, gitHubIssueEvaluator, description, repo.Id,
                        issueRepo);
                }
            }
            else
            {
                // Create a new issue for the error
                Logger.LogInformation(
                    $"Creating a new gitHub issue for dependency Update Error, " +
                    $"for the error message  '{updateHistoryError.ErrorMessage}'" +
                    $"for the repository : '{updateHistoryError.Repository}'");
                await CreateIssueAsync(client, updateHistoryError, gitHubIssueEvaluator, description, repo.Id,
                    issueRepo);
            }
        }

        private async Task CreateIssueAsync(GitHubClient client, RepositoryBranchUpdateHistoryEntry updateHistoryError,
            IReliableDictionary<(string repository, string branch),int> gitHubIssueEvaluator, string description, long repoId, string issueRepo)
        {
            var labelName = "DependencyUpdateError";
            var newIssue = new NewIssue("[Dependency Update] Errors during dependency updates to :" +
                updateHistoryError.Repository);
            var bodyTitle = $"The following errors have been detected when attempting to update dependencies in " +
                $"'{updateHistoryError.Repository}' \n\n";
            if (!string.IsNullOrEmpty(description))
            {
                newIssue.Body = $"{bodyTitle} {description}  \n\n**/FyiHandle :** {_options.FyiHandle}";
                newIssue.Labels.Add(labelName);
                var issue = await client.Issue.Create(repoId, newIssue);
                Logger.LogInformation($"Issue Number '{issue.Number}' was created in '{issueRepo}'");
                // add or update the newly generated issue number in the reliable dictionary.
                using (ITransaction tx = _stateManager.CreateTransaction())
                {
                    await gitHubIssueEvaluator.AddOrUpdateAsync(tx,
                        (updateHistoryError.Repository, updateHistoryError.Branch),
                        issue.Number,
                        (key,value)=> issue.Number);
                    await tx.CommitAsync();
                }
            }
        }

        //Updates the issue body / comment body or creates a new comment
        private async Task UpdateIssueAsync(GitHubClient client, RepositoryBranchUpdateHistoryEntry updateHistoryError,
            Func<string, string, bool> replaceDescription, string description, Issue issue , long repoId)
        {
            bool descriptionUpdateFlag = false;
            var comments = await client.Issue.Comment.GetAllForIssue(repoId, issue.Number);
            // check if the issue body has to be updated or comment body has to be updated.
            if (replaceDescription(updateHistoryError.Arguments, issue.Body))
            {
                // Issue body will be updated
                descriptionUpdateFlag = true;
                IssueUpdate issueUpdate = issue.ToUpdate();
                var bodyTitle = $"The following errors have been detected when attempting to update dependencies in " +
                    $"'{updateHistoryError.Repository}' \n\n";
                issueUpdate.Body = $"{bodyTitle}  {description}  \n\n**/FyiHandle :** {_options.FyiHandle}";
                Logger.LogInformation($"Updating issue body for the issue :  '{issue.Number}'");
                await client.Issue.Update(repoId, issue.Number, issueUpdate);
            }
            // If issue body is replaced with newer description, then do not create a new comment with the same description.
            if (!descriptionUpdateFlag)
            {
                foreach (var comment in comments)
                {
                    descriptionUpdateFlag = false;
                    if (replaceDescription(updateHistoryError.Arguments, comment.Body))
                    {
                        // Issue's comment body will be updated
                        descriptionUpdateFlag = true;
                        Logger.LogInformation($"Updating comment body for the issue : '{issue.Number}'");
                        await client.Issue.Comment.Update(repoId, comment.Id, description);
                        continue;
                    }
                }
            }
            // New error for the same repo-branch combination, so adding a new comment to it. 
            if (!descriptionUpdateFlag && !string.IsNullOrEmpty(description))
            {
                Logger.LogInformation($"Creating a new comment for the issue : '{issue.Number}'");
                await client.Issue.Comment.Create(repoId, issue.Number, description);
            }
        }

        // Builds description and creates a func to replace the description from existing issue body/ comment body. 
        private Task GetIssueDescription(RepositoryBranchUpdateHistoryEntry updateHistoryError, string issueRepo)
        {
            var description = "";
            Func<string, string, bool> replaceDesc = (argument, issueBody) => false;
            switch (updateHistoryError.Method)
            {
                // Only UpdateAssetAsync method has subscriptionId as one of the parameters in the arguments column. Other methods do not have this info.
                case "UpdateAssetsAsync":
                {
                    description = ParseUpdateAssetsAsync(updateHistoryError);
                    if (!string.IsNullOrEmpty(description))
                    {
                        replaceDesc = (argument, issueBody) =>
                        {
                            if (!string.IsNullOrEmpty(issueBody))
                            {
                                var argumentParser = ParseIssueCommentBody(issueBody);
                                var subId = GetSubscriptionId(updateHistoryError.Arguments);
                                if (argumentParser.Count != 0 &&
                                    string.Equals(argumentParser["subscriptionId"], subId) &&
                                    string.Equals(argumentParser["errorMessage"], updateHistoryError.ErrorMessage))
                                {
                                    return true;
                                }
                            }
                            return false;
                        };
                    }
                    else
                    {
                        Logger.LogInformation(
                            "The subscriptionId is not valid so skipping UpdateAssetsAsync method.");
                    }
                }
                    break;
                case "SynchronizePullRequestAsync":
                    Logger.LogInformation("Skipping SynchronizePullRequestAsync method. Issue creation is not necessary for this method");
                    break;
                case "ProcessPendingUpdatesAsync":
                {
                    description = 
                        $"[marker]: <> (method: '{updateHistoryError.Method}', errorMessage: '{updateHistoryError.ErrorMessage}')"
                        + $"\n**Repository :** '{updateHistoryError.Repository}'"
                        + $"\n**Branch Name :** '{updateHistoryError.Branch}''"
                        + $"\n**Error Message :**  '{updateHistoryError.ErrorMessage}'"
                        + $"\n**Method :**   '{updateHistoryError.Method}'"
                        + $"\n**Action :**  '{updateHistoryError.Action}'"
                        + $"\n**Last seen :**  '{updateHistoryError.Timestamp}'";
                    replaceDesc = (argument, issueBody) =>
                    {
                        if (!string.IsNullOrEmpty(issueBody))
                        {
                            var argumentParser = ParseIssueCommentBody(issueBody);
                            if (argumentParser.Count != 0 && 
                                string.Equals(argumentParser["errorMessage"], updateHistoryError.ErrorMessage))
                            {
                                return true;
                            }
                        }
                        return false;
                    };
                    break;
                }
                // for all other methods
                default:
                {
                    description =
                        $"[marker]: <> (method: '{updateHistoryError.Method}', errorMessage: '{updateHistoryError.ErrorMessage}')"
                        + $"\n**Repository :** '{updateHistoryError.Repository}'"
                        + $"\n**Branch Name :** '{updateHistoryError.Branch}''"
                        + $"\n**Error Message :**  '{updateHistoryError.ErrorMessage}'"
                        + $"\n**Method :**   '{updateHistoryError.Method}'"
                        + $"\n**Action :**  '{updateHistoryError.Action}'"
                        + $"\n**Last seen :**  '{updateHistoryError.Timestamp}'";
                    replaceDesc = (argument, issueBody) => true;
                    break;
                }
            }
            return CreateOrUpdateGithubIssueAsync(updateHistoryError, issueRepo, replaceDesc, description);
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
                _context.Subscriptions
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
