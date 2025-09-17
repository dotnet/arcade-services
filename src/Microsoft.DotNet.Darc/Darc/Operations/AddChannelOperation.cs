// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc.Yaml;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Operations;

internal class AddChannelOperation : ConfigurationManagementOperation
{
    private readonly AddChannelCommandLineOptions _options;
    private readonly ILogger<AddChannelOperation> _logger;
    private readonly IBarApiClient _barClient;

    public AddChannelOperation(
            AddChannelCommandLineOptions options,
            IBarApiClient barClient,
            IGitRepoFactory gitRepoFactory,
            IRemoteFactory remoteFactory,
            ILogger<AddChannelOperation> logger)
        : base(options, gitRepoFactory, remoteFactory, logger)
    {
        _options = options;
        _barClient = barClient;
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

            await CreateConfigurationBranchIfNeeded();

            List<ChannelYamlData> channels = await GetConfiguration<ChannelYamlData>(ChannelConfigurationFileName);

            if (channels.Any(c => c.Name == _options.Name))
            {
                _logger.LogError("An existing channel with name '{channelName}' already exists", _options.Name);
                return Constants.ErrorCode;
            }

            // TODO: Put the channel in the right spot in the file
            channels.Add(new ChannelYamlData()
            {
                Name = _options.Name,
                Classification = _options.Classification
            });

            await WriteConfigurationFile(ChannelConfigurationFileName, channels, $"Adding channel '{_options.Name}'");

            var newChannelInfo = await _barClient.GetChannelAsync(_options.Name)
                ?? throw new DarcException("Failed to create new channel.");

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

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to create new channel.");
            return Constants.ErrorCode;
        }
    }
}
