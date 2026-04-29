// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class UpdateChannelOperation : Operation
{
    private readonly UpdateChannelCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly IConfigurationRepositoryManager _configurationRepositoryManager;
    private readonly ILogger<UpdateChannelOperation> _logger;

    public UpdateChannelOperation(
        UpdateChannelCommandLineOptions options,
        IBarApiClient barClient,
        IConfigurationRepositoryManager configurationRepositoryManager,
        ILogger<UpdateChannelOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _configurationRepositoryManager = configurationRepositoryManager;
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
                _logger.LogError("Either --name or --classification (or both) must be specified.");
                return Constants.ErrorCode;
            }

            // Retrieve the current channel information first to confirm it exists
            var channel = await _barClient.GetChannelAsync(_options.Id);
            if (channel == null)
            {
                _logger.LogError("Could not find a channel with id '{id}'", _options.Id);
                return Constants.ErrorCode;
            }

            // When using configuration repository, channel name is immutable
            if (!string.IsNullOrEmpty(_options.Name) && !string.Equals(channel.Name, _options.Name, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Channel name cannot be changed. Channel name is immutable.");
                return Constants.ErrorCode;
            }

            // Create an updated channel YAML from the existing channel and update classification if provided
            var updatedChannelYaml = ChannelYaml.FromClientModel(channel) with
            {
                Classification = _options.Classification ?? channel.Classification
            };

            await _configurationRepositoryManager.UpdateChannelAsync(
                _options.ToConfigurationRepositoryOperationParameters(),
                updatedChannelYaml);

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (ConfigurationObjectNotFoundException ex)
        {
            _logger.LogError("No existing channel with name '{name}' found in file '{filePath}' of repo '{repo}' on branch '{branch}'",
                _options.Name,
                ex.FilePath,
                ex.RepositoryUri,
                ex.BranchName);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to update channel.");
            return Constants.ErrorCode;
        }
    }
}
