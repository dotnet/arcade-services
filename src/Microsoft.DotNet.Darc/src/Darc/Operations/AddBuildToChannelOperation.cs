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
using System.Net.Http;
using Microsoft.DotNet.Services.Utility;

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
                int promoteBuildQueuedStatus = await PromoteBuildAsync(build, targetChannel, remote).ConfigureAwait(false);

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

        private async Task<int> PromoteBuildAsync(Build build, Channel targetChannel, IRemote remote)
        {
            if (_options.SkipAssetsPublishing)
            {
                await remote.AssignBuildToChannelAsync(build.Id, targetChannel.Id);
                Console.WriteLine($"Build {build.Id} was assigned to channel '{targetChannel.Name}' bypassing the promotion pipeline.");
                return Constants.SuccessCode;
            }

            LocalSettings localSettings = LocalSettings.LoadSettingsFile(_options);
            _options.AzureDevOpsPat = (string.IsNullOrEmpty(_options.AzureDevOpsPat)) ? localSettings.AzureDevOpsToken : _options.AzureDevOpsPat;

            if (string.IsNullOrEmpty(_options.AzureDevOpsPat))
            {
                Console.WriteLine($"Promoting build {build.Id} with the given parameters would require starting the Build Promotion pipeline, however an AzDO PAT was not found.");
                Console.WriteLine("Either specify an AzDO PAT as a parameter or add the --skip-assets-publishing parameter when calling Darc add-build-to-channel.");
                return Constants.ErrorCode;
            }

            var (arcadeSDKSourceBranch, arcadeSDKSourceSHA) = await GetSourceBranchInfoAsync(build).ConfigureAwait(false);

            // This condition can happen when for some reason we failed to determine the source branch/sha 
            // of the build that produced the used Arcade SDK or when the user specify an invalid combination
            // of source-sha/branch parameters.
            if (arcadeSDKSourceBranch == null && arcadeSDKSourceSHA == null)
            {
                return Constants.ErrorCode;
            }
            AzureDevOpsClient azdoClient = new AzureDevOpsClient(gitExecutable: null, _options.AzureDevOpsPat, Logger, temporaryRepositoryPath: null);

            var targetAzdoBuildStatus = await ValidateAzDOBuildAsync(azdoClient, build.AzureDevOpsAccount, build.AzureDevOpsProject, build.AzureDevOpsBuildId.Value)
                .ConfigureAwait(false);

            if (!targetAzdoBuildStatus)
            {
                return Constants.ErrorCode;
            }


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

            var promotionBuildUrl = $"https://{BuildPromotionPipelineAccountName}.visualstudio.com/{BuildPromotionPipelineProjectName}/_build/results?buildId={azdoBuildId}";

            Console.WriteLine($"Build {build.Id} will be assigned to channel '{targetChannel.Name}' once this build finishes publishing assets: {promotionBuildUrl}");

            if (_options.NoWait)
            {
                Console.WriteLine("Returning before asset publishing and channel assignment finishes. The operation continues asynchronously in AzDO.");
                return Constants.SuccessCode;
            }

            try
            {
                var waitIntervalInSeconds = TimeSpan.FromSeconds(60);
                AzureDevOpsBuild promotionBuild;

                do
                {
                    Console.WriteLine($"Waiting '{waitIntervalInSeconds.TotalSeconds}' seconds for promotion build to complete.");
                    await Task.Delay(waitIntervalInSeconds);
                    promotionBuild = await azdoClient.GetBuildAsync(BuildPromotionPipelineAccountName, BuildPromotionPipelineProjectName, azdoBuildId);
                } while (!promotionBuild.Status.Equals("completed", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                Console.WriteLine($"Darc couldn't check status of the promotion build. {e.Message}");
                return Constants.ErrorCode;
            }

            build = await remote.GetBuildAsync(build.Id);

            if (build.Channels.Any(c => c.Id == targetChannel.Id))
            {
                Console.WriteLine($"Build '{build.Id}' was successfully added to channel '({targetChannel.Id}) {targetChannel.Name}'");
                return Constants.SuccessCode;
            }
            else
            {
                Console.WriteLine("The promotion build finished but the build isn't associated with the channel. This is an error scenario. Please contact @dnceng.");
                return Constants.ErrorCode;
            }
        }

        private async Task<bool> ValidateAzDOBuildAsync(AzureDevOpsClient azdoClient, string azureDevOpsAccount, string azureDevOpsProject, int azureDevOpsBuildId)
        {
            try
            {
                var artifacts = await azdoClient.GetBuildArtifactsAsync(azureDevOpsAccount, azureDevOpsProject, azureDevOpsBuildId, maxRetries: 5);

                // The build manifest is always necessary
                if (!artifacts.Any(f => f.Name.Equals("AssetManifests")))
                {
                    Console.Write("The build that you want to add to a new channel doesn't have a Build Manifest. That's required for publishing. Aborting.");
                    return true;
                }

                if ((_options.DoSigningValidation || _options.DoNuGetValidation || _options.DoSourcelinkValidation)
                    && !artifacts.Any(f => f.Name.Equals("PackageArtifacts")))
                {
                    Console.Write("The build that you want to add to a new channel doesn't have a list of package assets. That's required when running signing or NuGet validation. Aborting.");
                    return true;
                }

                if (_options.DoSourcelinkValidation && !artifacts.Any(f => f.Name.Equals("BlobArtifacts")))
                {
                    Console.Write("The build that you want to add to a new channel doesn't have a list of blob assets. That's required when running SourceLink validation. Aborting.");
                    return true;
                }

                return true;
            }
            catch (HttpRequestException e) when (e.Message.Contains("404 (Not Found)"))
            {
                Console.Write("The build that you want to add to a new channel isn't available in AzDO anymore. Aborting.");
                return false;
            }
            catch (HttpRequestException e) when (e.Message.Contains("401 (Unauthorized)"))
            {
                Console.WriteLine("Got permission denied response while trying to retrieve target build from Azure DevOps. Aborting.");
                Console.Write("Please make sure that your Azure DevOps PAT has the build read and execute scopes set.");
                return false;
            }
        }

        /// <summary>
        /// By default the source branch/SHA for the Build Promotion pipeline will be the branch/SHA
        /// that produced the Arcade.SDK used by the build being promoted. The user can override that
        /// by specifying both, channel & SHA, on the command line.
        /// </summary>
        /// <param name="build">Build for which the Arcade SDK dependency build will be inferred.</param>
        private async Task<(string sourceBranch, string sourceVersion)> GetSourceBranchInfoAsync(Build build)
        {
            var hasSourceBranch = !string.IsNullOrEmpty(_options.SourceBranch);
            var hasSourceSHA = !string.IsNullOrEmpty(_options.SourceSHA);

            if (hasSourceBranch)
            {
                _options.SourceBranch = GitHelpers.NormalizeBranchName(_options.SourceBranch);
            }

            if (hasSourceBranch && hasSourceSHA)
            {
                return (_options.SourceBranch, _options.SourceSHA);
            }
            else if (hasSourceSHA && !hasSourceBranch)
            {
                Console.WriteLine("The `source-sha` parameter needs to be specified together with `source-branch`.");
                return (null, null);
            }
            else if (hasSourceBranch)
            {
                return (_options.SourceBranch, null);
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
                Console.WriteLine("The target build doesn't have a dependency on Microsoft.DotNet.Arcade.Sdk.");
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

            var oldestSupportedArcadeSDKDate = new DateTimeOffset(2020, 01, 28, 0, 0, 0, new TimeSpan(0, 0, 0));
            if (DateTimeOffset.Compare(sourceBuildArcadeSDKDepBuild.DateProduced, oldestSupportedArcadeSDKDate) < 0)
            {
                Console.WriteLine($"The target build uses an SDK released in {sourceBuildArcadeSDKDepBuild.DateProduced}");
                Console.WriteLine($"The target build needs to use an Arcade SDK version released after {oldestSupportedArcadeSDKDate} otherwise " +
                    $"you must inform the `source-branch` / `source-sha` parameters to point to a specific Arcade build.");
                Console.Write($"You can also pass the `skip-assets-publishing` parameter if all you want is to " +
                    $"assign the build to a channel. Note, though, that this will not publish the build assets.");
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
