// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.AzureDevOps;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.GitHub;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Git.IssueManager;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;

namespace ReleasePipelineRunner
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    public sealed class ReleasePipelineRunner : IServiceImplementation, IReleasePipelineRunner
    {
        public ReleasePipelineRunner(
            IReliableStateManager stateManager,
            ILogger<ReleasePipelineRunner> logger,
            BuildAssetRegistryContext context,
            IDependencyUpdater dependencyUpdater)
        {
            StateManager = stateManager;
            Logger = logger;
            Context = context;
            DependencyUpdater = dependencyUpdater;
        }

        public IReliableStateManager StateManager { get; }
        public ILogger<ReleasePipelineRunner> Logger { get; }
        public BuildAssetRegistryContext Context { get; }
        public IDependencyUpdater DependencyUpdater { get; }

        private const string RunningPipelineDictionaryName = "runningPipelines";
        private static HashSet<string> InProgressStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "notstarted", "scheduled", "queued", "inprogress" };

        public async Task StartAssociatedReleasePipelinesAsync(int buildId, int channelId)
        {
            IReliableConcurrentQueue<ReleasePipelineRunnerItem> queue =
                await StateManager.GetOrAddAsync<IReliableConcurrentQueue<ReleasePipelineRunnerItem>>("queue");
            using (ITransaction tx = StateManager.CreateTransaction())
            {
                await queue.EnqueueAsync(
                    tx,
                    new ReleasePipelineRunnerItem
                    {
                        BuildId = buildId,
                        ChannelId = channelId
                    });
                await tx.CommitAsync();
            }
        }

        public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            IReliableConcurrentQueue<ReleasePipelineRunnerItem> queue =
                await StateManager.GetOrAddAsync<IReliableConcurrentQueue<ReleasePipelineRunnerItem>>("queue");

            try
            {
                using (ITransaction tx = StateManager.CreateTransaction())
                {
                    ConditionalValue<ReleasePipelineRunnerItem> maybeItem = await queue.TryDequeueAsync(
                        tx,
                        cancellationToken);
                    if (maybeItem.HasValue)
                    {
                        ReleasePipelineRunnerItem item = maybeItem.Value;
                        using (Logger.BeginScope(
                            $"Triggering release pipelines associated with channel {item.ChannelId} for build {item.BuildId}.",
                            item.BuildId,
                            item.ChannelId))
                        {
                            await RunAssociatedReleasePipelinesAsync(item.BuildId, item.ChannelId, cancellationToken);
                        }
                    }

                    await tx.CommitAsync();
                }
            }
            catch (TaskCanceledException tcex) when (tcex.CancellationToken == cancellationToken)
            {
                return TimeSpan.MaxValue;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Processing queue messages");
            }

            return TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// Start release pipeline associated with a channel.
        /// </summary>
        /// <param name="buildId">Maestro build id.</param>
        /// <param name="channelId">Maestro channel id.</param>
        /// <returns></returns>
        public async Task RunAssociatedReleasePipelinesAsync(int buildId, int channelId, CancellationToken cancellationToken)
        {
            Logger.LogInformation($"Starting release pipeline for {buildId} in {channelId}");
            Build build = await Context.Builds
                .Where(b => b.Id == buildId).FirstOrDefaultAsync();

            if (build == null)
            {
                Logger.LogError($"Could not find the specified BAR Build {buildId} to run a release pipeline.");
                return;
            }

            // If something uses the old API version we won't have this information available.
            // This will also be the case if something adds an existing build (created using
            // the old API version) to a channel
            if (build.AzureDevOpsBuildId == null)
            {
                Logger.LogInformation($"barBuildInfo.AzureDevOpsBuildId is null for BAR Build.Id {build.Id}.");
                return;
            }

            Channel channel = await Context.Channels
                .Where(ch => ch.Id == channelId)
                .Include(ch => ch.ChannelReleasePipelines)
                .ThenInclude(crp => crp.ReleasePipeline)
                .FirstOrDefaultAsync();

            if (channel == null)
            {
                Logger.LogInformation($"Could not find the specified channel {channelId} to run a release pipeline on.");
                return;
            }

            if (channel.ChannelReleasePipelines?.Any() != true)
            {
                Logger.LogInformation($"Channel {channel.Id}, which build with BAR ID {build.Id} is attached to, doesn't have an associated publishing pipeline.");
                return;
            }

            AzureDevOpsClient azdoClient = await GetAzureDevOpsClientForAccount(build.AzureDevOpsAccount);

            var azdoBuild = await azdoClient.GetBuildAsync(
                build.AzureDevOpsAccount,
                build.AzureDevOpsProject,
                build.AzureDevOpsBuildId.Value);

            var runningPipelines =
                await StateManager.GetOrAddAsync<IReliableDictionary<int, IList<ReleasePipelineStatusItem>>>(RunningPipelineDictionaryName);
            List<ReleasePipelineStatusItem> releaseList = new List<ReleasePipelineStatusItem>();

            Logger.LogInformation($"Found {channel.ChannelReleasePipelines.Count} pipeline(s) for channel {channelId}");

            foreach (ChannelReleasePipeline pipeline in channel.ChannelReleasePipelines)
            {
                try
                {
                    string organization = pipeline.ReleasePipeline.Organization;
                    string project = pipeline.ReleasePipeline.Project;
                    int pipelineId = pipeline.ReleasePipeline.PipelineIdentifier;

                    Logger.LogInformation($"Going to create a release using pipeline {organization}/{project}/{pipelineId}");

                    AzureDevOpsReleaseDefinition pipeDef = await azdoClient.GetReleaseDefinitionAsync(organization, project, pipelineId);
                    pipeDef = await azdoClient.RemoveAllArtifactSourcesAsync(organization, project, pipeDef);

                    pipeDef = await azdoClient.AddArtifactSourceAsync(organization, project, pipeDef, azdoBuild);

                    int releaseId = await azdoClient.StartNewReleaseAsync(organization, project, pipeDef, build.Id);

                    var item = new ReleasePipelineStatusItem(releaseId, channelId, organization, project);
                    releaseList.Add(item);

                    Logger.LogInformation($"Created release {releaseId} using pipeline {organization}/{project}/{pipelineId}");
                }
                catch (Exception e)
                {
                    Logger.LogError($"Some problem happened while starting publishing pipeline " +
                        $"{pipeline.ReleasePipeline.PipelineIdentifier} for build " +
                        $"{build.AzureDevOpsBuildId}: {e.Message}", e);
                    throw;
                }
            }

            if (releaseList.Count > 0)
            {
                using (ITransaction tx = StateManager.CreateTransaction())
                {
                    var runningPipelinesForBuild = await runningPipelines.TryGetValueAsync(tx, buildId);
                    if (runningPipelinesForBuild.HasValue)
                    {
                        // Some channel already triggered release pipelines for this build. Need to update with the releases for the new channel.
                        releaseList.AddRange(runningPipelinesForBuild.Value);
                        await runningPipelines.TryUpdateAsync(tx, buildId, releaseList, runningPipelinesForBuild.Value);
                    }
                    else
                    {
                        await runningPipelines.AddAsync(tx, buildId, releaseList);
                    }
                    await tx.CommitAsync();
                }
            }
        }

        /// <summary>
        /// Check for finished releases and add the corresponding build channel association to the DB.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        // Check every 5 minutes for any finished release Pipelines
        [CronSchedule("0 0/5 * 1/1 * ? *", TimeZones.UTC)]
        public async Task ProcessFinishedReleasesAsync(CancellationToken cancellationToken)
        {
            Logger.LogInformation($"Starting ProcessFinishedReleasesAsync.");

            var runningPipelines =
                await StateManager.GetOrAddAsync<IReliableDictionary<int, IList<ReleasePipelineStatusItem>>>(RunningPipelineDictionaryName);

            HashSet<BuildChannel> buildChannelsToAdd = new HashSet<BuildChannel>(new BuildChannelComparer());
            using (ITransaction tx = StateManager.CreateTransaction())
            {
                var runningPipelinesEnumerable = await runningPipelines.CreateEnumerableAsync(tx, EnumerationMode.Unordered);
                using (var asyncEnumerator = runningPipelinesEnumerable.GetAsyncEnumerator())
                {
                    while (await asyncEnumerator.MoveNextAsync(cancellationToken))
                    {
                        int buildId = asyncEnumerator.Current.Key;
                        IList<ReleasePipelineStatusItem> releaseStatuses = asyncEnumerator.Current.Value;
                        int channelId = releaseStatuses.First().ChannelId;
                        List<ReleasePipelineStatusItem> unfinishedReleases = new List<ReleasePipelineStatusItem>();
                        foreach (ReleasePipelineStatusItem releaseStatus in releaseStatuses)
                        {
                            try
                            {
                                int releaseId = releaseStatus.ReleaseId;
                                AzureDevOpsClient azdoClient = await GetAzureDevOpsClientForAccount(releaseStatus.PipelineOrganization);
                                AzureDevOpsRelease release =
                                    await azdoClient.GetReleaseAsync(releaseStatus.PipelineOrganization, releaseStatus.PipelineProject, releaseStatus.ReleaseId);

                                if (HasInProgressEnvironments(release))
                                {
                                    unfinishedReleases.Add(releaseStatus);
                                    Logger.LogInformation($"Release {releaseId} from build {buildId} and channel {channelId} is still in progress.");
                                }
                                else
                                {
                                    Logger.LogInformation($"Release {releaseId}, channel {channelId} finished executing");

                                    if (release.Environments.Any(r => r.Status != AzureDevOpsReleaseStatus.Succeeded))
                                    {
                                        await CreateGitHubIssueAsync(buildId, releaseId, release.Name);
                                        await StateManager.RemoveAsync(release.Name);
                                    }
                                }
                            }
                            catch (TaskCanceledException tcex) when (tcex.CancellationToken == cancellationToken)
                            {
                                // ignore
                            }
                            catch (Exception ex)
                            {
                                // Something failed while fetching the release information so the potential created issue wouldn't have relevant information to
                                // be notified so we just log the exception to AppInsights with not filed issue.
                                Logger.LogError(ex, $"Processing release {releaseStatus.ReleaseId} failed. Check the exception for details.");
                            }
                        }

                        if (unfinishedReleases.Count > 0)
                        {
                            await runningPipelines.TryUpdateAsync(tx, buildId, unfinishedReleases, releaseStatuses);
                        }
                        else
                        {
                            Logger.LogInformation($"All releases for build {buildId} for channel {channelId} finished. Creating BuildChannel.");

                            buildChannelsToAdd.Add(new BuildChannel
                            {
                                BuildId = buildId,
                                ChannelId = channelId
                            });
                            await runningPipelines.TryRemoveAsync(tx, buildId);
                        }
                    }
                }

                if (buildChannelsToAdd.Count > 0)
                {
                    List<BuildChannel> addedBuildChannels = await AddFinishedBuildChannelsIfNotPresent(buildChannelsToAdd);
                    await TriggerDependencyUpdates(addedBuildChannels);
                }
                await tx.CommitAsync();
            }
        }

        private async Task TriggerDependencyUpdates(List<BuildChannel> addedBuildChannels)
        {
            foreach (BuildChannel buildChannel in addedBuildChannels)
            {
                Logger.LogInformation($"Calling DependencyUpdater to process dependency updates for build {buildChannel.BuildId} and channel {buildChannel.ChannelId}.");
                await DependencyUpdater.StartUpdateDependenciesAsync(buildChannel.BuildId, buildChannel.ChannelId);
            }
        }

        private static bool HasInProgressEnvironments(AzureDevOpsRelease release)
        {
            return release.Environments.Any(x => InProgressStatuses.Contains(x.Status));
        }

        private async Task<AzureDevOpsClient> GetAzureDevOpsClientForAccount(string account)
        {
            IAzureDevOpsTokenProvider azdoTokenProvider = Context.GetService<IAzureDevOpsTokenProvider>();
            string accessToken = await azdoTokenProvider.GetTokenForAccount(account);
            return new AzureDevOpsClient(accessToken, Logger, null);
        }

        private async Task<List<BuildChannel>> AddFinishedBuildChannelsIfNotPresent(HashSet<BuildChannel> buildChannelsToAdd)
        {
            var missingBuildChannels = buildChannelsToAdd.Where(x => !Context.BuildChannels.Any(y => y.ChannelId == x.ChannelId && y.BuildId == x.BuildId)).ToList();
            Context.BuildChannels.AddRange(missingBuildChannels);
            await Context.SaveChangesAsync();
            return missingBuildChannels;
        }

        private async Task CreateGitHubIssueAsync(int buildId, int releaseId, string releaseName)
        {
            Build build = Context.Builds.Where(b => b.Id == buildId).First();
            string whereToCreateIssue = "https://github.com/dotnet/arcade";
            string fyiHandles = "@JohnTortugo, @jcagme";
            string gitHubToken = null, azureDevOpsToken = null;
            string repo = build.GitHubRepository ?? build.AzureDevOpsRepository;

            try
            {
                if (!string.IsNullOrEmpty(build.GitHubRepository))
                {
                    IGitHubTokenProvider gitHubTokenProvider = Context.GetService<IGitHubTokenProvider>();
                    long installationId = await Context.GetInstallationId(build.GitHubRepository);
                    gitHubToken = await gitHubTokenProvider.GetTokenForInstallation(installationId);
                }

                if (!string.IsNullOrEmpty(build.AzureDevOpsRepository))
                {
                    IAzureDevOpsTokenProvider azdoTokenProvider = Context.GetService<IAzureDevOpsTokenProvider>();
                    azureDevOpsToken = await azdoTokenProvider.GetTokenForAccount(build.AzureDevOpsAccount);
                }

                IssueManager issueManager = new IssueManager(gitHubToken, azureDevOpsToken);
                string author = await issueManager.GetCommitAuthorAsync(repo, build.Commit);
                string title = $"Release '{releaseName}' with id {releaseId} failed";
                string description = $"Something failed while trying to publish artifacts for build [{buildId}]({build.Id})." +
                    $"{Environment.NewLine} {Environment.NewLine}" +
                    $"Please click [here](https://dnceng.visualstudio.com/internal/_releaseProgress?_a=release-pipeline-progress&releaseId={releaseId}) to check the error logs." +
                    $"{Environment.NewLine} {Environment.NewLine}" +
                    $"Last commit by: {author}" +
                    $" {Environment.NewLine} {Environment.NewLine}" +
                    $"/fyi: {fyiHandles}";
                int issueId = await issueManager.CreateNewIssueAsync(whereToCreateIssue, title, description);

                Logger.LogInformation($"Issue {issueId} was created in '{whereToCreateIssue}'");
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, $"Something failed while attempting to create an issue based on repo '{repo}' and commit {build.Commit}");
            }
        }
    }
}
