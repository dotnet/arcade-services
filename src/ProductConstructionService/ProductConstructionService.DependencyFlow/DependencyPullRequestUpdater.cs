// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

using AssetData = Microsoft.DotNet.ProductConstructionService.Client.Models.AssetData;
using BuildDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;

namespace ProductConstructionService.DependencyFlow;

internal class DependencyPullRequestUpdater : PullRequestUpdater
{
    private readonly ISubscriptionConfiguration _subscriptionConfiguration;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IPullRequestBuilder _pullRequestBuilder;
    private readonly IRemoteFactory _remoteFactory;
    private readonly ISqlBarClient _sqlClient;
    private readonly IPullRequestStateManager _stateManager;
    private readonly ISubscriptionEventRecorder _subscriptionEventRecorder;
    private readonly ILogger<DependencyPullRequestUpdater> _logger;

    public DependencyPullRequestUpdater(
        ISubscriptionConfiguration subscriptionConfiguration,
        IPullRequestChecker pullRequestChecker,
        IPullRequestStateManager stateManager,
        IRemoteFactory remoteFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IPullRequestBuilder pullRequestBuilder,
        ISqlBarClient sqlClient,
        ILogger<DependencyPullRequestUpdater> logger,
        IPullRequestCommenter pullRequestCommenter,
        ISubscriptionEventRecorder subscriptionEventRecorder)
        : base(subscriptionConfiguration, pullRequestChecker, sqlClient, pullRequestCommenter, stateManager, logger)
    {
        _subscriptionConfiguration = subscriptionConfiguration;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _pullRequestBuilder = pullRequestBuilder;
        _remoteFactory = remoteFactory;
        _sqlClient = sqlClient;
        _stateManager = stateManager;
        _logger = logger;
        _subscriptionEventRecorder = subscriptionEventRecorder;
    }

    protected override async Task ProcessSubscriptionUpdateAsync(
        SubscriptionUpdateWorkItem update,
        InProgressPullRequest? pr,
        PullRequest? prInfo,
        BuildDTO build,
        bool forceUpdate)
    {
        if (pr != null && prInfo != null)
        {
            await UpdatePullRequestAsync(update, pr, prInfo, build);
            await _stateManager.UnsetUpdateReminderAsync(isCodeFlow: false);
            return;
        }

        // Create a new (regular) dependency update PR
        var prUrl = await CreatePullRequestAsync(update, build);
        if (prUrl == null)
        {
            _logger.LogInformation("No changes required for subscription {subscriptionId}, no pull request created", update.SubscriptionId);
        }
        else
        {
            _logger.LogInformation("Pull request '{url}' for subscription {subscriptionId} created", prUrl, update.SubscriptionId);
        }

        await _stateManager.UnsetUpdateReminderAsync(isCodeFlow: false);
    }

