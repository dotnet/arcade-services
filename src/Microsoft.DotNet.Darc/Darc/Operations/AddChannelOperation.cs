// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc.Yaml;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class AddChannelOperation : ConfigurationManagementOperation
{
    private readonly AddChannelCommandLineOptions _options;
    private readonly ILogger<AddChannelOperation> _logger;

    public AddChannelOperation(
            AddChannelCommandLineOptions options,
            IGitRepoFactory gitRepoFactory,
            IRemoteFactory remoteFactory,
            ILogger<AddChannelOperation> logger)
        : base(options, gitRepoFactory, remoteFactory, logger)
    {
        _options = options;
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

            bool openPr = string.IsNullOrEmpty(_options.ConfigurationBranch);

            await CreateConfigurationBranchIfNeeded();

            List<ChannelYamlData> channels = await GetConfiguration<ChannelYamlData>(ChannelConfigurationFileName);

            _logger.LogInformation("Found {channelCount} existing channels", channels.Count);

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

            _logger.LogInformation("Adding channel '{channelName}' to {fileName}", _options.Name, ChannelConfigurationFileName);
            await WriteConfigurationFile(ChannelConfigurationFileName, channels, $"Adding channel '{_options.Name}'");

            if (!_options.NoPr && (_options.Quiet || UxHelpers.PromptForYesNo($"Create PR with changes in {_options.ConfigurationRepository}?")))
            {
                await CreatePullRequest(
                    _options.ConfigurationRepository,
                    _options.ConfigurationBranch,
                    $"Add channel '{_options.Name}'",
                    string.Empty);
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
