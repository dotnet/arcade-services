// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class AddBuildToChannelOperation : Operation
    {
        private const int BuildPromotionPipelineId = 750;
        private const string BuildPromotionPipelineAccountName = "dnceng";
        private const string BuildPromotionPipelineProjectName = "internal";

        AddBuildToChannelCommandLineOptions _options;
        public AddBuildToChannelOperation(AddBuildToChannelCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        ///     Assigns a build to a channel.
        /// </summary>
        /// <returns>Process exit code.</returns>
        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                // Find the build to give someone info
                Build build = await remote.GetBuildAsync(_options.Id);
                if (build == null)
                {
                    Console.WriteLine($"Could not find a build with id '{_options.Id}'");
                    return Constants.ErrorCode;
                }

                Channel targetChannel = await UxHelpers.ResolveSingleChannel(remote, _options.Channel);
                if (targetChannel == null)
                {
                    return Constants.ErrorCode;
                }

                if (build.Channels.Any(c => c.Id == targetChannel.Id))
                {
                    Console.WriteLine($"Build '{build.Id}' has already been assigned to '{targetChannel.Name}'");
                    return Constants.SuccessCode;
                }

                Console.WriteLine($"Assigning the following build to channel '{targetChannel.Name}':");
                Console.WriteLine();
                Console.Write(UxHelpers.GetBuildDescription(build));

                // Queues a build of the Build Promotion pipeline that will takes care of making sure
                // that the build assets are published to the right location and also promoting the build
                // to the requested channel
                int promoteBuildQueuedStatus = await PromoteBuildAsync(build, targetChannel).ConfigureAwait(false);

                if (promoteBuildQueuedStatus != Constants.SuccessCode)
                {
                    return Constants.ErrorCode;
                }

                // Be helpful. Let the user know what will happen.
                string buildRepo = build.GitHubRepository ?? build.AzureDevOpsRepository;
                List<Subscription> applicableSubscriptions = (await remote.GetSubscriptionsAsync(
                    sourceRepo: buildRepo, channelId: targetChannel.Id)).ToList();

                PrintSubscriptionInfo(applicableSubscriptions);

                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error: Failed to assign build '{_options.Id}' to channel '{_options.Channel}'.");
                return Constants.ErrorCode;
            }
        }

        private async Task<int> PromoteBuildAsync(Build build, Channel targetChannel)
        {
            LocalSettings localSettings = LocalSettings.LoadSettingsFile(_options);
            _options.AzureDevOpsPat = (string.IsNullOrEmpty(_options.AzureDevOpsPat)) ? localSettings.AzureDevOpsToken : _options.AzureDevOpsPat;
            _options.GitHubPat = (string.IsNullOrEmpty(_options.GitHubPat)) ? localSettings.GitHubToken : _options.GitHubPat;

            var (arcadeSDKSourceBranch, arcadeSDKSourceSHA) = await GetSourceBranchInfoAsync(build).ConfigureAwait(false);

            // This condition can happen when for some reason we failed to determine the source branch/sha 
            // of the build that produced the used Arcade SDK
            if (arcadeSDKSourceBranch == null || arcadeSDKSourceSHA == null)
            {
                return Constants.ErrorCode;
            }

            AzureDevOpsClient azdoClient = new AzureDevOpsClient(gitExecutable: null, _options.AzureDevOpsPat, Logger, temporaryRepositoryPath: null);

            var queueTimeVariables = $"{{" +
                $"\"BARBuildId\": \"{ build.Id }\", " +
                $"\"PromoteToMaestroChannelId\": \"{ targetChannel.Id }\", " +
                $"\"EnableSigningValidation\": \"{ _options.DoSigningValidation }\", " +
                $"\"EnableNugetValidation\": \"{ _options.DoNuGetValidation }\", " +
                $"\"EnableSourceLinkValidation\": \"{ _options.DoSourcelinkValidation }\", " +
                $"\"EnableSDLValidation\": \"{ _options.DoSDLValidation }\", " +
                $"\"SDLValidationCustomParams\": \"{ _options.SDLValidationParams }\", " +
                $"\"SDLValidationContinueOnError\": \"{ _options.SDLValidationContinueOnError }\", " +
                $"}}";

            var azdoBuildId = await azdoClient.StartNewBuildAsync(BuildPromotionPipelineAccountName, 
                BuildPromotionPipelineProjectName, 
                BuildPromotionPipelineId, 
                arcadeSDKSourceBranch,
                arcadeSDKSourceSHA,
                queueTimeVariables)
                .ConfigureAwait(false);

            Console.WriteLine($"Build {build.Id} will be assigned to channel '{targetChannel.Name}' once this promotion build finishes: " +
                $"https://{BuildPromotionPipelineAccountName}.visualstudio.com/{BuildPromotionPipelineProjectName}/_build/results?buildId={azdoBuildId}&view=results");

            return Constants.SuccessCode;
        }

        /// <summary>
        /// By default the source branch/SHA for the Build Promotion pipeline will be the branch/SHA
        /// that produced the Arcade.SDK used by the build being promoted. The user can override that
        /// by specifying both, channel & SHA, on the command line.
        /// </summary>
        /// <param name="build">Build for which the Arcade SDK dependency build will be inferred.</param>
        private async Task<(string sourceBranch, string sourceVersion)> GetSourceBranchInfoAsync(Build build)
        {
            if (!string.IsNullOrEmpty(_options.SourceBranch) && !string.IsNullOrEmpty(_options.SourceSHA))
            {
                return (_options.SourceBranch, _options.SourceSHA);
            }

            string sourceBuildRepo = string.IsNullOrEmpty(build.GitHubRepository) ?
                    build.AzureDevOpsRepository :
                    build.GitHubRepository;

            IRemote repoAndBarRemote = RemoteFactory.GetRemote(_options, sourceBuildRepo, Logger);

            IEnumerable<DependencyDetail> sourceBuildDependencies = await repoAndBarRemote.GetDependenciesAsync(sourceBuildRepo, build.Commit)
                .ConfigureAwait(false);

            DependencyDetail sourceBuildArcadeSDKDependency = sourceBuildDependencies.FirstOrDefault(i => string.Equals(i.Name, "Microsoft.DotNet.Arcade.Sdk", StringComparison.OrdinalIgnoreCase));

            if (sourceBuildArcadeSDKDependency == null)
            {
                Console.WriteLine("You're trying to promote a build that doesn't have a dependency on Microsoft.DotNet.Arcade.Sdk.");
                return (null, null);
            }

            IEnumerable<Asset> listArcadeSDKAssets = await repoAndBarRemote.GetAssetsAsync(sourceBuildArcadeSDKDependency.Name, sourceBuildArcadeSDKDependency.Version)
                .ConfigureAwait(false);

            Asset sourceBuildArcadeSDKDepAsset = listArcadeSDKAssets.FirstOrDefault();
            
            if (sourceBuildArcadeSDKDepAsset == null)
            {
                Console.WriteLine($"Could not fetch information about Microsoft.DotNet.Arcade.Sdk asset version {sourceBuildArcadeSDKDependency.Version}.");
                return (null, null);
            }

            Build sourceBuildArcadeSDKDepBuild = await repoAndBarRemote.GetBuildAsync(sourceBuildArcadeSDKDepAsset.BuildId);

            if (sourceBuildArcadeSDKDepBuild == null)
            {
                Console.Write($"Could not find information (in BAR) about the build that produced Microsoft.DotNet.Arcade.Sdk version {sourceBuildArcadeSDKDependency.Version}.");
                return (null, null);
            }

            return (sourceBuildArcadeSDKDepBuild.GitHubBranch, sourceBuildArcadeSDKDepBuild.Commit);
        }

        private void PrintSubscriptionInfo(List<Subscription> applicableSubscriptions)
        {
            IEnumerable<Subscription> subscriptionsThatWillFlowImmediately = applicableSubscriptions.Where(s => s.Enabled &&
                    s.Policy.UpdateFrequency == UpdateFrequency.EveryBuild);
            IEnumerable<Subscription> subscriptionsThatWillFlowTomorrowOrNotAtAll = applicableSubscriptions.Where(s => s.Enabled &&
                    s.Policy.UpdateFrequency != UpdateFrequency.EveryBuild);
            IEnumerable<Subscription> disabledSubscriptions = applicableSubscriptions.Where(s => !s.Enabled);

            // Print out info
            if (subscriptionsThatWillFlowImmediately.Any())
            {
                Console.WriteLine("The following repos/branches will apply this build immediately:");
                foreach (var sub in subscriptionsThatWillFlowImmediately)
                {
                    Console.WriteLine($"  {sub.TargetRepository} @ {sub.TargetBranch}");
                }
            }

            if (subscriptionsThatWillFlowTomorrowOrNotAtAll.Any())
            {
                Console.WriteLine("The following repos/branches will apply this change at a later time, or not by default.");
                Console.WriteLine("To flow immediately, run the specified command");
                foreach (var sub in subscriptionsThatWillFlowTomorrowOrNotAtAll)
                {
                    Console.WriteLine($"  {sub.TargetRepository} @ {sub.TargetBranch} (update freq: {sub.Policy.UpdateFrequency})");
                    Console.WriteLine($"    darc trigger-subscriptions --id {sub.Id}");
                }
            }

            if (disabledSubscriptions.Any())
            {
                Console.WriteLine("The following repos/branches will not get this change because their subscriptions are disabled.");
                foreach (var sub in disabledSubscriptions)
                {
                    Console.WriteLine($"  {sub.TargetRepository} @ {sub.TargetBranch}");
                }
            }
        }
    }
}
