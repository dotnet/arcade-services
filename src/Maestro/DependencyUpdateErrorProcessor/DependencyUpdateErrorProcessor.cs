// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
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

        /// captures [marker]: <> (subscriptionId: 'a9f0536e-15cc-4d1f-79a4-08d79acf8bba', method: 'UpdateAssetsAsync', errorMessage: 'testing one')
        /// from the issue or comment body.
        private static readonly Regex CommentMarker = new Regex(@"\[marker\]: <> \(subscriptionId: '(?<subscriptionId>[^,]*)', method: '(?<method>[^,]*)'(?:, errorMessage: '(?<errorMessage>[^,]*)')?\)");

        [CronSchedule("0 0 0/1 1/1 * ? *", TimeZones.PST)]
        public async Task ProcessDependencyUpdateErrorsAsync()
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
                catch (Exception exe)
                {
                    Logger.LogError(exe, "Failed to create github issue.");
                }
            }
        }

        /// <summary>
        /// Looks for error in the Repository history table and process one record at a time.
        /// </summary>
        /// <param name="checkPoint"></param>
        /// <param name="issueRepo"></param>
        /// <param name="checkpointEvaluator"></param>
        /// <returns></returns>
        private async Task CheckForErrorsInRepositoryBranchHistoryTableAsync(DateTimeOffset checkPoint, string issueRepo,
            IReliableDictionary<string, DateTimeOffset> checkpointEvaluator)
        {
            // Looking for errors from the RepositoryBranchHistory table
            var unprocessedHistoryEntries =
                _context.RepositoryBranchUpdateHistory.Where(entry => entry.Success == false 
                && entry.Timestamp > checkPoint.UtcDateTime)
                .OrderBy(entry => entry.Timestamp).ToList();
            if (!unprocessedHistoryEntries.Any())
            {
                Logger.LogInformation($"No errors found in the RepositoryBranchUpdateHistory table. The last checkpoint time was : '{checkPoint}'");
                return;
            }
            foreach (var error in unprocessedHistoryEntries)
            {
                try
                {
                    await IssueDescriptionEvaluator(error, issueRepo);
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

        /// <summary>
        /// Github authentication.
        /// </summary>
        /// <param name="issueRepo"></param>
        /// <returns></returns>
        private async Task<GitHubClient> AuthenticateGitHubClient(string issueRepo)
        {
            IGitHubTokenProvider gitHubTokenProvider = _context.GetService<IGitHubTokenProvider>();
            long installationId = await _context.GetInstallationId(issueRepo);
            string gitHubToken = await gitHubTokenProvider.GetTokenForInstallationAsync(installationId);
            Logger.LogInformation($"GitHub token acquired for '{issueRepo}'");
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
            ProductHeaderValue product = new ProductHeaderValue("Maestro", version);
            return new GitHubClient(product)
            {
                Credentials = new Credentials(gitHubToken),
            };
        }

        /// <summary>
        /// Creates/updates the github issue.
        /// </summary>
        /// <param name="updateHistoryError"></param>
        /// <param name="issueRepo"></param>
        /// <param name="replaceDescription"></param>
        /// <param name="description"></param>
        /// <returns></returns>
        private async Task CreateOrUpdateGithubIssueAsync(RepositoryBranchUpdateHistoryEntry updateHistoryError, 
            string issueRepo, Func<string, string, bool> replaceDescription, string description)
        {
            Logger.LogInformation($"Error Message : '{updateHistoryError.ErrorMessage}' in repository :  '{updateHistoryError.Repository}'");
            IReliableDictionary<(string repository, string branch),int> gitHubIssueEvaluator =
                await _stateManager.GetOrAddAsync<IReliableDictionary<(string repository, string branch), int>>("gitHubIssueEvaluator");
            GitHubClient client = await AuthenticateGitHubClient(issueRepo);
            Octokit.Repository repo = await client.Repository.Get(_options.Owner, _options.Repository);
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
                        $@"Updating a gitHub issue number : '{issueNumber}' for the error : '{updateHistoryError.ErrorMessage}'
                         for the repository : '{updateHistoryError.Repository}'");
                    await UpdateIssueAsync(client, updateHistoryError, replaceDescription,
                        description, issue, repo.Id);
                }
                else
                {
                    // create a new issue for the existing repo-branch combo as the existing issue is closed.
                    Logger.LogInformation(
                        $@"Older issue {issue.Number} is closed so creating a new gitHub issue. 
                        Creating a new gitHub issue for dependency Update Error, for the error message  '{updateHistoryError.ErrorMessage}'
                        for the repository : '{updateHistoryError.Repository}'");
                    await CreateIssueAsync(client, updateHistoryError, gitHubIssueEvaluator, description, repo.Id,
                        issueRepo);
                }
            }
            else
            {
                // Create a new issue for the error
                Logger.LogInformation(
                    $@"Creating a new gitHub issue for dependency Update Error, 
                    for the error message  '{updateHistoryError.ErrorMessage}'
                    for the repository : '{updateHistoryError.Repository}'");
                await CreateIssueAsync(client, updateHistoryError, gitHubIssueEvaluator, description, repo.Id,
                    issueRepo);
            }
        }

        /// <summary>
        /// Creates a new github issue.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="updateHistoryError"></param>
        /// <param name="gitHubIssueEvaluator"></param>
        /// <param name="description"></param>
        /// <param name="repoId"></param>
        /// <param name="issueRepo"></param>
        /// <returns></returns>
        private async Task CreateIssueAsync(GitHubClient client, RepositoryBranchUpdateHistoryEntry updateHistoryError,
            IReliableDictionary<(string repository, string branch),int> gitHubIssueEvaluator, string description, long repoId, string issueRepo)
        {
            string labelName = "DependencyUpdateError";
            NewIssue newIssue = new NewIssue("[Dependency Update] Errors during dependency updates to :" +
                updateHistoryError.Repository);
            string bodyTitle = $@"The following errors have been detected when attempting to update dependencies in 
                '{updateHistoryError.Repository}' 
                 {Environment.NewLine}";
            if (!string.IsNullOrEmpty(description))
            {
                newIssue.Body = $@"{bodyTitle} {description}
                    **/FyiHandle :** {_options.FyiHandle}";
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

        /// <summary>
        /// Updates the issue body / comment body or creates a new comment for existing issue.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="updateHistoryError"></param>
        /// <param name="shouldReplaceDescription"></param>
        /// <param name="description"></param>
        /// <param name="issue"></param>
        /// <param name="repoId"></param>
        /// <returns></returns>
        private async Task UpdateIssueAsync(GitHubClient client, RepositoryBranchUpdateHistoryEntry updateHistoryError,
            Func<string, string, bool> shouldReplaceDescription, string description, Issue issue , long repoId)
        {
            // check if the issue body has to be updated or comment body has to be updated.
            if (shouldReplaceDescription(updateHistoryError.Arguments, issue.Body))
            {
                // Issue body will be updated
                IssueUpdate issueUpdate = issue.ToUpdate();
                string bodyTitle = $@"The following errors have been detected when attempting to update dependencies in
                    '{updateHistoryError.Repository}'
                     {Environment.NewLine}";
                issueUpdate.Body = $@"{bodyTitle}  {description}
                    **/FyiHandle :** {_options.FyiHandle}";
                Logger.LogInformation($"Updating issue body for the issue :  '{issue.Number}'");
                await client.Issue.Update(repoId, issue.Number, issueUpdate);
                return;
            }
            var comments = await client.Issue.Comment.GetAllForIssue(repoId, issue.Number);
            // If issue body is replaced with newer description, then do not create a new comment with the same description.
            foreach (var comment in comments)
            {
                if (shouldReplaceDescription(updateHistoryError.Arguments, comment.Body))
                {
                    // Issue's comment body will be updated
                    Logger.LogInformation($"Updating comment body for the issue : '{issue.Number}'");
                    await client.Issue.Comment.Update(repoId, comment.Id, description);
                    return;
                }
            }
            // New error for the same repo-branch combination, so adding a new comment to it. 
            if (!string.IsNullOrEmpty(description))
            {
                Logger.LogInformation($"Creating a new comment for the issue : '{issue.Number}'");
                await client.Issue.Comment.Create(repoId, issue.Number, description);
            }
        }

        /// <summary>
        /// Builds description and creates a func to check if the existing description has to be replaced or not.
        /// </summary>
        /// <param name="updateHistoryError"></param>
        /// <param name="issueRepo"></param>
        /// <returns></returns>
        private async Task IssueDescriptionEvaluator(RepositoryBranchUpdateHistoryEntry updateHistoryError, string issueRepo)
        {
            var description = "";
            Func<string, string, bool> shouldReplaceDescription;
            switch (updateHistoryError.Method)
            {
                // Only UpdateAssetAsync method has subscriptionId as one of the parameters in the arguments column. Other methods do not have this info.
                case "UpdateAssetsAsync":
                    description = ParseUpdateAssetsMethod(updateHistoryError);
                    if (!string.IsNullOrEmpty(description))
                    {
                        shouldReplaceDescription = (argument, issueBody) =>
                        {
                            if (!string.IsNullOrEmpty(issueBody))
                            {
                                var argumentParser =  ParseIssueCommentBody(issueBody);
                                var subId = GetSubscriptionId(updateHistoryError.Arguments);
                                if (!string.IsNullOrEmpty(argumentParser.subscriptionId) && 
                                    string.Equals(argumentParser.method, updateHistoryError.Method) &&
                                    string.Equals(argumentParser.subscriptionId, subId) &&
                                    string.Equals(argumentParser.errorMessage, updateHistoryError.ErrorMessage))
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
                        shouldReplaceDescription = (argument, issueBody) => false;
                    }
                    break;

                case "SynchronizePullRequestAsync":
                    Logger.LogInformation("Skipping SynchronizePullRequestAsync method. Issue creation is not necessary for this method");
                    shouldReplaceDescription = (argument, issueBody) => false;
                    break;

                case "ProcessPendingUpdatesAsync":
                    description = 
                        $@"[marker]: <> (subscriptionId: '', method: '{updateHistoryError.Method}', errorMessage: '{updateHistoryError.ErrorMessage}')
                        **Repository :** '{updateHistoryError.Repository}'
                        **Branch Name :** '{updateHistoryError.Branch}'
                        **Error Message :**  '{updateHistoryError.ErrorMessage}'
                        **Method :**   '{updateHistoryError.Method}'
                        **Action :**  '{updateHistoryError.Action}'
                        **Last seen :**  '{updateHistoryError.Timestamp}'";
                    shouldReplaceDescription = (argument, issueBody) =>
                    {
                        if (!string.IsNullOrEmpty(issueBody))
                        {
                            var argumentParser = ParseIssueCommentBody(issueBody);
                            if (!string.IsNullOrEmpty(argumentParser.errorMessage) && 
                                string.Equals(argumentParser.method, updateHistoryError.Method) &&
                                string.Equals(argumentParser.errorMessage, updateHistoryError.ErrorMessage))
                            {
                                return true;
                            }
                        }
                        return false;
                    };
                    break;

                // for all other methods
                default:
                    description =
                        $@"[marker]: <> (subscriptionId: '', method: '{updateHistoryError.Method}', errorMessage: '{updateHistoryError.ErrorMessage}')
                        **Repository :** '{updateHistoryError.Repository}'
                        **Branch Name :** '{updateHistoryError.Branch}'
                        **Error Message :**  '{updateHistoryError.ErrorMessage}'
                        **Method :**   '{updateHistoryError.Method}'
                        **Action :**  '{updateHistoryError.Action}'
                        **Last seen :**  '{updateHistoryError.Timestamp}'";
                    shouldReplaceDescription = (argument, issueBody) => true;
                    break;
                }
            await CreateOrUpdateGithubIssueAsync(updateHistoryError, issueRepo, shouldReplaceDescription, description);
        }

        /// <summary>
        /// Parses the regex and puts it in a dictionary.
        /// <example>For example:
        /// The regex captures : [marker]: <> (subscriptionId: 'a9f0536e-15cc-4d1f-79a4-08d79acf8bba',
        /// method: 'UpdateAssetsAsync', errorMessage: 'build: 14	Unexpected error processing action: Validation Failed')
        /// from the body of the issue/ comment. subscriptionId, errorMessage and method is extracted. 
        /// </example>
        /// </summary>
        /// <param name="body"></param>
        /// <returns></returns>
        private static (string subscriptionId, string errorMessage, string method) ParseIssueCommentBody(string body)
        {
            var match = CommentMarker.Match(body);
            var subscriptionId = match.Groups["subscriptionId"].Value;
            var errorMessage = match.Groups["errorMessage"].Value;
            var method = match.Groups["method"].Value;
            return (subscriptionId, errorMessage, method);
        }

        /// <summary>
        /// Parses arguments from UpdateAssetsAsync method and creates issue/ comment description.
        /// </summary>
        /// <param name="updateHistoryError"></param>
        /// <returns></returns>
        private string ParseUpdateAssetsMethod(RepositoryBranchUpdateHistoryEntry updateHistoryError)
        {
            var description = "";
            string subscriptionId = GetSubscriptionId(updateHistoryError.Arguments);
            Guid subscriptionGuid = GetSubscriptionGuid(subscriptionId);
            Maestro.Data.Models.Subscription subscription = (from sub in
                _context.Subscriptions
                where sub.Id == subscriptionGuid
                select sub).FirstOrDefault();
            // Subscription might be removed, so if the subscription does not exist then the error is no longer valid. So we can skip this error.
            if (subscription == null)
            {
                Logger.LogInformation("SubscriptionId:" + subscriptionId + " has been deleted for the repository : ");
                return string.Empty;
            }
            description =
                $@"[marker]: <> (subscriptionId: '{subscriptionId}', method: '{updateHistoryError.Method}', errorMessage: '{updateHistoryError.ErrorMessage}')
                **SubscriptionId:** '{subscriptionId}'
                **Source Repository :**  '{subscription.SourceRepository}'
                **Target Repository :**  '{subscription.TargetRepository}'
                **Branch Name :**  '{updateHistoryError.Branch}'
                **Error Message :**  '{updateHistoryError.ErrorMessage}'
                **Method :** '{updateHistoryError.Method}'
                **Action :** '{updateHistoryError.Action}'
                **Last seen :** '{updateHistoryError.Timestamp}'";
            return description;
        }

        private static string GetSubscriptionId(string methodArguments)
        {
            JArray arguments = JArray.Parse(methodArguments);
            var subscriptionId = arguments[0].ToString();
            return subscriptionId;
        }

        /// <summary>
        /// Gets the SubscriptionIdGuid from subscriptionId
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <returns></returns>
        private static Guid GetSubscriptionGuid(string subscriptionId)
        {
            if (!Guid.TryParse(subscriptionId, out var subscriptionGuid))
            {
                throw new ArgumentException($"Subscription id '{subscriptionId}' is not a valid guid.");
            }
            return subscriptionGuid;
        }

        /// <summary>
        /// This will run after the MaxValue is reached. In this case it will never run.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(TimeSpan.MaxValue);
        }
    }
}
