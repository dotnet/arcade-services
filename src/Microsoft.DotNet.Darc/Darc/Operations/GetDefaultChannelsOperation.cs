// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
/// Implements an operation to get information about the default channel associations.
/// </summary>
internal class GetDefaultChannelsOperation : Operation
{
    private readonly GetDefaultChannelsCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<GetDefaultChannelsOperation> _logger;

    public GetDefaultChannelsOperation(
        GetDefaultChannelsCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<GetDefaultChannelsOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    /// Retrieve information about the default association between builds of a specific branch/repo
    /// and a channel.
    /// </summary>
    /// <returns></returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IEnumerable<DefaultChannel> defaultChannels = (await _barClient.GetDefaultChannelsAsync())
                .Where(defaultChannel =>
                {
                    return (string.IsNullOrEmpty(_options.SourceRepository) ||
                            defaultChannel.Repository.Contains(_options.SourceRepository, StringComparison.OrdinalIgnoreCase)) &&
                           (string.IsNullOrEmpty(_options.Branch) ||
                            defaultChannel.Branch.Contains(_options.Branch, StringComparison.OrdinalIgnoreCase)) &&
                           (string.IsNullOrEmpty(_options.Channel) ||
                            defaultChannel.Channel.Name.Contains(_options.Channel, StringComparison.OrdinalIgnoreCase));
                })
                .OrderBy(df => df.Repository);

            if (!defaultChannels.Any())
            {
                Console.WriteLine("No matching channels were found.");
            }

            // Write out a simple list of each channel's name
            foreach (DefaultChannel defaultChannel in defaultChannels)
            {
                Console.WriteLine(UxHelpers.GetDefaultChannelDescriptionString(defaultChannel));
            }

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to retrieve default channel information.");
            return Constants.ErrorCode;
        }
    }
}
