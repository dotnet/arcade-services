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
        private int BuildPromotionPipelineId { get; } = 750;
        private string BuildPromotionPipelineAccountName { get; } = "dnceng";
        private string BuildPromotionPipelineProjectName { get; } = "internal";

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
                await PromoteBuildAsync(targetChannel.Id).ConfigureAwait(false);

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

        private async Task PromoteBuildAsync(int PromoteToMaestroChannelId)
        {
            if (string.IsNullOrEmpty(_options.AzureDevOpsPat))
            {
                LocalSettings localSettings = LocalSettings.LoadSettingsFile(_options);
                _options.AzureDevOpsPat = localSettings.AzureDevOpsToken;
            }

            AzureDevOpsClient azdoClient = new AzureDevOpsClient(gitExecutable: null, _options.AzureDevOpsPat, Logger, temporaryRepositoryPath: null);

            var queueTimeVariables = $"{{" +
                $"\"BARBuildId\": \"{ _options.Id }\", " +
                $"\"PromoteToMaestroChannelId\": \"{ PromoteToMaestroChannelId }\", " +
                $"\"EnableSigningValidation\": \"{ _options.DoSigningValidation }\", " +
                $"\"EnableNugetValidation\": \"{ _options.DoNuGetValidation }\", " +
                $"\"EnableSourceLinkValidation\": \"{ _options.DoSourcelinkValidation }\", " +
                $"\"EnableSDLValidation\": \"{ _options.DoSDLValidation }\", " +
                $"\"SDLValidationCustomParams\": \"{ _options.SDLValidationParams }\", " +
                $"\"SDLValidationContinueOnError\": \"{ _options.SDLValidationContinueOnError }\", " +
                $"}}";

            await azdoClient.StartNewBuildAsync(BuildPromotionPipelineAccountName, 
                BuildPromotionPipelineProjectName, 
                BuildPromotionPipelineId, 
                queueTimeVariables)
                .ConfigureAwait(false);
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