    private async Task UpdatePullRequestAsync(
        SubscriptionUpdateWorkItem update,
        InProgressPullRequest pr,
        PullRequest prInfo,
        BuildDTO build)
    {
        (var targetRepository, var targetBranch) = await _subscriptionConfiguration.GetTargetAsync();

        _logger.LogInformation("Updating pull request {url} branch {targetBranch} in {targetRepository}", pr.Url, targetBranch, targetRepository);

        IRemote darcRemote = await _remoteFactory.CreateRemoteAsync(targetRepository);

        TargetRepoDependencyUpdates repoDependencyUpdates =
            await GetRequiredUpdates(update, targetRepository, build, prInfo.HeadBranch, targetBranch);

        if (repoDependencyUpdates.DirectoryUpdates.Values.All(update =>
            update.CoherencyCheckSuccessful
            && update.NonCoherencyUpdates.Count == 0
            && (update.CoherencyUpdates == null
                || update.CoherencyUpdates.Count == 0)))
        {
            _logger.LogInformation("No updates found for pull request {url}", pr.Url);
            return;
        }

        pr.RequiredUpdates = MergeExistingWithIncomingUpdates(
            pr.RequiredUpdates,
            repoDependencyUpdates.DirectoryUpdates
                .SelectMany(kvp => kvp.Value.NonCoherencyUpdates
                    .Concat(kvp.Value.CoherencyUpdates ?? [])
                    .Select(update => (kvp.Key, update)))
                .Select(u => new DependencyUpdateSummary(u.update) { RelativeBasePath = u.Key })
                .ToList());

        if (pr.RequiredUpdates.Count < 1)
        {
            _logger.LogInformation("No new updates found for pull request {url}", pr.Url);
            return;
        }

        await _subscriptionEventRecorder.RegisterSubscriptionUpdateAction(SubscriptionUpdateAction.ApplyingUpdates, update.SubscriptionId);

        pr.CoherencyCheckSuccessful = repoDependencyUpdates.CoherencyCheckSuccessful;
        List<CoherencyErrorDetails> agregatedCoherencyErrors = repoDependencyUpdates.GetAgregatedCoherencyErrors();
        pr.CoherencyErrors = agregatedCoherencyErrors.Count > 0 ? agregatedCoherencyErrors : null;

        List<SubscriptionPullRequestUpdate> previousSubscriptions = [.. pr.ContainedSubscriptions];

        // Update the list of contained subscriptions with the new subscription update.
        // Replace all existing updates for the subscription id with the new update.
        // This avoids a potential issue where we may update the last applied build id
        // on the subscription to an older build id.
        pr.ContainedSubscriptions.RemoveAll(s => s.SubscriptionId == update.SubscriptionId);

        // Mark all previous dependency updates that are being updated as Updated. All new dependencies should not be
        // marked as update as they are new. Any dependency not being updated should not be marked as failed.
        // At this point, pr.ContainedSubscriptions only contains the subscriptions that were not updated,
        // so everything that is in the previous list but not in the current list were updated.
        await _subscriptionEventRecorder.AddDependencyFlowEventsAsync(
            previousSubscriptions.Except(pr.ContainedSubscriptions),
            DependencyFlowEventType.Updated,
            DependencyFlowEventReason.FailedUpdate,
            pr.MergePolicyResult,
            pr.Url);
        pr.ContainedSubscriptions.Add(new SubscriptionPullRequestUpdate
        {
            SubscriptionId = update.SubscriptionId,
            BuildId = update.BuildId,
            SourceRepo = update.SourceRepo,
            CommitSha = update.SourceSha
        });

        // Mark any new dependency updates as Created. Any subscriptions that are in pr.ContainedSubscriptions
        // but were not in the previous list of subscriptions are new
        await _subscriptionEventRecorder.AddDependencyFlowEventsAsync(
            pr.ContainedSubscriptions.Except(previousSubscriptions),
            DependencyFlowEventType.Created,
            DependencyFlowEventReason.New,
            MergePolicyCheckResult.PendingPolicies,
            pr.Url);

        var requiredDescriptionUpdates =
            await CalculateOriginalDependenciesAsync(darcRemote, targetRepository, targetBranch, repoDependencyUpdates);

        prInfo.Description = await _pullRequestBuilder.CalculatePRDescriptionAndCommitUpdatesAsync(
            requiredDescriptionUpdates,
            prInfo.Description,
            targetRepository,
            prInfo.HeadBranch);

        prInfo.Title = await _pullRequestBuilder.GeneratePRTitleAsync(pr.ContainedSubscriptions, targetBranch);

        await darcRemote.UpdatePullRequestAsync(pr.Url, prInfo);
        pr.LastUpdate = DateTime.UtcNow;
        pr.NextBuildsToProcess.Remove(update.SubscriptionId);
        await _stateManager.SetCheckReminderAsync(pr, prInfo, isCodeFlow: update.SubscriptionType == SubscriptionType.DependenciesAndSources);

        _logger.LogInformation("Pull request '{prUrl}' updated", pr.Url);
    }

