// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using IConfigurationRepositoryManager = Microsoft.DotNet.MaestroConfiguration.Client.IConfigurationRepositoryManager;

namespace Microsoft.DotNet.Darc.Operations;

internal class DefaultChannelStatusOperation : UpdateDefaultChannelBaseOperation
{
    private readonly DefaultChannelStatusCommandLineOptions _options;
    private readonly ILogger<DefaultChannelStatusOperation> _logger;
    private readonly IConfigurationRepositoryManager _configurationRepositoryManager;

    public DefaultChannelStatusOperation(
        DefaultChannelStatusCommandLineOptions options,
        IBarApiClient barClient,
        IConfigurationRepositoryManager configurationRepositoryManager,
        ILogger<DefaultChannelStatusOperation> logger)
        : base(options, barClient)
    {
        _options = options;
        _logger = logger;
        _configurationRepositoryManager = configurationRepositoryManager;
    }

    /// <summary>
    /// Implements the default channel enable/disable operation
    /// </summary>
    public override async Task<int> ExecuteAsync()
    {
        if ((_options.Enable && _options.Disable) ||
            (!_options.Enable && !_options.Disable))
        {
            Console.WriteLine("Please specify either --enable or --disable");
            return Constants.ErrorCode;
        }

        try
        {
            DefaultChannel resolvedChannel = await ResolveSingleChannel();
            if (resolvedChannel == null)
            {
                return Constants.ErrorCode;
            }

            bool enabled;
            if (_options.Enable)
            {
                if (resolvedChannel.Enabled)
                {
                    Console.WriteLine($"Default channel association is already enabled");
                    return Constants.ErrorCode;
                }
                enabled = true;
            }
            else
            {
                if (!resolvedChannel.Enabled)
                {
                    Console.WriteLine($"Default channel association is already disabled");
                    return Constants.ErrorCode;
                }
                enabled = false;
            }

            if (_options.ShouldUseConfigurationRepository)
            {
                // Create an updated YAML default channel with the new enabled status
                DefaultChannelYaml updatedDefaultChannelYaml = new()
                {
                    Repository = resolvedChannel.Repository,
                    Branch = resolvedChannel.Branch,
                    Channel = resolvedChannel.Channel.Name,
                    Enabled = enabled
                };

                try
                {
                    await _configurationRepositoryManager.UpdateDefaultChannelAsync(
                                _options.ToConfigurationRepositoryOperationParameters(),
                                updatedDefaultChannelYaml);
                }
                // TODO drop to the "global try-catch" when configuration repo is the only behavior
                catch (MaestroConfiguration.Client.ConfigurationObjectNotFoundException ex)
                {
                    _logger.LogError("No existing default channel with repository '{repo}', branch '{branch}', and channel '{channel}' found in file {filePath} of repo {repo} on branch {branch}",
                        updatedDefaultChannelYaml.Repository,
                        updatedDefaultChannelYaml.Branch,
                        updatedDefaultChannelYaml.Channel,
                        ex.FilePath,
                        ex.RepositoryUri,
                        ex.BranchName);
                    return Constants.ErrorCode;
                }
            }
            else
            {
                await _barClient.UpdateDefaultChannelAsync(resolvedChannel.Id, enabled: enabled);

                Console.WriteLine($"Default channel association has been {(enabled ? "enabled" : "disabled")}.");
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
            _logger.LogError(e, "Error: Failed enable/disable default channel association.");
            return Constants.ErrorCode;
        }
    }
}
