// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.Dotnet.GitHub.Authentication;
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

        private IGitHubClientFactory AuthenticateGitHubClient { get; }

        public DependencyUpdateErrorProcessor(
            IReliableStateManager stateManager,
            ILogger<DependencyUpdateErrorProcessor> logger,
            BuildAssetRegistryContext context,
            IOptions<DependencyUpdateErrorProcessorOptions> options,
            IGitHubClientFactory authenticateGithubClient
            )
        {
            _stateManager = stateManager;
            _context = context;
            _options = options.Value;
            _logger = logger;
            AuthenticateGitHubClient = authenticateGithubClient;
        }

       [CronSchedule("0 0 0/1 1/1 * ? *", TimeZones.PST)]
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
                    await CheckForErrorsInRepositoryBranchHistoryTableAsync(
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
        /// Looks for error in the Repository history table and process one record at a time. Which is later processed and creates an issue/ updates existing issue.
        /// </summary>
        /// <param name="checkPoint">The last record processed datetime.</param>
        /// <param name="issueRepo">Repository where the gitHub issue has to be created.</param>
        /// <param name="checkpointEvaluator">Reliable dictionary that holds the time the last record was processed.</param>
        /// <returns></returns>
        private async Task CheckForErrorsInRepositoryBranchHistoryTableAsync(
            DateTimeOffset checkPoint, 
            string issueRepo,
            IReliableDictionary<string, DateTimeOffset> checkpointEvaluator)
        {
            // Looking for errors from the RepositoryBranchHistory table
            var unprocessedHistoryEntries =
                _context.RepositoryBranchUpdateHistory
                    .Where(entry => entry.Success == false && 
                        entry.Timestamp > checkPoint.UtcDateTime)
                    .OrderBy(entry => entry.Timestamp)
                    .ToList();
            if (!unprocessedHistoryEntries.Any())
            {
                _logger.LogInformation($"No errors found in the RepositoryBranchUpdateHistory table. The last checkpoint time was : '{checkPoint}'");
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
                    _logger.LogError(ex, $"Unable to create a github issue for error message : '{error.ErrorMessage}' for the repository : '{error.Repository}' and branch : '{error.Branch}'");
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
            RepositoryBranchUpdateHistoryEntry updateHistoryError, 
            string issueRepo, 
            Func<string, string, bool> shouldReplaceDescription, 
            string description)
        {
            _logger.LogInformation($"Error Message : '{updateHistoryError.ErrorMessage}' in repository :  '{updateHistoryError.Repository}'");
            IReliableDictionary<(string repository, string branch),int> gitHubIssueEvaluator =
                await _stateManager.GetOrAddAsync<IReliableDictionary<(string repository, string branch), int>>("gitHubIssueEvaluator");
            var parseRepoUri = ParseRepoUri(issueRepo);
            IGitHubClient client = await AuthenticateGitHubClient.CreateGitHubClientAsync(parseRepoUri.owner, parseRepoUri.repo);
            Octokit.Repository repo = await client.Repository.Get(
                parseRepoUri.owner, 
                parseRepoUri.repo);
            var issueNumber = new ConditionalValue<int>();
            using (ITransaction tx = _stateManager.CreateTransaction())
            {
                issueNumber = await gitHubIssueEvaluator.TryGetValueAsync(
                    tx,
                    (updateHistoryError.Repository, 
                        updateHistoryError.Branch));
                await tx.CommitAsync();
            }
            if (issueNumber.HasValue)
            {
                Issue issue = await client.Issue.Get(repo.Id, issueNumber.Value);
                // check if the issue is open only then update it else create a new issue and update the dictionary.
                if (issue.State.Equals("Open"))
                {
                    _logger.LogInformation($@"Updating a gitHub issue number : '{issueNumber}' for the error : '{updateHistoryError.ErrorMessage}' for the repository : '{updateHistoryError.Repository}'");
                    await UpdateIssueAsync(
                        client, 
                        updateHistoryError,
                        shouldReplaceDescription,
                        description,
                        issue,
                        repo.Id);
                    return;
                }
            }
            // Create a new issue for the error if the issue is already closed or the issue does not exists.
                _logger.LogInformation($@"Creating a new gitHub issue for dependency Update Error, for the error message : '{updateHistoryError.ErrorMessage} for the repository : '{updateHistoryError.Repository}'");
                await CreateIssueAsync(
                    client,
                    updateHistoryError, 
                    gitHubIssueEvaluator, 
                    description,
                    repo.Id,
                    issueRepo);
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

        /// <summary>
        /// Creates a new github issue.
        /// </summary>
        /// <param name="client">Github client</param>
        /// <param name="updateHistoryError">Error info for which github issue has to be created</param>
        /// <param name="gitHubIssueEvaluator">Reliable service that holds the (key - repo-branch) and (value issueNumber)</param>
        /// <param name="description">Description for the issue body or comment body</param>
        /// <param name="repoId">Repository Id</param>
        /// <param name="issueRepo">Repository where issue has to be created.</param>
        /// <returns></returns>
        private async Task CreateIssueAsync(
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
            RepositoryBranchUpdateHistoryEntry updateHistoryError,
            Func<string, string, bool> shouldReplaceDescription, 
            string description,
            Issue issue , 
            long repoId)
        {
            // check if the issue body has to be updated or comment body has to be updated.
            if (shouldReplaceDescription(updateHistoryError.Arguments, issue.Body))
            {
                // Issue body will be updated
                IssueUpdate issueUpdate = issue.ToUpdate();
                string bodyTitle = $@"The following errors have been detected when attempting to update dependencies in
'{updateHistoryError.Repository}'

";
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
                                (string subscriptionId, string errorMessage, string method) markerComponents =  ParseIssueCommentBody(issueBody);
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

                case "SynchronizePullRequestAsync":
                    _logger.LogInformation("Skipping SynchronizePullRequestAsync method. Issue creation is not necessary for this method");
                    return;

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
                _logger.LogInformation($@"SubscriptionId '{subscriptionId}' has been deleted for the repository : ");
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
