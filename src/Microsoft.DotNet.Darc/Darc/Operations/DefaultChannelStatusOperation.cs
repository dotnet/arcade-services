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
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

internal class DefaultChannelStatusOperation : ConfigurationManagementOperation
{
    private readonly DefaultChannelStatusCommandLineOptions _options;
    private readonly ILogger<DefaultChannelStatusOperation> _logger;
    private readonly IRemoteFactory _remoteFactory;

    public DefaultChannelStatusOperation(
        DefaultChannelStatusCommandLineOptions options,
        IGitRepoFactory gitRepoFactory,
        IRemoteFactory remoteFactory,
        ILogger<DefaultChannelStatusOperation> logger)
        : base(options, gitRepoFactory, remoteFactory, logger)
    {
        _options = options;
        _logger = logger;
        _remoteFactory = remoteFactory;
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
            IRemote repoRemote = await _remoteFactory.CreateRemoteAsync(_options.Repository);

            // Normalize the branch name
            string normalizedBranch = GitHelpers.NormalizeBranchName(_options.Branch);

            if (!await UxHelpers.VerifyAndConfirmBranchExistsAsync(repoRemote, _options.Repository, normalizedBranch, !_options.Quiet))
            {
                Console.WriteLine("Aborting default channel status change.");
                return Constants.ErrorCode;
            }

            List<DefaultChannelYamlData> defaultChannels = await GetConfiguration<DefaultChannelYamlData>(DefaultChannelConfigurationFileName, _options.ConfigurationBaseBranch);

            DefaultChannelYamlData? channelToUpdate = defaultChannels.FirstOrDefault(c => 
                c.Repository == _options.Repository &&
                c.Branch == normalizedBranch &&
                c.Channel == _options.Channel);

            if (channelToUpdate == null)
            {
                _logger.LogError("Could not find default channel for repository '{repository}', branch '{branch}', channel '{channel}'", 
                    _options.Repository, normalizedBranch, _options.Channel);
                return Constants.ErrorCode;
            }

            bool targetEnabled = _options.Enable;
            
            if (channelToUpdate.Enabled == targetEnabled)
            {
                Console.WriteLine($"Default channel association is already {(targetEnabled ? "enabled" : "disabled")}");
                return Constants.ErrorCode;
            }

            await CreateConfigurationBranchIfNeeded();

            channelToUpdate.Enabled = targetEnabled;

            string action = targetEnabled ? "Enabling" : "Disabling";
            _logger.LogInformation("{action} default channel for {repo} / {branch} in {fileName}", action, _options.Repository, normalizedBranch, DefaultChannelConfigurationFileName);
            await WriteConfigurationFile(DefaultChannelConfigurationFileName, defaultChannels, $"{action} default channel for '{_options.Repository} / {normalizedBranch}'");

            if (!_options.NoPr && (_options.Quiet || UxHelpers.PromptForYesNo($"Create PR with changes in {_options.ConfigurationRepository}?")))
            {
                await CreatePullRequest(
                    _options.ConfigurationRepository,
                    _options.ConfigurationBranch,
                    _options.ConfigurationBaseBranch,
                    $"{action} default channel for '{_options.Repository} / {normalizedBranch}'",
                    string.Empty);
            }

            Console.WriteLine($"Default channel association has been {(targetEnabled ? "enabled" : "disabled")}.");

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
