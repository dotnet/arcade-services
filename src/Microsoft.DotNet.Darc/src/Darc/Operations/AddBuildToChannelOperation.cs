// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class AddBuildToChannelOperation : Operation
    {
        private static readonly Dictionary<string, (string project, int pipelineId)> BuildPromotionPipelinesForAccount =
            new Dictionary<string, (string project, int pipelineId)>(StringComparer.OrdinalIgnoreCase)
            {
                { "dnceng", ("internal", 750) },
                { "devdiv", ("devdiv", 12603) }
            };

        // These channels are unsupported because the Arcade master branch 
        // (the branch that has build promotion infra) doesn't have YAML 
        // implementation for them. There is usually not a high demand for 
        // promoting builds to these channels.
        private readonly Dictionary<int, string> UnsupportedChannels = new Dictionary<int, string>()
        {
            { 3, ".NET Core 3 Dev" },
            { 19, ".NET Core 3 Release" },
            { 129, ".NET Core 3.1 Release" },
            { 184, ".NET Core 3.0 Internal Servicing" },
            { 344, ".NET 3 Eng" },
            { 390, ".NET 3 Eng - Validation" },
            { 531, ".NET Core 3.1 Blazor Features" },
            { 550, ".NET Core 3.1 Internal Servicing" },
            { 555, ".NET Core SDK 3.0.1xx Internal" },
            { 556, ".NET Core SDK 3.0.1xx" },
            { 557, ".NET Core SDK 3.1.2xx Internal" },
            { 558, ".NET Core SDK 3.1.2xx" },
            { 559, ".NET Core SDK 3.1.1xx Internal" },
            { 560, ".NET Core SDK 3.1.1xx" }
        };

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

                Build build = await remote.GetBuildAsync(_options.Id);
                if (build == null)
                {
                    Console.WriteLine($"Could not find a build with id '{_options.Id}'.");
                    return Constants.ErrorCode;
                }

                if (string.IsNullOrEmpty(_options.Channel) && !_options.AddToDefaultChannels)
                {
                    Console.WriteLine("You need to use --channel or --default-channels to inform the channel(s) that the build should be promoted to.");
                    return Constants.ErrorCode;
                }

                if (_options.PublishingInfraVersion < 2 || _options.PublishingInfraVersion > 3)
                {
                    Console.WriteLine($"Publishing version '{_options.PublishingInfraVersion}' is not configured. The following versions are available: 2, 3");
                    return Constants.ErrorCode;
                }

                if (_options.PublishingInfraVersion > 2 && _options.DoSDLValidation)
                {
                    Console.WriteLine($"Publishing version '{_options.PublishingInfraVersion}' does not support running SDL when adding a build to a channel");
                    return Constants.ErrorCode;
                }

                List<Channel> targetChannels = new List<Channel>();

                if (!string.IsNullOrEmpty(_options.Channel))
                {
                    Channel targetChannel = await UxHelpers.ResolveSingleChannel(remote, _options.Channel);
                    if (targetChannel == null)
                    {
                        return Constants.ErrorCode;
                    }

                    targetChannels.Add(targetChannel);
                }

                if (_options.AddToDefaultChannels)
                {
                    IEnumerable<DefaultChannel> defaultChannels = await remote.GetDefaultChannelsAsync(
                        build.GitHubRepository ?? build.AzureDevOpsRepository, 
                        build.GitHubBranch ?? build.AzureDevOpsBranch);

                    targetChannels.AddRange(
                        defaultChannels.
                            Where(dc => dc.Enabled).
                            Select(dc => dc.Channel));
                }

                IEnumerable<Channel> currentChannels = build.Channels.Where(ch => targetChannels.Any(tc => tc.Id == ch.Id));
                if (currentChannels.Any())
                {
                    Console.WriteLine($"The build '{build.Id}' is already on these target channel(s):");

                    foreach (var channel in currentChannels)
                    {
                        Console.WriteLine($"\t{channel.Name}");
                        targetChannels.RemoveAll(tch => tch.Id == channel.Id);
                    }
                }

                if (!targetChannels.Any())
                {
                    Console.WriteLine($"Build '{build.Id}' is already on all target channel(s).");
                    return Constants.SuccessCode;
                }

                if (targetChannels.Any(ch => UnsupportedChannels.ContainsKey(ch.Id)))
                {
                    Console.WriteLine($"Currently Darc doesn't support build promotion to the following channels:");

                    foreach (var channel in UnsupportedChannels)
                    {
                        Console.WriteLine($"\t ({channel.Key}) {channel.Value}");
                    }

                    Console.WriteLine("Please contact @dnceng to see other options.");
                    return Constants.ErrorCode;
                }

                // Queues a build of the Build Promotion pipeline that will takes care of making sure
                // that the build assets are published to the right location and also promoting the build
                // to the requested channel
                int promoteBuildQueuedStatus = await PromoteBuildAsync(build, targetChannels, remote)
                    .ConfigureAwait(false);

                if (promoteBuildQueuedStatus != Constants.SuccessCode)
                {
                    return Constants.ErrorCode;
                }

                Console.WriteLine($"Assigning build '{build.Id}' to the following channel(s):");
                foreach (var channel in targetChannels)
                {
                    Console.WriteLine($"\t{channel.Name}");
                }
                Console.WriteLine();
                Console.Write(UxHelpers.GetTextBuildDescription(build));

                // Be helpful. Let the user know what will happen.
                string buildRepo = build.GitHubRepository ?? build.AzureDevOpsRepository;
                List<Subscription> applicableSubscriptions = new List<Subscription>();

                foreach (var targetChannel in targetChannels)
                {
                    IEnumerable<Subscription> appSubscriptions = await remote.GetSubscriptionsAsync(
                        sourceRepo: buildRepo, 
                        channelId: targetChannel.Id);

                    applicableSubscriptions.AddRange(appSubscriptions);
                }

                PrintSubscriptionInfo(applicableSubscriptions);

                return Constants.SuccessCode;
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine(e.Message);
                return Constants.ErrorCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error: Failed to assign build '{_options.Id}' to channel '{_options.Channel}'.");
                return Constants.ErrorCode;
            }
        }

        private async Task<int> PromoteBuildAsync(Build build, List<Channel> targetChannels, IRemote remote)
        {
            if (_options.SkipAssetsPublishing)
            {
                foreach (var targetChannel in targetChannels)
                {
                    await remote.AssignBuildToChannelAsync(build.Id, targetChannel.Id);
                    Console.WriteLine($"Build {build.Id} was assigned to channel '{targetChannel.Name}' bypassing the promotion pipeline.");
                }
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

            if (!BuildPromotionPipelinesForAccount.TryGetValue(
                build.AzureDevOpsAccount,
                out (string project, int pipelineId) promotionPipelineInformation))
            {
                Console.WriteLine($"Promoting builds from AzureDevOps account {build.AzureDevOpsAccount} is not supported by this command.");
                return Constants.ErrorCode;
            }

            // Construct the templateParameters and queue time variables. 
            // Publishing v2 uses variables and v3 uses parameters, so just use the same values for both.
            var promotionPipelineVariables = new Dictionary<string, string>
            {
                { "BarBuildId", build.Id.ToString() },
                { "PublishingInfraVersion", _options.PublishingInfraVersion.ToString() },
                { "PromoteToChannelIds", string.Join(",", targetChannels.Select(tch => tch.Id)) },
                { "EnableSigningValidation", _options.DoSigningValidation.ToString() },
                { "SigningValidationAdditionalParameters", _options.SigningValidationAdditionalParameters },
                { "EnableNugetValidation", _options.DoNuGetValidation.ToString() },
                { "EnableSourceLinkValidation", _options.DoSourcelinkValidation.ToString() },
                { "PublishInstallersAndChecksums", _options.PublishInstallersAndChecksums.ToString() },
                { "SymbolPublishingAdditionalParameters", _options.SymbolPublishingAdditionalParameters },
                { "ArtifactsPublishingAdditionalParameters", _options.ArtifactPublishingAdditionalParameters }
            };

            if (_options.DoSDLValidation)
            {
                promotionPipelineVariables.Add("EnableSDLValidation", _options.DoSDLValidation.ToString());
                promotionPipelineVariables.Add("SDLValidationCustomParams", _options.SDLValidationParams);
                promotionPipelineVariables.Add("SDLValidationContinueOnError", _options.SDLValidationContinueOnError);
            }

            // Pass the same values to the variables and pipeline parameters so this works with the
            // v2 and v3 versions of the promotion pipeline.
            int azdoBuildId = await azdoClient.StartNewBuildAsync(build.AzureDevOpsAccount,
                promotionPipelineInformation.project,
                promotionPipelineInformation.pipelineId,
                arcadeSDKSourceBranch,
                arcadeSDKSourceSHA,
                promotionPipelineVariables,
                promotionPipelineVariables
                ).ConfigureAwait(false);

            string promotionBuildUrl = $"https://{build.AzureDevOpsAccount}.visualstudio.com/{promotionPipelineInformation.project}/_build/results?buildId={azdoBuildId}";

            Console.WriteLine($"Build {build.Id} will be assigned to target channel(s) once this build finishes publishing assets: {promotionBuildUrl}");

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
                    promotionBuild = await azdoClient.GetBuildAsync(
                        build.AzureDevOpsAccount,
                        promotionPipelineInformation.project,
                        azdoBuildId);
                } while (!promotionBuild.Status.Equals("completed", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                Console.WriteLine($"Darc couldn't check status of the promotion build. {e.Message}");
                return Constants.ErrorCode;
            }

            build = await remote.GetBuildAsync(build.Id);

            if (targetChannels.All(ch => build.Channels.Any(c => c.Id == ch.Id)))
            {
                Console.WriteLine($"Build '{build.Id}' was successfully added to the target channel(s).");
                return Constants.SuccessCode;
            }
            else
            {
                Console.WriteLine("The promotion build finished but the build isn't associated with at least one of the target channels. This is an error scenario. Please contact @dnceng.");
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
                    return false;
                }

                if ((_options.DoSigningValidation || _options.DoNuGetValidation || _options.DoSourcelinkValidation)
                    && !artifacts.Any(f => f.Name.Equals("PackageArtifacts")))
                {
                    Console.Write("The build that you want to add to a new channel doesn't have a list of package assets in the PackageArtifacts container. That's required when running signing or NuGet validation. Aborting.");
                    return false;
                }

                if (_options.DoSourcelinkValidation && !artifacts.Any(f => f.Name.Equals("BlobArtifacts")))
                {
                    Console.Write("The build that you want to add to a new channel doesn't have a list of blob assets in the BlobArtifacts container. That's required when running SourceLink validation. Aborting.");
                    return false;
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
        /// by specifying both, branch & SHA, on the command line.
        /// </summary>
        /// <param name="build">Build for which the Arcade SDK dependency build will be inferred.</param>
        private async Task<(string sourceBranch, string sourceVersion)> GetSourceBranchInfoAsync(Build build)
        {
            bool hasSourceBranch = !string.IsNullOrEmpty(_options.SourceBranch);
            bool hasSourceSHA = !string.IsNullOrEmpty(_options.SourceSHA);

            if (hasSourceBranch)
            {
                _options.SourceBranch = GitHelpers.NormalizeBranchName(_options.SourceBranch);

                if (_options.SourceBranch.EndsWith("release/3.x", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Warning: Arcade branch {_options.SourceBranch} doesn't support build promotion. Please try specifiying --source-branch 'master'.");
                    Console.WriteLine("Switching source branch to Arcade master.");
                    return ("master", null);
                }
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

            if (sourceBuildArcadeSDKDepBuild.GitHubBranch.EndsWith("release/3.x", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Warning: To promote a build that uses a 3.x version of Arcade SDK you need to inform the --source-branch 'master' parameter.");
                Console.WriteLine("Switching source branch to Arcade master.");
                return ("master", null);
            }

            var oldestSupportedArcadeSDKDate = new DateTimeOffset(2020, 01, 28, 0, 0, 0, new TimeSpan(0, 0, 0));
            if (DateTimeOffset.Compare(sourceBuildArcadeSDKDepBuild.DateProduced, oldestSupportedArcadeSDKDate) < 0)
            {
                Console.WriteLine($"The target build uses an SDK released in {sourceBuildArcadeSDKDepBuild.DateProduced}");
                Console.WriteLine($"The target build needs to use an Arcade SDK 5.x.x version released after {oldestSupportedArcadeSDKDate} otherwise " +
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
