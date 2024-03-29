// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class DeleteChannelOperation : Operation
{
    private readonly DeleteChannelCommandLineOptions _options;
    public DeleteChannelOperation(DeleteChannelCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    /// <summary>
    /// Deletes a channel by name
    /// </summary>
    /// <returns></returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IBarApiClient barClient = Provider.GetRequiredService<IBarApiClient>();

            // Get the ID of the channel with the specified name.
            Channel existingChannel = (await barClient.GetChannelsAsync()).Where(channel => channel.Name.Equals(_options.Name, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

            if (existingChannel == null)
            {
                Logger.LogError($"Could not find channel with name '{_options.Name}'");
                return Constants.ErrorCode;
            }

            await barClient.DeleteChannelAsync(existingChannel.Id);
            Console.WriteLine($"Successfully deleted channel '{existingChannel.Name}'.");

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error: Failed to delete channel.");
            return Constants.ErrorCode;
        }
    }
}
