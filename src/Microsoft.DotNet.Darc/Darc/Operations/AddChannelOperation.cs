// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Operations;

internal class AddChannelOperation : Operation
{
    private readonly AddChannelCommandLineOptions _options;
    private readonly ILogger<AddChannelOperation> _logger;
    private readonly IBarApiClient _barClient;
    private readonly IConfigurationRepositoryManager _configurationRepositoryManager;

    public AddChannelOperation(
        AddChannelCommandLineOptions options,
        IBarApiClient barClient,
        IConfigurationRepositoryManager configurationRepositoryManager,
        ILogger<AddChannelOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _configurationRepositoryManager = configurationRepositoryManager;
        _logger = logger;
    }

    /// <summary>
    /// Adds a new channel with the specified name.
    /// </summary>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // If the user tried to mark as internal, indicate that this is currently
            // unsupported.
            if (_options.Internal)
            {
                _logger.LogError("Cannot currently mark channels as internal.");
                return Constants.ErrorCode;
            }

            if (_options.ShouldUseConfigurationRepository)
            {
                var channelYaml = new ChannelYaml
                {
                    Name = _options.Name,
                    Classification = _options.Classification
                };

                await ValidateNoEquivalentChannel(channelYaml);

                try
                {
                    await _configurationRepositoryManager.AddChannelAsync(
                                _options.ToConfigurationRepositoryOperationParameters(),
                                channelYaml);
                }
                // TODO https://github.com/dotnet/arcade-services/issues/5693 drop to the "global try-catch" when configuration repo is the only behavior
                catch (DuplicateConfigurationObjectException e)
                {
                    _logger.LogError("Channel with name '{name}' already exists in '{filePath}'.",
                       channelYaml.Name,
                       e.FilePath);
                    return Constants.ErrorCode;
                }
            }
            else
            {
                Channel newChannelInfo = await _barClient.CreateChannelAsync(_options.Name, _options.Classification);
                switch (_options.OutputFormat)
                {
                    case DarcOutputType.json:
                        Console.WriteLine(JsonConvert.SerializeObject(
                            new
                            {
                                id = newChannelInfo.Id,
                                name = newChannelInfo.Name,
                                classification = newChannelInfo.Classification
                            },
                            Formatting.Indented));
                        break;
                    case DarcOutputType.text:
                        Console.WriteLine($"Successfully created new channel with name '{_options.Name}' and id {newChannelInfo.Id}.");
                        break;
                    default:
                        throw new NotImplementedException($"Output type {_options.OutputFormat} not supported by add-channel");
                }
            }

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (RestApiException e) when (e.Response.Status == (int) HttpStatusCode.Conflict)
        {
            _logger.LogError($"An existing channel with name '{_options.Name}' already exists");
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to create new channel.");
            return Constants.ErrorCode;
        }
    }

    private async Task ValidateNoEquivalentChannel(ChannelYaml channelYaml)
    {
        // Check if a channel with the same name already exists in BAR
        var existingChannels = await _barClient.GetChannelsAsync();
        var existingChannel = existingChannels.FirstOrDefault(c => string.Equals(c.Name, channelYaml.Name, StringComparison.OrdinalIgnoreCase));
        if (existingChannel != null)
        {
            throw new ArgumentException($"A channel with name '{existingChannel.Name}' already exists.");
        }
    }
}
