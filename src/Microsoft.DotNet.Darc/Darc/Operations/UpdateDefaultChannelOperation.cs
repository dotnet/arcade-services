// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using IConfigurationRepositoryManager = Microsoft.DotNet.MaestroConfiguration.Client.IConfigurationRepositoryManager;

namespace Microsoft.DotNet.Darc.Operations;

internal class UpdateDefaultChannelOperation : Operation
{
    private readonly UpdateDefaultChannelCommandLineOptions _options;
    private readonly ILogger<UpdateDefaultChannelOperation> _logger;
    private readonly IBarApiClient _barClient;
    private readonly IConfigurationRepositoryManager _configurationRepositoryManager;

    public UpdateDefaultChannelOperation(
        UpdateDefaultChannelCommandLineOptions options,
        ILogger<UpdateDefaultChannelOperation> logger,
        IBarApiClient barClient,
        IConfigurationRepositoryManager configurationRepositoryManager)
    {
        _options = options;
        _logger = logger;
        _barClient = barClient;
        _configurationRepositoryManager = configurationRepositoryManager;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // Validate enable/disable flags
            if (_options.Enable.HasValue && _options.Disable.HasValue)
            {
                Console.WriteLine("Please specify either --enable or --disable, not both");
                return Constants.ErrorCode;
            }

            bool? enabled = null;
            if (_options.Enable.HasValue && _options.Enable.Value)
            {
                enabled = true;
            }
            else if (_options.Disable.HasValue && _options.Disable.Value)
            {
                enabled = false;
            }

            if (_options.ShouldUseConfigurationRepository)
            {
                return await UpdateViaConfigurationRepositoryAsync(enabled);
            }
            else
            {
                return await UpdateViaApiAsync(enabled);
            }
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to update default channel association.");
            return Constants.ErrorCode;
        }
    }

    private async Task<int> UpdateViaApiAsync(bool? enabled)
    {
        DefaultChannel resolvedChannel = await ResolveSingleChannelAsync();
        if (resolvedChannel == null)
        {
            return Constants.ErrorCode;
        }

        // Normalize the new branch if provided
        string newBranch = _options.NewBranch;
        if (!string.IsNullOrEmpty(newBranch))
        {
            newBranch = GitHelpers.NormalizeBranchName(newBranch);
        }

        await _barClient.UpdateDefaultChannelAsync(
            resolvedChannel.Id,
            repository: _options.NewRepository,
            branch: newBranch,
            channel: _options.NewChannel,
            enabled: enabled);

        Console.WriteLine($"Successfully updated default channel association (ID: {resolvedChannel.Id}).");
        return Constants.SuccessCode;
    }

    private async Task<int> UpdateViaConfigurationRepositoryAsync(bool? enabled)
    {
        // First, resolve the existing default channel to get the current values
        DefaultChannel resolvedChannel = await ResolveSingleChannelAsync();
        if (resolvedChannel == null)
        {
            return Constants.ErrorCode;
        }

        // Create the updated default channel YAML with the original values as the key
        var originalDefaultChannelYaml = DefaultChannelYaml.FromClientModel(resolvedChannel);

        // Build the updated values - use new values if provided, otherwise keep the original
        string updatedRepository = _options.NewRepository ?? resolvedChannel.Repository;
        string updatedBranch = _options.NewBranch ?? resolvedChannel.Branch;
        if (!string.IsNullOrEmpty(_options.NewBranch))
        {
            updatedBranch = GitHelpers.NormalizeBranchName(_options.NewBranch);
        }
        string updatedChannel = _options.NewChannel ?? resolvedChannel.Channel.Name;
        bool updatedEnabled = enabled ?? resolvedChannel.Enabled;

        var updatedDefaultChannelYaml = new DefaultChannelYaml
        {
            Repository = updatedRepository,
            Branch = updatedBranch,
            Channel = updatedChannel,
            Enabled = updatedEnabled
        };

        try
        {
            await _configurationRepositoryManager.UpdateDefaultChannelAsync(
                _options.ToConfigurationRepositoryOperationParameters(),
                new DefaultChannelYaml
                {
                    Repository = updatedRepository,
                    Branch = updatedBranch,
                    Channel = updatedChannel,
                    Enabled = updatedEnabled
                });

            Console.WriteLine($"Successfully updated default channel association.");
            return Constants.SuccessCode;
        }
        catch (MaestroConfiguration.Client.ConfigurationObjectNotFoundException)
        {
            _logger.LogError("Default channel with repository '{repo}', branch '{branch}', and channel '{channel}' not found in configuration repository.",
                originalDefaultChannelYaml.Repository,
                originalDefaultChannelYaml.Branch,
                originalDefaultChannelYaml.Channel);
            return Constants.ErrorCode;
        }
    }

    /// <summary>
    ///     Resolve channel based on the input options. If no channel could be resolved
    ///     based on the input options, returns null.
    /// </summary>
    /// <returns>Default channel or null</returns>
    private async Task<DefaultChannel> ResolveSingleChannelAsync()
    {
        IEnumerable<DefaultChannel> potentialDefaultChannels = await _barClient.GetDefaultChannelsAsync();

        // User should have supplied id or a combo of the channel name, repo, and branch.
        if (_options.Id != -1)
        {
            DefaultChannel defaultChannel = potentialDefaultChannels.SingleOrDefault(d => d.Id == _options.Id);
            if (defaultChannel == null)
            {
                Console.WriteLine($"Could not find a default channel with id {_options.Id}");
            }
            return defaultChannel;
        }
        else if (string.IsNullOrEmpty(_options.Repository) ||
                 string.IsNullOrEmpty(_options.Channel) ||
                 string.IsNullOrEmpty(_options.Branch))
        {
            Console.WriteLine("Please specify either the default channel id with --id or a combination of --channel, --branch and --repo");
            return null;
        }

        // Normalize the branch for comparison
        string normalizedBranch = GitHelpers.NormalizeBranchName(_options.Branch);

        // Otherwise, filter based on the other inputs. If more than one resolves, then print the possible
        // matches and return null
        var matchingChannels = potentialDefaultChannels.Where(d =>
        {
            return (string.IsNullOrEmpty(_options.Repository) || d.Repository.Contains(_options.Repository, StringComparison.OrdinalIgnoreCase)) &&
                   (string.IsNullOrEmpty(_options.Channel) || d.Channel.Name.Contains(_options.Channel, StringComparison.OrdinalIgnoreCase)) &&
                   (string.IsNullOrEmpty(_options.Branch) || d.Branch.Contains(normalizedBranch, StringComparison.OrdinalIgnoreCase));
        });

        if (!matchingChannels.Any())
        {
            Console.WriteLine($"No default channels found matching the specified criteria.");
            return null;
        }
        else if (matchingChannels.Count() != 1)
        {
            Console.WriteLine($"More than one default channel matching the specified criteria. Please change your options to be more specific.");
            foreach (DefaultChannel defaultChannel in matchingChannels)
            {
                Console.WriteLine($"    {UxHelpers.GetDefaultChannelDescriptionString(defaultChannel)}");
            }
            return null;
        }
        else
        {
            return matchingChannels.Single();
        }
    }
}
