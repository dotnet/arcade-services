// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Operations;

internal class UpdateChannelOperation : Operation
{
    private readonly UpdateChannelCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<UpdateChannelOperation> _logger;

    public UpdateChannelOperation(
        UpdateChannelCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<UpdateChannelOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    /// Updates an existing channel's metadata (name and/or classification).
    /// </summary>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // Validate that at least one of name or classification is provided
            if (string.IsNullOrEmpty(_options.Name) && string.IsNullOrEmpty(_options.Classification))
            {
                Console.WriteLine("Either --name or --classification (or both) must be specified.");
                return Constants.ErrorCode;
            }

            // Retrieve the current channel information first to confirm it exists
            var channels = await _barClient.GetChannelsAsync();
            var channel = channels.FirstOrDefault(c => c.Id == _options.Id);
            
            if (channel == null)
            {
                Console.WriteLine($"Could not find a channel with id '{_options.Id}'.");
                return Constants.ErrorCode;
            }

            // Update the channel with the specified information
            var updatedChannel = await _barClient.UpdateChannelAsync(_options.Id, _options.Name, _options.Classification);

            switch (_options.OutputFormat)
            {
                case DarcOutputType.json:
                    Console.WriteLine(JsonConvert.SerializeObject(
                        new
                        {
                            id = updatedChannel.Id,
                            name = updatedChannel.Name,
                            classification = updatedChannel.Classification
                        },
                        Formatting.Indented));
                    break;
                case DarcOutputType.text:
                    Console.WriteLine($"Successfully updated channel with id '{_options.Id}':");
                    Console.WriteLine($"  Name: {updatedChannel.Name}");
                    Console.WriteLine($"  Classification: {updatedChannel.Classification}");
                    break;
                default:
                    throw new NotImplementedException($"Output type {_options.OutputFormat} not supported by update-channel");
            }

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (RestApiException e) when (e.Response.Status == (int)HttpStatusCode.Conflict)
        {
            _logger.LogError($"A channel with the specified name already exists.");
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to update channel.");
            return Constants.ErrorCode;
        }
    }
}
