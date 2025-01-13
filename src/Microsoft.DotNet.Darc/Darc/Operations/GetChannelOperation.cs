// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Operations;

internal class GetChannelOperation : Operation
{
    private readonly GetChannelCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<GetChannelOperation> _logger;

    public GetChannelOperation(
        GetChannelCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<GetChannelOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    /// Retrieve information about a specific channel
    /// </summary>
    /// <param name="options">Command line options</param>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            var channel = await _barClient.GetChannelAsync(_options.Id);

            if (channel == null)
            {
                _logger.LogError("Channel with id {channelId} not found", _options.Id);
                return Constants.ErrorCode;
            }

            switch (_options.OutputFormat)
            {
                case DarcOutputType.json:
                    Console.WriteLine(JsonConvert.SerializeObject(channel, Formatting.Indented));
                    break;
                case DarcOutputType.text:
                    Console.WriteLine($"({channel.Id}) {channel.Name}");
                    break;
                default:
                    throw new NotImplementedException($"Output format {_options.OutputFormat} not supported for get-channel");
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
            _logger.LogError(e, "Error: Failed to retrieve the channel");
            return Constants.ErrorCode;
        }
    }
}
