// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Maestro.AzureDevOps;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Newtonsoft.Json;

namespace ReleasePipelineRunner
{
    [DataContract]
    public sealed class ReleasePipelineRunnerItem
    {
        [DataMember]
        public int BuildId { get; set; }

        [DataMember]
        public int ChannelId { get; set; }
    }

    [DataContract]
    public sealed class ReleasePipelineStatusItem
    {
        [DataMember]
        public int ReleaseId { get; set; }

        [DataMember]
        public int ChannelId { get; set; }

        [DataMember]
        public string PipelineOrganization { get; set; }

        [DataMember]
        public string PipelineProject { get; set; }

        public ReleasePipelineStatusItem()
        {

        }
        public ReleasePipelineStatusItem(int releaseId, int channelId, string pipelineOrganization, string pipelineProject)
        {
            ReleaseId = releaseId;
            ChannelId = channelId;
            PipelineOrganization = pipelineOrganization;
            PipelineProject = pipelineProject;
        }
    }

    sealed class BuildChannelComparer : IEqualityComparer<BuildChannel>
    {
        public bool Equals(BuildChannel x, BuildChannel y)
        {
            return x.BuildId == y.BuildId && x.ChannelId == y.ChannelId;
        }

        public int GetHashCode(BuildChannel obj)
        {
            return obj.ChannelId * 31 + obj.BuildId;
        }
    }

    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    public sealed class ReleasePipelineRunner : IServiceImplementation, IReleasePipelineRunner
    {
        public ReleasePipelineRunner(
            IReliableStateManager stateManager,
            ILogger<ReleasePipelineRunner> logger,
            BuildAssetRegistryContext context)
        {
            StateManager = stateManager;
            Logger = logger;
            Context = context;
        }

        public IReliableStateManager StateManager { get; }
        public ILogger<ReleasePipelineRunner> Logger { get; }
        public BuildAssetRegistryContext Context { get; }

        private const string RunningPipelineDictionaryName = "runningPipelines";
        private static HashSet<string> InProgressStatuses = new HashSet<string>() { "notstarted", "scheduled", "queued", "inprogress" };

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

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            IReliableConcurrentQueue<ReleasePipelineRunnerItem> queue =
                await StateManager.GetOrAddAsync<IReliableConcurrentQueue<ReleasePipelineRunnerItem>>("queue");
            while (!cancellationToken.IsCancellationRequested)
            {
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
                                "Triggering release pipelines associated with channel {channelId} for build {buildId}.",
                                item.BuildId,
                                item.ChannelId))
                            {
                                await RunAssociatedReleasePipelinesAsync(item.BuildId, item.ChannelId, cancellationToken);
                            }
                        }

                        await tx.CommitAsync();
                    }

                    await Task.Delay(1000, cancellationToken);
                }
                catch (TaskCanceledException tcex) when (tcex.CancellationToken == cancellationToken)
                {
                    // ignore
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Processing queue messages");
                }
            }
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
            Build build = Context.Builds
                .Where(b => b.Id == buildId).First();

            Channel channel = Context.Channels
                .Where(ch => ch.Id == channelId)
                .Include(ch => ch.ChannelReleasePipelines)
                .ThenInclude(crp => crp.ReleasePipeline)
                .First();

            // If something use the old API version we won't have this information available.
            // This will also be the case if something adds an existing build (created using
            // the old API version) to a channel
            if (build.AzureDevOpsBuildId == null)
            {
                Logger.LogInformation($"barBuildInfo.AzureDevOpsBuildId is null for BAR Build.Id {build.Id}.");
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

            foreach (ChannelReleasePipeline pipeline in channel.ChannelReleasePipelines)
            {
                try
                {
                    string organization = pipeline.ReleasePipeline.Organization;
                    string project = pipeline.ReleasePipeline.Project;
                    int pipelineId = pipeline.ReleasePipeline.PipelineIdentifier;

                    AzureDevOpsReleaseDefinition pipeDef = await azdoClient.GetReleaseDefinitionAsync(organization, project, pipelineId);
                    pipeDef = await azdoClient.RemoveAllArtifactSourcesAsync(organization, project, pipeDef);

                    pipeDef = await azdoClient.AddArtifactSourceAsync(organization, project, pipeDef, azdoBuild);

                    int releaseId = await azdoClient.StartNewReleaseAsync(organization, project, pipeDef, build.Id);

                    var item = new ReleasePipelineStatusItem(releaseId, channelId, organization, project);
                    releaseList.Add(item);

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
                        await runningPipelines.AddOrUpdateAsync(tx, buildId, releaseList, (key, oldValue) => releaseList);
                    }
                    else
                    {
                        await runningPipelines.AddAsync(tx, buildId, releaseList);
                    }
                    await tx.CommitAsync();
                }
            }
            await ProcessFinishedReleasesAsync(cancellationToken);
        }

        /// <summary>
        /// Check for finished releases and add the corresponding build channel association to the DB.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        // Check every 10 minutes for any finished release Pipelines
        [CronSchedule("0 0/10 * 1/1 * ? *", TimeZones.UTC)]
        public async Task ProcessFinishedReleasesAsync(CancellationToken cancellationToken)
        {
            var runningPipelines =
                        await StateManager.GetOrAddAsync<IReliableDictionary<int, IList<ReleasePipelineStatusItem>>>(RunningPipelineDictionaryName);
            try
            {
                HashSet<BuildChannel> buildChannelsToAdd = new HashSet<BuildChannel>(new BuildChannelComparer());
                using (ITransaction tx = StateManager.CreateTransaction())
                {
                    var runningPipelinesEnumerable = await runningPipelines.CreateEnumerableAsync(tx, EnumerationMode.Unordered);
                    var asyncEnumerator = runningPipelinesEnumerable.GetAsyncEnumerator();
                    while (await asyncEnumerator.MoveNextAsync(cancellationToken))
                    {
                        int buildId = asyncEnumerator.Current.Key;
                        ConditionalValue<IList<ReleasePipelineStatusItem>> maybeReleaseStatuses = await runningPipelines.TryGetValueAsync(tx, buildId);
                        if (maybeReleaseStatuses.HasValue)
                        {
                            bool allFinished = true;
                            IList<ReleasePipelineStatusItem> releaseStatuses = maybeReleaseStatuses.Value;

                            foreach (ReleasePipelineStatusItem releaseStatus in releaseStatuses)
                            {
                                int releaseId = releaseStatus.ReleaseId;
                                AzureDevOpsClient azdoClient = await GetAzureDevOpsClientForAccount(releaseStatus.PipelineOrganization);
                                AzureDevOpsRelease release = await azdoClient.GetReleaseAsync(releaseStatus.PipelineOrganization, releaseStatus.PipelineProject, releaseStatus.ReleaseId);
                                string currentStatus = release.Environments[0].Status;
                                if (InProgressStatuses.Contains(currentStatus.ToLower()))
                                {
                                    allFinished = false;
                                }
                                else
                                {
                                    Logger.LogInformation($"Release {releaseId} finished executing with status: {currentStatus}");
                                    buildChannelsToAdd.Add(new BuildChannel
                                    {
                                        BuildId = buildId,
                                        ChannelId = releaseStatus.ChannelId
                                    });
                                }
                            }

                            // Stop tracking releases for builds where every release finished.
                            if (allFinished)
                            {
                                await runningPipelines.TryRemoveAsync(tx, asyncEnumerator.Current.Key);
                            }
                        }
                    }
                    if (buildChannelsToAdd.Count > 0)
                    {
                        AddFinishedBuildChannelsIfNotPresent(buildChannelsToAdd);
                    }
                    await tx.CommitAsync();
                }
            }
            catch (TaskCanceledException tcex) when (tcex.CancellationToken == cancellationToken)
            {
                // ignore
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Processing finished releases");
            }
        }

        private async Task<AzureDevOpsClient> GetAzureDevOpsClientForAccount(string account)
        {
            IAzureDevOpsTokenProvider azdoTokenProvider = Context.GetService<IAzureDevOpsTokenProvider>();
            string accessToken = await azdoTokenProvider.GetTokenForAccount(account);
            return new AzureDevOpsClient(accessToken, Logger, null);
        }

        private void AddFinishedBuildChannelsIfNotPresent(HashSet<BuildChannel> buildChannelsToAdd)
        {
            var missingBuildChannels = buildChannelsToAdd.Where(x => !Context.BuildChannels.Any(y => y.ChannelId == x.ChannelId && y.BuildId == x.BuildId)).ToList();
            Context.BuildChannels.AddRange(missingBuildChannels);
            Context.SaveChanges();
        }
    }
}