    /// <summary>
    ///     Creates a pull request from the given updates.
    /// </summary>
    /// <returns>The pull request url when a pr was created; <see langref="null" /> if no PR is necessary</returns>
    private async Task<PullRequest?> CreatePullRequestAsync(SubscriptionUpdateWorkItem update, BuildDTO build)
    {
        (var targetRepository, var targetBranch) = await _subscriptionConfiguration.GetTargetAsync();
        bool isCodeFlow = update.SubscriptionType == SubscriptionType.DependenciesAndSources;

        IRemote darcRemote = await _remoteFactory.CreateRemoteAsync(targetRepository);
        TargetRepoDependencyUpdates repoDependencyUpdates;

        try
        {
            repoDependencyUpdates =
                await GetRequiredUpdates(update, targetRepository, build, prBranch: null, targetBranch: targetBranch);
        }
        catch (DependencyFileNotFoundException e)
        {
            // It can happen that the target branch does not exist or it just does not have eng/Version.Details.xml
            _logger.LogWarning("Failed to read target branch dependencies: {ErrorMessage}", e.Message);
            return null;
        }

        // if there coherency check was a success, we really don't need to do anything, so just return
        if (repoDependencyUpdates.DirectoryUpdates.Values.All(update =>
            update.CoherencyCheckSuccessful
            && update.NonCoherencyUpdates.Count == 0
            && (update.CoherencyUpdates == null
                || update.CoherencyUpdates.Count == 0)))
        {
            return null;
        }

        await _subscriptionEventRecorder.RegisterSubscriptionUpdateAction(SubscriptionUpdateAction.ApplyingUpdates, update.SubscriptionId);

        var newBranchName = GetNewBranchName(targetBranch);
        await darcRemote.CreateNewBranchAsync(targetRepository, targetBranch, newBranchName);

        // if the coherency check failed, and we don't have any non-coherency updates, then we want to open a PR with an error message
        if (!repoDependencyUpdates.CoherencyCheckSuccessful
            && repoDependencyUpdates.DirectoryUpdates.Values.All(update =>
                update.NonCoherencyUpdates.Count == 0))
        {
            var commitMessage = "Failed to perform coherency update for one or more dependencies.";
            await darcRemote.CommitUpdatesAsync(filesToCommit: [], targetRepository, newBranchName, commitMessage);
            var prDescription = $"Coherency update: {commitMessage} Please review the GitHub checks or run `darc update-dependencies --coherency-only` locally against {newBranchName} for more information.";
            PullRequest pr = await darcRemote.CreatePullRequestAsync(
                targetRepository,
                new PullRequest
                {
                    Title = $"[{targetBranch}] Update dependencies to ensure coherency",
                    Description = prDescription,
                    BaseBranch = targetBranch,
                    HeadBranch = newBranchName,
                });

            List<CoherencyErrorDetails> aggregatedCoherencyErrors = repoDependencyUpdates.GetAgregatedCoherencyErrors();

            InProgressPullRequest inProgressPr = new()
            {
                UpdaterId = Id.ToString(),
                Url = pr.Url,
                HeadBranch = newBranchName,
                HeadBranchSha = pr.HeadBranchSha,
                SourceSha = update.SourceSha,
                ContainedSubscriptions = [],
                RequiredUpdates = [],
                CoherencyCheckSuccessful = false,
                CoherencyErrors = aggregatedCoherencyErrors.Count > 0 ? aggregatedCoherencyErrors : null,
                CodeFlowDirection = CodeFlowDirection.None,
                CreationDate = DateTime.UtcNow,
            };

            await _stateManager.SetCheckReminderAsync(inProgressPr, pr, isCodeFlow);
            return pr;
        }

        try
        {
            var description = await _pullRequestBuilder.CalculatePRDescriptionAndCommitUpdatesAsync(
                repoDependencyUpdates,
                currentDescription: null,
                targetRepository,
                newBranchName);

            SubscriptionPullRequestUpdate subscriptionUpdate = new()
            {
                SubscriptionId = repoDependencyUpdates.SubscriptionUpdate.SubscriptionId,
                BuildId = repoDependencyUpdates.SubscriptionUpdate.BuildId,
                SourceRepo = repoDependencyUpdates.SubscriptionUpdate.SourceRepo,
                CommitSha = repoDependencyUpdates.SubscriptionUpdate.SourceSha
            };

            PullRequest pr = await darcRemote.CreatePullRequestAsync(
                targetRepository,
                new PullRequest
                {
                    Title = await _pullRequestBuilder.GeneratePRTitleAsync([subscriptionUpdate], targetBranch),
                    Description = description,
                    BaseBranch = targetBranch,
                    HeadBranch = newBranchName,
                });

            List<CoherencyErrorDetails> aggregatedCoherencyErrors = repoDependencyUpdates.GetAgregatedCoherencyErrors();

            var inProgressPr = new InProgressPullRequest
            {
                UpdaterId = Id.ToString(),
                Url = pr.Url,
                HeadBranch = newBranchName,
                HeadBranchSha = pr.HeadBranchSha,
                SourceSha = update.SourceSha,

                ContainedSubscriptions = [subscriptionUpdate],

                RequiredUpdates = repoDependencyUpdates.DirectoryUpdates
                    .SelectMany(kvp => kvp.Value.NonCoherencyUpdates
                        .Concat(kvp.Value.CoherencyUpdates ?? [])
                        .Select(update => (kvp.Key, update)))
                    .Select(u => new DependencyUpdateSummary(u.update) { RelativeBasePath = u.Key })
                    .ToList(),

                CoherencyCheckSuccessful = repoDependencyUpdates.CoherencyCheckSuccessful,
                CoherencyErrors = aggregatedCoherencyErrors.Count > 0 ? aggregatedCoherencyErrors : null,
                CodeFlowDirection = CodeFlowDirection.None,
                CreationDate = DateTime.UtcNow,
            };

            if (!string.IsNullOrEmpty(pr?.Url))
            {
                await _subscriptionEventRecorder.AddDependencyFlowEventsAsync(
                    inProgressPr.ContainedSubscriptions,
                    DependencyFlowEventType.Created,
                    DependencyFlowEventReason.New,
                    MergePolicyCheckResult.PendingPolicies,
                    pr.Url);

                await _stateManager.SetCheckReminderAsync(inProgressPr, pr, isCodeFlow);
                return pr;
            }

            // If we did not create a PR, then mark the dependency flow as completed as nothing to do.
            await _subscriptionEventRecorder.AddDependencyFlowEventsAsync(
                inProgressPr.ContainedSubscriptions,
                DependencyFlowEventType.Completed,
                DependencyFlowEventReason.NothingToDo,
                MergePolicyCheckResult.PendingPolicies,
                null);

            // Something wrong happened when trying to create the PR but didn't throw an exception (probably there was no diff).
            // We need to delete the branch also in this case.
            await darcRemote.DeleteBranchAsync(targetRepository, newBranchName);
            return null;
        }
        catch
        {
            await darcRemote.DeleteBranchAsync(targetRepository, newBranchName);
            throw;
        }
    }

