// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class DeleteChannelOperation : Operation
{
    private readonly DeleteChannelCommandLineOptions _options;
    private readonly ILogger<DeleteChannelOperation> _logger;

    public DeleteChannelOperation(
        CommandLineOptions options,
        ILogger<DeleteChannelOperation> logger,
        IBarApiClient barClient)
        : base(barClient)
    {
        _options = (DeleteChannelCommandLineOptions)options;
        _logger = logger;
    }

    /// <summary>
    /// Deletes a channel by name
    /// </summary>
    /// <returns></returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // Get the ID of the channel with the specified name.
            Channel existingChannel = (await _barClient.GetChannelsAsync()).Where(channel => channel.Name.Equals(_options.Name, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

            if (existingChannel == null)
            {
                _logger.LogError($"Could not find channel with name '{_options.Name}'");
                return Constants.ErrorCode;
            }

            await _barClient.DeleteChannelAsync(existingChannel.Id);
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
            _logger.LogError(e, "Error: Failed to delete channel.");
            return Constants.ErrorCode;
        }
    }
}
