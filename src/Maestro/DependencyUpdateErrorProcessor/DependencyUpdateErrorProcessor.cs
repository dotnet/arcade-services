// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
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
    public sealed class DependencyUpdateErrorProcessor : IServiceImplementation
    {
        private readonly IReliableStateManager _stateManager;

        private readonly DependencyUpdateErrorProcessorOptions _options;

        private readonly ILogger<DependencyUpdateErrorProcessor> _logger;

        private readonly BuildAssetRegistryContext _context;

        // captures [marker]: <> (subscriptionId: 'a9f0536e-15cc-4d1f-79a4-08d79acf8bba', method: 'UpdateAssetsAsync', errorMessage: 'testing one')
        // from the issue or comment body.
        private static readonly Regex CommentMarker =
            new Regex(@"\[marker\]: <> \(subscriptionId: '(?<subscriptionId>[^,]*)', method: '(?<method>[^,]*)'(?:, errorMessage: '(?<errorMessage>[^,]*)')?\)");

        // Parses the github url and gives back the repo owner and repoId.
        // github url -> https://github.com/maestro-auth-test/maestro-test2  repo ->maestro-test2 owner -> maestro-auth-test2 
        private static readonly Regex RepositoryUriPattern = new Regex(@"^/(?<owner>[^/]+)/(?<repo>[^/]+)/?$");

        private readonly IGitHubApplicationClientFactory _authenticateGitHubApplicationClient;

        public DependencyUpdateErrorProcessor(
            IReliableStateManager stateManager,
            ILogger<DependencyUpdateErrorProcessor> logger,
            BuildAssetRegistryContext context,
            IOptions<DependencyUpdateErrorProcessorOptions> options,
            IGitHubApplicationClientFactory authenticateGithubApplicationClient
            )
        {
            _stateManager = stateManager;
            _context = context;
            _options = options.Value;
            _logger = logger;
            _authenticateGitHubApplicationClient = authenticateGithubApplicationClient;
        }

        [CronSchedule("0 0 5 1/1 * ? *", TimeZones.PST)]
        public async Task ProcessDependencyUpdateErrorsAsync()
        {
            if (_options.IsEnabled)
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
                    await CheckForErrorsInUpdateHistoryTablesAsync(
                        checkpoint,
                        _options.GithubUrl,
                        checkpointEvaluator);
                }
                catch (TimeoutException exe)
                {
                    _logger.LogError(exe, "Unable to connect to reliable services.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to create a github issue.");
                }
            }
        }

        /// <summary>
        /// Looks for errors in the update tables, and processes one record at a time to create or update corresponding issues.
        /// </summary>
        /// <param name="checkPoint">The last record processed datetime.</param>
        /// <param name="issueRepo">Repository where the gitHub issue has to be created.</param>
        /// <param name="checkpointEvaluator">Reliable dictionary that holds the time the last record was processed.</param>
        /// <returns></returns>
        private async Task CheckForErrorsInUpdateHistoryTablesAsync(
            DateTimeOffset checkPoint,
            string issueRepo,
            IReliableDictionary<string, DateTimeOffset> checkpointEvaluator)
        {
            // First get the un-processed entries from the RepositoryBranchHistory table
            List<UpdateHistoryEntry> unprocessedHistoryEntries =
                _context.RepositoryBranchUpdateHistory
                    .Where(entry => entry.Success == false &&
                           entry.Timestamp > checkPoint.UtcDateTime)
                    .ToList<UpdateHistoryEntry>();

            // Add in the SubscriptionUpdate errors:
            unprocessedHistoryEntries.AddRange(
                _context.SubscriptionUpdateHistory
                    .Where(entry => entry.Success == false &&
                        entry.Timestamp > checkPoint.UtcDateTime));

            // Sort union of these sets by timestamp, so the oldest checkpoint is the oldest unprocessed from both update history tables
            unprocessedHistoryEntries = unprocessedHistoryEntries.OrderBy(entry => entry.Timestamp).ToList();

            if (!unprocessedHistoryEntries.Any())
            {
                _logger.LogInformation($"No errors found in the 'RepositoryBranchUpdates' or 'SubscriptionUpdates' tables. The last checkpoint time was : '{checkPoint}'");
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
                    _logger.LogError(exe, $"Unable to update the last processed error timestamp : '{error.Timestamp}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Unable to create a github issue for error message : '{error.ErrorMessage}' for {GetPrintableDescription(error)}");
                }
            }
        }

        /// <summary>
        /// Creates/updates the github issue.
        /// </summary>
        /// <param name="updateHistoryError">Error info for which github issue has to be created</param>
        /// <param name="issueRepo">Repository where the github issue is created</param>
        /// <param name="shouldReplaceDescription">Func that carries info the description has to be replaced </param>
        /// <param name="description">Description for the issue body / comment body</param>
        /// <returns></returns>
        private async Task CreateOrUpdateGithubIssueAsync(
            UpdateHistoryEntry updateHistoryError,
            string issueRepo,
            Func<string, string, bool> shouldReplaceDescription,
            string description)
        {
            var parsedRepoUri = ParseRepoUri(issueRepo);
            IGitHubClient client = await _authenticateGitHubApplicationClient.CreateGitHubClientAsync(parsedRepoUri.owner, parsedRepoUri.repo);
            Repository repo = await client.Repository.Get(
                parsedRepoUri.owner,
                parsedRepoUri.repo);
            var issueNumber = new ConditionalValue<int>();

            switch (updateHistoryError)
            {
                case RepositoryBranchUpdateHistoryEntry repoBranchUpdateHistoryError:
                    {
                        _logger.LogInformation($"Error Message : '{repoBranchUpdateHistoryError.ErrorMessage}' in repository :  '{repoBranchUpdateHistoryError.Repository}'");

                        IReliableDictionary<(string repository, string branch), int> gitHubIssueEvaluator =
                            await _stateManager.GetOrAddAsync<IReliableDictionary<(string repository, string branch), int>>("gitHubIssueEvaluator");

                        using (ITransaction tx = _stateManager.CreateTransaction())
                        {
                            issueNumber = await gitHubIssueEvaluator.TryGetValueAsync(
                                tx,
                                (repoBranchUpdateHistoryError.Repository,
                                 repoBranchUpdateHistoryError.Branch));
                            await tx.CommitAsync();
                        }

                        if (issueNumber.HasValue)
                        {
                            // Found an existing issue, fall through to update.
                            break;
                        }
                        // Create a new issue for the error if the issue is already closed or the issue does not exist.
                        _logger.LogInformation($@"Creating a new gitHub issue for dependency Update Error, for the error message : '{repoBranchUpdateHistoryError.ErrorMessage} for the repository : '{repoBranchUpdateHistoryError.Repository}'");
                        await CreateDependencyUpdateErrorIssueAsync(
                            client,
                            repoBranchUpdateHistoryError,
                            gitHubIssueEvaluator,
                            description,
                            repo.Id,
                            issueRepo);
                        break;
                    }

                case SubscriptionUpdateHistoryEntry subscriptionUpdateHistoryError:
                    {
                        _logger.LogInformation($"Error Message : '{subscriptionUpdateHistoryError.ErrorMessage}' in subscription :  '{subscriptionUpdateHistoryError.SubscriptionId}'");

                        IReliableDictionary<Guid, int> gitHubIssueEvaluator =
                            await _stateManager.GetOrAddAsync<IReliableDictionary<Guid, int>>("gitHubSubscriptionIssueEvaluator");

                        using (ITransaction tx = _stateManager.CreateTransaction())
                        {
                            issueNumber = await gitHubIssueEvaluator.TryGetValueAsync(
                                tx,
                                subscriptionUpdateHistoryError.SubscriptionId);
                            await tx.CommitAsync();
                        }
                        if (issueNumber.HasValue)
                        {
                            // Found an existing issue, fall through to update.
                            break;
                        }
                        // Create a new issue for the error if the issue is already closed or the issue does not exist.
                        _logger.LogInformation($@"Creating a new gitHub issue for Subscription Update Error, for the error message : '{subscriptionUpdateHistoryError.ErrorMessage} for subscription : '{subscriptionUpdateHistoryError.SubscriptionId}'");
                        await CreateSubscriptionUpdateErrorIssueAsync(
                            client,
                            subscriptionUpdateHistoryError,
                            gitHubIssueEvaluator,
                            description,
                            repo.Id,
                            issueRepo);
                        break;
                    }

                default:
                    throw new InvalidOperationException($"Unknown update history entry type: {updateHistoryError.GetType()}");
            }

            // Updating an existing issue; can use same codepath.
            if (issueNumber.HasValue)
            {
                Issue issue = await client.Issue.Get(repo.Id, issueNumber.Value);
                // check if the issue is open only then update it else create a new issue and update the dictionary.
                if (issue.State.Equals("Open"))
                {
                    _logger.LogInformation($@"Updating a gitHub issue number : '{issueNumber}' for the error : '{updateHistoryError.ErrorMessage}' for {GetPrintableDescription(updateHistoryError)}");
                    await UpdateIssueAsync(client, updateHistoryError, shouldReplaceDescription, description, issue, repo.Id);
                    return;
                }
            }
        }

        /// <summary>
        ///     Parse out the owner and repo from a repository url
        /// </summary>
        /// <param name="uri">Github repository URL</param>
        /// <returns>Tuple of owner and repo</returns>
        public static (string owner, string repo) ParseRepoUri(string uri)
        {
            var u = new Uri(uri);
            Match match = RepositoryUriPattern.Match(u.AbsolutePath);
            if (!match.Success)
            {
                return default;
            }
            return (match.Groups["owner"].Value, match.Groups["repo"].Value);
        }

        public static string GetPrintableDescription(UpdateHistoryEntry entry)
        {
            if (entry is SubscriptionUpdateHistoryEntry)
            {
                return $"subscription: '{((SubscriptionUpdateHistoryEntry)entry).SubscriptionId}'";
            }
            if (entry is RepositoryBranchUpdateHistoryEntry)
            {
                return $"repository: '{((RepositoryBranchUpdateHistoryEntry)entry).Repository}', branch: '{((RepositoryBranchUpdateHistoryEntry)entry).Branch}'";
            }
            throw new NotImplementedException($"Please update GetPrintableDescription for type {entry.GetType()}. ");
        }

        /// <summary>
        /// Creates a new github issue.
        /// </summary>
        /// <param name="client">Github client</param>
        /// <param name="updateHistoryError">Error info for which github issue has to be created</param>
        /// <param name="gitHubIssueEvaluator">Reliable collection that holds the (key - 'repo-branch') and (value issueNumber)</param>
        /// <param name="description">Description for the issue body or comment body</param>
        /// <param name="repoId">Repository Id</param>
        /// <param name="issueRepo">Repository where issue has to be created.</param>
        /// <returns></returns>
        private async Task CreateDependencyUpdateErrorIssueAsync(
            IGitHubClient client,
            RepositoryBranchUpdateHistoryEntry updateHistoryError,
            IReliableDictionary<(string repository, string branch), int> gitHubIssueEvaluator,
            string description,
            long repoId,
            string issueRepo)
        {
            string labelName = "DependencyUpdateError";
            NewIssue newIssue = new NewIssue($@"[Dependency Update] Errors during dependency updates to : {updateHistoryError.Repository}");
            string bodyTitle = $@"The following errors have been detected when attempting to update dependencies in 
'{updateHistoryError.Repository}'

";
            newIssue.Body = $@"{bodyTitle} {description}
**/FyiHandle :** {_options.FyiHandle}";
            newIssue.Labels.Add(labelName);
            Issue issue = await client.Issue.Create(repoId, newIssue);
            _logger.LogInformation($"Issue Number '{issue.Number}' was created in '{issueRepo}'");
            // add or update the newly generated issue number in the reliable dictionary.
            using (ITransaction tx = _stateManager.CreateTransaction())
            {
                await gitHubIssueEvaluator.AddOrUpdateAsync(
                    tx,
                    (updateHistoryError.Repository,
                        updateHistoryError.Branch),
                    issue.Number,
                    (key, value) => issue.Number);
                await tx.CommitAsync();
            }
        }

        /// <summary>
        /// Creates a new github issue.
        /// </summary>
        /// <param name="client">Github client</param>
        /// <param name="updateHistoryError">Error info for which github issue has to be created</param>
        /// <param name="gitHubIssueEvaluator">Reliable collection that holds the (key - repo-branch) and (value issueNumber)</param>
        /// <param name="description">Description for the issue body or comment body</param>
        /// <param name="repoId">Repository Id</param>
        /// <param name="issueRepo">Repository where issue has to be created.</param>
        /// <returns></returns>
        private async Task CreateSubscriptionUpdateErrorIssueAsync(
            IGitHubClient client,
            SubscriptionUpdateHistoryEntry updateHistoryError,
            IReliableDictionary<Guid, int> gitHubIssueEvaluator,
            string description,
            long repoId,
            string issueRepo)
        {
            string labelName = "DependencyUpdateError";
            NewIssue newIssue = new NewIssue($@"[Subscription Update] Errors during subscription updates to {updateHistoryError.SubscriptionId}");
            string bodyTitle = $@"The following errors have been detected when attempting to perform updates for subscription
'{updateHistoryError.SubscriptionId}'

";
            newIssue.Body = $@"{bodyTitle} {description}
**/FyiHandle :** {_options.FyiHandle}";
            newIssue.Labels.Add(labelName);
            Issue issue = await client.Issue.Create(repoId, newIssue);
            _logger.LogInformation($"Issue Number '{issue.Number}' was created in '{issueRepo}'");
            // add or update the newly generated issue number in the reliable dictionary.
            using (ITransaction tx = _stateManager.CreateTransaction())
            {
                await gitHubIssueEvaluator.AddOrUpdateAsync(
                    tx,
                    updateHistoryError.SubscriptionId,
                    issue.Number,
                    (key, value) => issue.Number);
                await tx.CommitAsync();
            }
        }

        /// <summary>
        /// Updates the issue body / comment body or creates a new comment for existing issue.
        /// </summary>
        /// <param name="client">Github client</param>
        /// <param name="updateHistoryError">Error info for which github issue has to be created</param>
        /// <param name="shouldReplaceDescription">Func that carries info if the description in the issue has to be replaced or not.</param>
        /// <param name="description">Description for the issue body or comment body</param>
        /// <param name="issue">Already existing issue that has to be updated</param>
        /// <param name="repoId">Repository Id of the repo where the issue has to be updated.</param>
        /// <returns></returns>
        private async Task UpdateIssueAsync(
            IGitHubClient client,
            UpdateHistoryEntry updateHistoryError,
            Func<string, string, bool> shouldReplaceDescription,
            string description,
            Issue issue,
            long repoId)
        {
            // check if the issue body has to be updated or comment body has to be updated.
            if (shouldReplaceDescription(updateHistoryError.Arguments, issue.Body))
            {
                // Issue body will be updated
                IssueUpdate issueUpdate = issue.ToUpdate();
                string bodyTitle = $@"The following errors have been detected when attempting to update {GetPrintableDescription(updateHistoryError)}'";

                issueUpdate.Body = $@"{bodyTitle}  {description}
**/FyiHandle :** {_options.FyiHandle}";
                _logger.LogInformation($"Updating issue body for the issue :  '{issue.Number}'");
                await client.Issue.Update(
                    repoId,
                    issue.Number,
                    issueUpdate);
                return;
            }
            var comments = await client.Issue.Comment.GetAllForIssue(repoId, issue.Number);
            // If issue body is replaced with newer description, then do not create a new comment with the same description.
            foreach (var comment in comments)
            {
                if (shouldReplaceDescription(updateHistoryError.Arguments, comment.Body))
                {
                    // Issue's comment body will be updated
                    _logger.LogInformation($"Updating comment body for the issue : '{issue.Number}'");
                    await client.Issue.Comment.Update(
                        repoId,
                        comment.Id,
                        description);
                    return;
                }
            }
            // New error for the same repo-branch combination, so adding a new comment to it. 
            if (!string.IsNullOrEmpty(description))
            {
                _logger.LogInformation($"Creating a new comment for the issue : '{issue.Number}'");
                await client.Issue.Comment.Create(
                    repoId,
                    issue.Number,
                    description);
            }
        }

        /// <summary>
        /// Builds description and creates a func to check if the existing description has to be replaced or not.
        /// </summary>
        /// <param name="updateHistoryError">Error info for which github issue has to be created</param>
        /// <param name="issueRepo">Repository where issue has to be created.</param>
        /// <returns></returns>
        private async Task IssueDescriptionEvaluator(UpdateHistoryEntry updateHistoryError, string issueRepo)
        {
            var description = "";

            Func<string, string, bool> shouldReplaceDescription = (argument, issueBody) => true;
            switch (updateHistoryError)
            {
                // Only UpdateAssetAsync method has subscriptionId as one of the parameters in the arguments column. Other methods do not have this info.
                case RepositoryBranchUpdateHistoryEntry updateAssetsEntry when updateAssetsEntry.Method == "UpdateAssetsAsync":
                    description = ParseUpdateAssetsMethod(updateAssetsEntry);
                    if (!string.IsNullOrEmpty(description))
                    {
                        shouldReplaceDescription = (argument, issueBody) =>
                        {
                            if (!string.IsNullOrEmpty(issueBody))
                            {
                                (string subscriptionId, string errorMessage, string method) markerComponents = ParseIssueCommentBody(issueBody);
                                var subId = GetSubscriptionId(updateHistoryError.Arguments);
                                if (!string.IsNullOrEmpty(markerComponents.subscriptionId) &&
                                    string.Equals(markerComponents.method, updateHistoryError.Method) &&
                                    string.Equals(markerComponents.subscriptionId, subId) &&
                                    string.Equals(markerComponents.errorMessage, updateHistoryError.ErrorMessage))
                                {
                                    return true;
                                }
                            }
                            return false;
                        };
                    }
                    else
                    {
                        _logger.LogInformation(
                            "The subscriptionId is not valid so skipping UpdateAssetsAsync method.");
                        return;
                    }
                    break;

                case RepositoryBranchUpdateHistoryEntry updateAssetsEntry when updateAssetsEntry.Method == "SynchronizePullRequestAsync":
                    _logger.LogInformation("Skipping SynchronizePullRequestAsync method. Issue creation is not necessary for this method");
                    return;

                case RepositoryBranchUpdateHistoryEntry pendingUpdatesEntry when pendingUpdatesEntry.Method == "ProcessPendingUpdatesAsync":
                    description =
$@"[marker]: <> (subscriptionId: '', method: '{updateHistoryError.Method}', errorMessage: '{updateHistoryError.ErrorMessage.Replace("'", string.Empty)}')
**Repository :** '{pendingUpdatesEntry.Repository}'
**Branch Name :** '{pendingUpdatesEntry.Branch}'
**Error Message :**  '{updateHistoryError.ErrorMessage}'
**Method :**   '{updateHistoryError.Method}'
**Action :**  '{updateHistoryError.Action}'
**Last seen :**  '{updateHistoryError.Timestamp}'";
                    shouldReplaceDescription = (argument, issueBody) =>
                    {
                        if (!string.IsNullOrEmpty(issueBody))
                        {
                            (string subscriptionId, string errorMessage, string method) markerComponents = ParseIssueCommentBody(issueBody);
                            if (!string.IsNullOrEmpty(markerComponents.errorMessage) &&
                                string.Equals(markerComponents.method, updateHistoryError.Method) &&
                                string.Equals(markerComponents.errorMessage, updateHistoryError.ErrorMessage))
                            {
                                return true;
                            }
                        }
                        return false;
                    };
                    break;

                // for all other methods
                default:
                    string detailsBlock = "";
                    if (updateHistoryError is RepositoryBranchUpdateHistoryEntry)
                    {
                        detailsBlock = $@"**Repository :** '{((RepositoryBranchUpdateHistoryEntry)updateHistoryError).Repository}'
**Branch Name :** '{((RepositoryBranchUpdateHistoryEntry)updateHistoryError).Branch}'";
                    }
                    if (updateHistoryError is SubscriptionUpdateHistoryEntry)
                    {
                        detailsBlock = $"**Subscription id :** '{((SubscriptionUpdateHistoryEntry)updateHistoryError).SubscriptionId}'";
                    }

                    description =
$@"[marker]: <> (subscriptionId: '', method: '{updateHistoryError.Method}', errorMessage: '{updateHistoryError.ErrorMessage.Replace("'", string.Empty)}')
{detailsBlock}
**Error Message :**  '{updateHistoryError.ErrorMessage}'
**Method :**   '{updateHistoryError.Method}'
**Action :**  '{updateHistoryError.Action}'
**Last seen :**  '{updateHistoryError.Timestamp}'";
                    break;
            }
            await CreateOrUpdateGithubIssueAsync(
                updateHistoryError,
                issueRepo,
                shouldReplaceDescription,
                description);
        }

        /// <summary>
        /// Parses the regex and puts it in a dictionary.
        /// <example>For example:
        /// The regex captures : [marker]: <> (subscriptionId: 'a9f0536e-15cc-4d1f-79a4-08d79acf8bba',
        /// method: 'UpdateAssetsAsync', errorMessage: 'build: 14	Unexpected error processing action: Validation Failed')
        /// from the body of the issue/ comment. subscriptionId, errorMessage and method is extracted. 
        /// </example>
        /// </summary>
        /// <param name="body">Issue body / comment body</param>
        /// <returns>Tuple of subscriptionId, errorMessage and method</returns>
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
        /// <param name="updateHistoryError">Error info for which github issue has to be created</param>
        /// <returns>Description for the issue body/ comment body for UpdateAssetsAsyncMethod</returns>
        private string ParseUpdateAssetsMethod(RepositoryBranchUpdateHistoryEntry updateHistoryError)
        {
            if (updateHistoryError == null)
            {
                return string.Empty;
            }
            var description = "";
            string subscriptionId = GetSubscriptionId(updateHistoryError.Arguments);
            Guid subscriptionGuid = GetSubscriptionGuid(subscriptionId);
            Maestro.Data.Models.Subscription subscription = (from sub in _context.Subscriptions
                                                             where sub.Id == subscriptionGuid
                                                             select sub).FirstOrDefault();
            // Subscription might be removed, so if the subscription does not exist then the error is no longer valid. So we can skip this error.
            if (subscription == null)
            {
                _logger.LogInformation($@"SubscriptionId '{subscriptionId}' has been deleted for the repository : ");
                return string.Empty;
            }
            description =
$@"[marker]: <> (subscriptionId: '{subscriptionId}', method: '{updateHistoryError.Method}', errorMessage: '{updateHistoryError.ErrorMessage.Replace("'", string.Empty)}')
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
        /// <returns>SubscriptionId Guid</returns>
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
