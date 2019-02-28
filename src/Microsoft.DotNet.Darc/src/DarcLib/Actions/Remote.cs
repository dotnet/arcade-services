// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib
{
    public class Remote : IRemote
    {
        private readonly IBarClient _barClient;
        private readonly GitFileManager _fileManager;
        private readonly IGitRepo _gitClient;
        private readonly ILogger _logger;

        public Remote(IGitRepo gitClient, IBarClient barClient, ILogger logger)
        {
            _logger = logger;
            _barClient = barClient;
            _gitClient = gitClient;

            if (_gitClient != null)
            {
                _fileManager = new GitFileManager(_gitClient, _logger);
            }
        }

        /// <summary>
        ///     Retrieve a list of default channel associations.
        /// </summary>
        /// <param name="repository">Optionally filter by repository</param>
        /// <param name="branch">Optionally filter by branch</param>
        /// <param name="channel">Optionally filter by channel</param>
        /// <returns>Collection of default channels.</returns>
        public Task<IEnumerable<DefaultChannel>> GetDefaultChannelsAsync(
            string repository = null,
            string branch = null,
            string channel = null)
        {
            CheckForValidBarClient();
            return _barClient.GetDefaultChannelsAsync(repository: repository,
                                                      branch: branch,
                                                      channel: channel);
        }

        /// <summary>
        ///     Adds a default channel association.
        /// </summary>
        /// <param name="repository">Repository receiving the default association</param>
        /// <param name="branch">Branch receiving the default association</param>
        /// <param name="channel">Name of channel that builds of 'repository' on 'branch' should automatically be applied to.</param>
        /// <returns>Async task.</returns>
        public Task AddDefaultChannelAsync(string repository, string branch, string channel)
        {
            CheckForValidBarClient();
            return _barClient.AddDefaultChannelAsync(repository, branch, channel);
        }

        /// <summary>
        ///     Removes a default channel based on the specified criteria
        /// </summary>
        /// <param name="repository">Repository having a default association</param>
        /// <param name="branch">Branch having a default association</param>
        /// <param name="channel">Name of channel that builds of 'repository' on 'branch' are being applied to.</param>
        /// <returns>Async task</returns>
        public Task DeleteDefaultChannelAsync(string repository, string branch, string channel)
        {
            CheckForValidBarClient();
            return _barClient.DeleteDefaultChannelAsync(repository, branch, channel);
        }

        /// <summary>
        ///     Creates a new channel in the Build Asset Registry
        /// </summary>
        /// <param name="name">Name of channel</param>
        /// <param name="classification">Classification of the channel</param>
        /// <returns>Newly created channel</returns>
        public Task<Channel> CreateChannelAsync(string name, string classification)
        {
            CheckForValidBarClient();
            return _barClient.CreateChannelAsync(name, classification);
        }

        /// <summary>
        /// Deletes a channel from the Build Asset Registry
        /// </summary>
        /// <param name="name">Name of channel</param>
        /// <returns>Channel just deleted</returns>
        public Task<Channel> DeleteChannelAsync(int id)
        {
            CheckForValidBarClient();
            return _barClient.DeleteChannelAsync(id);
        }

        /// <summary>
        ///     Get a set of subscriptions based on input filters.
        /// </summary>
        /// <param name="sourceRepo">Filter by the source repository of the subscription.</param>
        /// <param name="targetRepo">Filter by the target repository of the subscription.</param>
        /// <param name="channelId">Filter by the target channel id of the subscription.</param>
        /// <returns>Set of subscription.</returns>
        public Task<IEnumerable<Subscription>> GetSubscriptionsAsync(
            string sourceRepo = null,
            string targetRepo = null,
            int? channelId = null)
        {
            CheckForValidBarClient();
            return _barClient.GetSubscriptionsAsync(sourceRepo: sourceRepo,
                                                    targetRepo: targetRepo,
                                                    channelId: channelId);
        }

        /// <summary>
        ///     Retrieve a subscription by ID
        /// </summary>
        /// <param name="subscriptionId">Id of subscription</param>
        /// <returns>Subscription information</returns>
        public Task<Subscription> GetSubscriptionAsync(string subscriptionId)
        {
            CheckForValidBarClient();
            return _barClient.GetSubscriptionAsync(GetSubscriptionGuid(subscriptionId));
        }

        /// <summary>
        ///     Get repository merge policies
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="branch">Repository branch</param>
        /// <returns>List of merge policies</returns>
        public Task<IEnumerable<MergePolicy>> GetRepositoryMergePoliciesAsync(string repoUri, string branch)
        {
            CheckForValidBarClient();
            return _barClient.GetRepositoryMergePolicies(repoUri, branch);
        }

        /// <summary>
        /// Trigger a subscription by ID
        /// </summary>
        /// <param name="subscriptionId">ID of subscription to trigger</param>
        /// <returns>Subscription just triggered.</returns>
        public async Task<Subscription> TriggerSubscriptionAsync(string subscriptionId)
        {
            CheckForValidBarClient();
            return await _barClient.TriggerSubscriptionAsync(GetSubscriptionGuid(subscriptionId));
        }

        /// <summary>
        ///     Create a new subscription
        /// </summary>
        /// <param name="channelName">Name of source channel</param>
        /// <param name="sourceRepo">URL of source repository</param>
        /// <param name="targetRepo">URL of target repository where updates should be made</param>
        /// <param name="targetBranch">Name of target branch where updates should be made</param>
        /// <param name="updateFrequency">Frequency of updates, can be 'none', 'everyBuild' or 'everyDay'</param>
        /// <param name="mergePolicies">
        ///     Dictionary of merge policies. Each merge policy is a name of a policy with an associated blob
        ///     of metadata
        /// </param>
        /// <returns>Newly created subscription, if successful</returns>
        public Task<Subscription> CreateSubscriptionAsync(
            string channelName,
            string sourceRepo,
            string targetRepo,
            string targetBranch,
            string updateFrequency,
            List<MergePolicy> mergePolicies)
        {
            CheckForValidBarClient();
            return _barClient.CreateSubscriptionAsync(
                channelName,
                sourceRepo,
                targetRepo,
                targetBranch,
                updateFrequency,
                mergePolicies);
        }

        /// <summary>
        /// Convert subscription id to a Guid, throwing if not valid.
        /// </summary>
        /// <param name="subscriptionId">ID of subscription.</param>
        /// <returns>New guid</returns>
        private Guid GetSubscriptionGuid(string subscriptionId)
        {
            if (!Guid.TryParse(subscriptionId, out Guid subscriptionGuid))
            {
                throw new ArgumentException($"Subscription id '{subscriptionId}' is not a valid guid.");
            }
            return subscriptionGuid;
        }

        /// <summary>
        ///     Delete a subscription by id
        /// </summary>
        /// <param name="subscriptionId">Id of subscription to delete</param>
        /// <returns>Information on deleted subscriptio</returns>
        public async Task<Subscription> DeleteSubscriptionAsync(string subscriptionId)
        {
            CheckForValidBarClient();
            return await _barClient.DeleteSubscriptionAsync(GetSubscriptionGuid(subscriptionId));
        }

        /// <summary>
        ///     Update an existing subscription
        /// </summary>
        /// <param name="subscriptionId">Id of subscription to update</param>
        /// <param name="subscription">Subscription information</param>
        /// <returns>Updated subscription</returns>
        public Task<Subscription> UpdateSubscriptionAsync(string subscriptionId, SubscriptionUpdate subscription)
        {
            CheckForValidBarClient();
            return _barClient.UpdateSubscriptionAsync(GetSubscriptionGuid(subscriptionId), subscription);
        }

        public async Task CreateNewBranchAsync(string repoUri, string baseBranch, string newBranch)
        {
            await _gitClient.CreateBranchAsync(repoUri, newBranch, baseBranch);
        }

        public async Task DeleteBranchAsync(string repoUri, string branch)
        {
            await _gitClient.DeleteBranchAsync(repoUri, branch);
        }

        public async Task<IEnumerable<Check>> GetPullRequestChecksAsync(string pullRequestUrl)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Getting status checks for pull request '{pullRequestUrl}'...");

            IEnumerable<Check> checks = await _gitClient.GetPullRequestChecksAsync(pullRequestUrl);

            return checks;
        }

        public async Task<IEnumerable<int>> SearchPullRequestsAsync(
            string repoUri,
            string pullRequestBranch,
            PrStatus status,
            string keyword = null,
            string author = null)
        {
            CheckForValidGitClient();
            return await _gitClient.SearchPullRequestsAsync(repoUri, pullRequestBranch, status, keyword, author);
        }

        public Task CreateOrUpdatePullRequestStatusCommentAsync(string pullRequestUrl, string message)
        {
            CheckForValidGitClient();
            return _gitClient.CreateOrUpdatePullRequestCommentAsync(pullRequestUrl, message);
        }

        public async Task<PrStatus> GetPullRequestStatusAsync(string pullRequestUrl)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Getting the status of pull request '{pullRequestUrl}'...");

            PrStatus status = await _gitClient.GetPullRequestStatusAsync(pullRequestUrl);

            _logger.LogInformation($"Status of pull request '{pullRequestUrl}' is '{status}'");

            return status;
        }

        public Task UpdatePullRequestAsync(string pullRequestUri, PullRequest pullRequest)
        {
            return _gitClient.UpdatePullRequestAsync(pullRequestUri, pullRequest);
        }

        public async Task MergePullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Merging pull request '{pullRequestUrl}'...");

            await _gitClient.MergePullRequestAsync(pullRequestUrl, parameters ?? new MergePullRequestParameters());

            _logger.LogInformation($"Merging pull request '{pullRequestUrl}' succeeded!");
        }

        public async Task<List<DependencyDetail>> GetRequiredUpdatesAsync(
            string repoUri,
            string branch,
            string sourceCommit,
            IEnumerable<AssetData> assets)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Check if repo '{repoUri}' and branch '{branch}' needs updates...");

            var toUpdate = new List<DependencyDetail>();
            IEnumerable<DependencyDetail> dependencyDetails =
                await _fileManager.ParseVersionDetailsXmlAsync(repoUri, branch);
            Dictionary<string, DependencyDetail> dependencies = dependencyDetails.ToDictionary(d => d.Name);

            foreach (AssetData asset in assets)
            {
                if (!dependencies.TryGetValue(asset.Name, out DependencyDetail dependency))

                {
                    _logger.LogInformation($"No dependency found for updated asset '{asset.Name}'");
                    continue;
                }

                dependency.Version = asset.Version;
                dependency.Commit = sourceCommit;
                toUpdate.Add(dependency);
            }

            _logger.LogInformation(
                $"Getting dependencies which need to be updated in repo '{repoUri}' and branch '{branch}' succeeded!");

            return toUpdate;
        }

        public async Task CommitUpdatesAsync(
            string repoUri,
            string branch,
            List<DependencyDetail> itemsToUpdate,
            string message)
        {
            CheckForValidGitClient();
            GitFileContentContainer fileContainer =
                await _fileManager.UpdateDependencyFiles(itemsToUpdate, repoUri, branch);
            List<GitFile> filesToCommit = fileContainer.GetFilesToCommit();

            // If we are updating the arcade sdk we need to update the eng/common files as well
            DependencyDetail arcadeItem = itemsToUpdate.FirstOrDefault(
                i => string.Equals(i.Name, "Microsoft.DotNet.Arcade.Sdk", StringComparison.OrdinalIgnoreCase));

            if (arcadeItem != null && repoUri != arcadeItem.RepoUri)
            {
                // Files in arcade repository. All Arcade items have a GitHub repo URI by default so we need to change the
                // URI from we are getting the eng/common files. If in an AzDO context we change the URI to that of
                // dotnet-arcade in dnceng

                string engCommonRepoUri = arcadeItem.RepoUri;

                if (Uri.TryCreate(repoUri, UriKind.Absolute, out Uri parsedUri))
                {
                    if (parsedUri.Host == "dev.azure.com" || parsedUri.Host.EndsWith("visualstudio.com"))
                    {
                        engCommonRepoUri = "https://dev.azure.com/dnceng/internal/_git/dotnet-arcade";
                    }
                }
                    
                List<GitFile> engCommonFiles = await GetCommonScriptFilesAsync(engCommonRepoUri, arcadeItem.Commit);
                filesToCommit.AddRange(engCommonFiles);

                // Files in the target repo
                string latestCommit = await _gitClient.GetLastCommitShaAsync(repoUri, branch);
                List<GitFile> targetEngCommonFiles = await GetCommonScriptFilesAsync(repoUri, latestCommit);

                foreach (GitFile file in targetEngCommonFiles)
                {
                    if (!engCommonFiles.Where(f => f.FilePath == file.FilePath).Any())
                    {
                        file.Operation = GitFileOperation.Delete;
                        filesToCommit.Add(file);
                    }
                }
            }

            await _gitClient.CommitFilesAsync(filesToCommit, repoUri, branch, message);
        }

        public Task<PullRequest> GetPullRequestAsync(string pullRequestUri)
        {
            return _gitClient.GetPullRequestAsync(pullRequestUri);
        }

        public Task<string> CreatePullRequestAsync(string repoUri, PullRequest pullRequest)
        {
            return _gitClient.CreatePullRequestAsync(repoUri, pullRequest);
        }

        /// <summary>
        ///     Retrieve the list of channels from the build asset registry.
        /// </summary>
        /// <param name="classification">Optional classification to get</param>
        /// <returns></returns>
        public Task<IEnumerable<Channel>> GetChannelsAsync(string classification = null)
        {
            CheckForValidBarClient();
            return _barClient.GetChannelsAsync(classification);
        }

        /// <summary>
        ///     Retrieve a specific channel by name.
        /// </summary>
        /// <param name="channel">Channel name.</param>
        /// <returns>Channel or null if not found.</returns>
        public Task<Channel> GetChannelAsync(string channel)
        {
            CheckForValidBarClient();
            return _barClient.GetChannelAsync(channel);
        }

        /// <summary>
        ///     Retrieve the latest build of a repository on a specific channel.
        /// </summary>
        /// <param name="repoUri">URI of repository to obtain a build for.</param>
        /// <param name="channelId">Channel the build was applied to.</param>
        /// <returns>Latest build of <paramref name="repoUri"/> on channel <paramref name="channelId"/>,
        /// or null if there is no latest.</returns>
        /// <remarks>The build's assets are returned</remarks>
        public async Task<Build> GetLatestBuildAsync(string repoUri, int channelId)
        {
            CheckForValidBarClient();
            try
            {
                return await _barClient.GetLatestBuildAsync(repoUri: repoUri, channelId: channelId);
            }
            catch (ApiErrorException e) when (e.Response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        /// <summary>
        ///     Retrieve information about the specified build.
        /// </summary>
        /// <param name="buildId">Id of build.</param>
        /// <returns>Information about the specific build</returns>
        /// <remarks>The build's assets are returned</remarks>
        public Task<Build> GetBuildAsync(int buildId)
        {
            CheckForValidBarClient();
            return _barClient.GetBuildAsync(buildId);
        }

        /// <summary>
        ///     Get a list of builds for the given repo uri and commit.
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="commit">Commit</param>
        /// <returns></returns>
        public Task<IEnumerable<Build>> GetBuildsAsync(string repoUri, string commit)
        {
            CheckForValidBarClient();
            return _barClient.GetBuildsAsync(repoUri: repoUri,
                                             commit: commit);
        }

        /// <summary>
        ///     Get assets matching a particular set of properties. All are optional.
        /// </summary>
        /// <param name="name">Name of asset</param>
        /// <param name="version">Version of asset</param>
        /// <param name="buildId">ID of build producing the asset</param>
        /// <param name="nonShipping">Only non-shipping</param>
        /// <returns>List of assets.</returns>
        public Task<IEnumerable<Asset>> GetAssetsAsync(string name = null,
                                                       string version = null,
                                                       int? buildId = null,
                                                       bool? nonShipping = null)
        {
            CheckForValidBarClient();
            return _barClient.GetAssetsAsync(name: name,
                                      version: version,
                                      buildId: buildId,
                                      nonShipping: nonShipping);
        }

        /// <summary>
        ///     Get the list of dependencies in the specified repo and branch/commit
        /// </summary>
        /// <param name="repoUri">Repository to get dependencies from</param>
        /// <param name="branchOrCommit">Commit to get dependencies at</param>
        /// <param name="name">Optional name of specific dependency to get information on</param>
        /// <returns>Matching dependency information.</returns>
        public async Task<IEnumerable<DependencyDetail>> GetDependenciesAsync(string repoUri,
                                                                              string branchOrCommit,
                                                                              string name = null)
        {
            CheckForValidGitClient();
            return (await _fileManager.ParseVersionDetailsXmlAsync(repoUri, branchOrCommit)).Where(
                dependency => string.IsNullOrEmpty(name) || dependency.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        ///     Called prior to operations requiring the BAR.  Throws if a bar client isn't available.
        /// </summary>
        private void CheckForValidBarClient()
        {
            if (_barClient == null)
            {
                throw new ArgumentException("Must supply a build asset registry client");
            }
        }

        /// <summary>
        ///     Called prior to operations requiring the BAR.  Throws if a git client isn't available;
        /// </summary>
        private void CheckForValidGitClient()
        {
            if (_gitClient == null)
            {
                throw new ArgumentException("Must supply a valid GitHub/Azure DevOps PAT");
            }
        }

        public async Task<List<GitFile>> GetCommonScriptFilesAsync(string repoUri, string commit)
        {
            CheckForValidGitClient();
            _logger.LogInformation("Generating commits for script files");

            List<GitFile> files = await _gitClient.GetFilesAtCommitAsync(repoUri, commit, "eng/common");

            _logger.LogInformation("Generating commits for script files succeeded!");

            return files;
        }
    }
}
