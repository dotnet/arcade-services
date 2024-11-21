// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class DeleteBuildFromChannelOperation : Operation
{
    private readonly DeleteBuildFromChannelCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<DeleteBuildFromChannelOperation> _logger;

    public DeleteBuildFromChannelOperation(
        DeleteBuildFromChannelCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<DeleteBuildFromChannelOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    ///     Deletes a build from a channel.
    /// </summary>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // Find the build to give someone info
            Build build = await _barClient.GetBuildAsync(_options.Id);
            if (build == null)
            {
                Console.WriteLine($"Could not find a build with id '{_options.Id}'");
                return Constants.ErrorCode;
            }

            Channel targetChannel = await UxHelpers.ResolveSingleChannel(_barClient, _options.Channel);
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

            await _barClient.DeleteBuildFromChannelAsync(_options.Id, targetChannel.Id);

            // Let the user know they can trigger subscriptions if they'd like.
            Console.WriteLine("Subscriptions can be triggered to revert to the previous state using the following command:");
            Console.WriteLine($"darc trigger-subscriptions --source-repo {build.GetRepository()} --channel {targetChannel.Name}");

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Error: Failed to delete build '{_options.Id}' from channel '{_options.Channel}'.");
            return Constants.ErrorCode;
        }
    }
}