    /// <summary>
    /// Given a set of input updates from builds, determine what updates
    /// are required in the target repository.
    /// </summary>
    /// <param name="update">Update</param>
    /// <param name="targetRepository">Target repository to calculate updates for</param>
    /// <param name="prBranch">PR head branch</param>
    /// <param name="targetBranch">Target branch</param>
    /// <returns>List of updates and dependencies that need updates.</returns>
    /// <remarks>
    ///     This is done in two passes.  The first pass runs through and determines the non-coherency
    ///     updates required based on the input updates.  The second pass uses the repo state + the
    ///     updates from the first pass to determine what else needs to change based on the coherency metadata.
    /// </remarks>
    private async Task<TargetRepoDependencyUpdates> GetRequiredUpdates(
        SubscriptionUpdateWorkItem update,
        string targetRepository,
        BuildDTO build,
        string? prBranch,
        string targetBranch)
    {
        _logger.LogInformation("Getting Required Updates for {branch} of {targetRepository}", targetBranch, targetRepository);
        // Get a remote factory for the target repo
        IRemote darc = await _remoteFactory.CreateRemoteAsync(targetRepository);

        Dictionary<UnixPath, TargetRepoDirectoryDependencyUpdates> repoDependencyUpdates = [];

        // Get subscription to access excluded assets
        var subscription = await _sqlClient.GetSubscriptionAsync(update.SubscriptionId)
            ?? throw new InvalidOperationException($"Subscription with ID {update.SubscriptionId} not found in the DB.");

        var excludedAssetsMatcher = new NameBasedAssetMatcher(subscription.ExcludedAssets);

        List<UnixPath> targetDirectories;
        if (string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            targetDirectories = [UnixPath.Empty];
        }
        else
        {
            targetDirectories = [];
            var directories = subscription.TargetDirectory.Split(',');

            foreach (var d in directories)
            {
                if (d.EndsWith('*'))
                {
                    // Trim trailing '/' and '*' characters and get directory names
                    string basePath = d.TrimEnd('/', '*');
                    var directoryNames = await darc.GetGitTreeNames(basePath, targetRepository, targetBranch);
                    targetDirectories.AddRange(directoryNames.Select(dirName => new UnixPath(basePath) / dirName));
                }
                else
                {
                    targetDirectories.Add(new UnixPath(d));
                }
            }
        }

        foreach (var targetDirectory in targetDirectories)
        {
            // Existing details
            var existingDependencies = (await darc.GetDependenciesAsync(targetRepository, prBranch ?? targetBranch, relativeBasePath: targetDirectory)).ToList();

            // Filter out excluded assets from the build assets
            bool isRoot = targetDirectory == UnixPath.Empty;
            List<AssetData> assetData = build.Assets
                .Where(a => !excludedAssetsMatcher.IsExcluded(isRoot ? a.Name : $"{targetDirectory}/{a.Name}"))
                .Select(a => new AssetData(false)
                {
                    Name = a.Name,
                    Version = a.Version
                })
                .ToList();

            // Retrieve the source of the assets
            List<DependencyUpdate> dependenciesToUpdate = _coherencyUpdateResolver.GetRequiredNonCoherencyUpdates(
                update.SourceRepo,
                update.SourceSha,
                assetData,
                existingDependencies);

            if (dependenciesToUpdate.Count < 1)
            {
                // No dependencies need to be updated.
                await _subscriptionEventRecorder.UpdateSubscriptionsForMergedPRAsync(
                    new List<SubscriptionPullRequestUpdate>
                    {
                    new()
                    {
                        SubscriptionId = update.SubscriptionId,
                        BuildId = update.BuildId,
                        SourceRepo = update.SourceRepo,
                        CommitSha = update.SourceSha
                    }
                    });
                repoDependencyUpdates[targetDirectory] = new TargetRepoDirectoryDependencyUpdates
                {
                    NonCoherencyUpdates = [],
                };
            }
            else
            {
                // Update the existing details list
                foreach (DependencyUpdate dependencyUpdate in dependenciesToUpdate)
                {
                    existingDependencies.Remove(dependencyUpdate.From);
                    existingDependencies.Add(dependencyUpdate.To);
                }

                repoDependencyUpdates[targetDirectory] = new TargetRepoDirectoryDependencyUpdates
                {
                    NonCoherencyUpdates = dependenciesToUpdate,
                };
            }

            // Once we have applied all of non coherent updates, then we need to run a coherency check on the dependencies.
            List<DependencyUpdate> coherencyUpdates = [];
            try
            {
                _logger.LogInformation("Running a coherency check on the existing dependencies for branch {branch} of repo {repository}",
                    targetBranch,
                    targetRepository);
                coherencyUpdates = await _coherencyUpdateResolver.GetRequiredCoherencyUpdatesAsync(existingDependencies);
            }
            catch (DarcCoherencyException e)
            {
                _logger.LogInformation("Failed attempting strict coherency update on branch '{strictCoherencyFailedBranch}' of repo '{strictCoherencyFailedRepo}'",
                     targetBranch, targetRepository);
                repoDependencyUpdates[targetDirectory].CoherencyCheckSuccessful = false;
                repoDependencyUpdates[targetDirectory].CoherencyErrors = e.Errors.Select(e => new CoherencyErrorDetails
                {
                    Error = e.Error,
                    PotentialSolutions = e.PotentialSolutions
                }).ToList();
            }

            if (coherencyUpdates.Count != 0)
            {
                repoDependencyUpdates[targetDirectory].CoherencyUpdates = [.. coherencyUpdates];
            }

            _logger.LogInformation("Finished getting Required Updates for {branch} of {targetRepository} on relative path {relativePath}",
                targetBranch,
                targetRepository,
                targetDirectory);
        }

        return new TargetRepoDependencyUpdates
        {
            DirectoryUpdates = repoDependencyUpdates,
            SubscriptionUpdate = update
        };
    }

