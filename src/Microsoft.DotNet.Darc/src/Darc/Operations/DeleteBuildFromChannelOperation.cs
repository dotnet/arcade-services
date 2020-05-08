// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class DeleteBuildFromChannelOperation : Operation
    {
        DeleteBuildFromChannelCommandLineOptions _options;
        public DeleteBuildFromChannelOperation(DeleteBuildFromChannelCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        ///     Deletes a build from a channel.
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

                if (!build.Channels.Any(c => c.Id == targetChannel.Id))
                {
                    Console.WriteLine($"Build '{build.Id}' is not assigned to channel '{targetChannel.Name}'");
                    return Constants.SuccessCode;
                }

                Console.WriteLine($"Deleting the following build from channel '{targetChannel.Name}':");
                Console.WriteLine();
                Console.Write(UxHelpers.GetTextBuildDescription(build));

                await remote.DeleteBuildFromChannelAsync(_options.Id, targetChannel.Id);

                // Let the user know they can trigger subscriptions if they'd like.
                Console.WriteLine("Subscriptions can be triggered to revert to the previous state using the following command:");
                Console.WriteLine($"darc trigger-subscriptions --source-repo {build.GitHubRepository ?? build.AzureDevOpsRepository} --channel {targetChannel.Name}");

                return Constants.SuccessCode;
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine(e.Message);
                return Constants.ErrorCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error: Failed to delete build '{_options.Id}' from channel '{_options.Channel}'.");
                return Constants.ErrorCode;
            }
        }
    }
}