    /// <summary>
    ///     Given a set of updates, replace the `from` version of every dependency update with the corresponding version
    ///     from the target branch 
    /// </summary>
    /// <param name="darcRemote">Darc client used to fetch target branch dependencies.</param>
    /// <param name="targetRepository">Target repository to fetch the dependencies from.</param>
    /// <param name="targetBranch">Target branch to fetch the dependencies from.</param>
    /// <param name="targetRepositoryUpdates">Incoming updates to the repository</param>
    /// <returns>
    ///     Subscription update and the corresponding list of altered dependencies
    /// </returns>
    /// <remarks>
    ///     This method is intended for use in situations where we want to keep the information about the original dependency
    ///     version, such as when updating PR descriptions.
    /// </remarks>
    private static async Task<TargetRepoDependencyUpdates> CalculateOriginalDependenciesAsync(
        IRemote darcRemote,
        string targetRepository,
        string targetBranch,
        TargetRepoDependencyUpdates targetRepositoryUpdates)
    {
        Dictionary<UnixPath, TargetRepoDirectoryDependencyUpdates> alteredUpdates = [];
        foreach (var (targetDirectory, targetDictionaryRepositoryUpdates) in targetRepositoryUpdates.DirectoryUpdates)
        {
            List<DependencyDetail> targetBranchDeps = [.. await darcRemote.GetDependenciesAsync(targetRepository, targetBranch, relativeBasePath: targetDirectory)];

            var updatedNonCoherencyDeps = targetDictionaryRepositoryUpdates.NonCoherencyUpdates
                .Select(dependency => new DependencyUpdate()
                {
                    From = targetBranchDeps
                        .FirstOrDefault(replace => dependency.From.Name == replace.Name, dependency.From),
                    To = dependency.To,
                })
                .ToList();
            var updatedCoherencyDeps = targetDictionaryRepositoryUpdates.CoherencyUpdates?
                .Select(dependency => new DependencyUpdate()
                {
                    From = targetBranchDeps
                        .FirstOrDefault(replace => dependency.From.Name == replace.Name, dependency.From),
                    To = dependency.To,
                })
                .ToList() ?? [];
            alteredUpdates[targetDirectory] = new TargetRepoDirectoryDependencyUpdates
            {
                NonCoherencyUpdates = updatedNonCoherencyDeps,
                CoherencyUpdates = updatedCoherencyDeps,
                CoherencyCheckSuccessful = targetDictionaryRepositoryUpdates.CoherencyCheckSuccessful,
                CoherencyErrors = targetDictionaryRepositoryUpdates.CoherencyErrors
            };
        }

        return new TargetRepoDependencyUpdates
        {
            DirectoryUpdates = alteredUpdates,
            SubscriptionUpdate = targetRepositoryUpdates.SubscriptionUpdate
        };
    }
}
