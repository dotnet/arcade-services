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

internal class DeleteDefaultChannelOperation : ConfigurationManagementOperation
{
    private readonly DeleteDefaultChannelCommandLineOptions _options;
    private readonly ILogger<DeleteDefaultChannelOperation> _logger;
    private readonly IRemoteFactory _remoteFactory;

    public DeleteDefaultChannelOperation(
        DeleteDefaultChannelCommandLineOptions options,
        IGitRepoFactory gitRepoFactory,
        IRemoteFactory remoteFactory,
        ILogger<DeleteDefaultChannelOperation> logger)
        : base(options, gitRepoFactory, remoteFactory, logger)
    {
        _options = options;
        _logger = logger;
        _remoteFactory = remoteFactory;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IRemote repoRemote = await _remoteFactory.CreateRemoteAsync(_options.Repository);

            // Normalize the branch name
            string normalizedBranch = GitHelpers.NormalizeBranchName(_options.Branch);

            if (!await UxHelpers.VerifyAndConfirmBranchExistsAsync(repoRemote, _options.Repository, normalizedBranch, !_options.Quiet))
            {
                Console.WriteLine("Aborting default channel deletion.");
                return Constants.ErrorCode;
            }

            List<DefaultChannelYamlData> defaultChannels = await GetConfiguration<DefaultChannelYamlData>(DefaultChannelConfigurationFileName, _options.ConfigurationBaseBranch);

            DefaultChannelYamlData? channelToRemove = defaultChannels.FirstOrDefault(c => 
                c.Repository == _options.Repository &&
                c.Branch == normalizedBranch &&
                c.Channel == _options.Channel);

            if (channelToRemove == null)
            {
                _logger.LogError("Could not find default channel for repository '{repository}', branch '{branch}', channel '{channel}'", 
                    _options.Repository, normalizedBranch, _options.Channel);
                return Constants.ErrorCode;
            }

            await CreateConfigurationBranchIfNeeded();

            defaultChannels.Remove(channelToRemove);

            _logger.LogInformation("Removing default channel for {repo} / {branch} from {fileName}", _options.Repository, normalizedBranch, DefaultChannelConfigurationFileName);
            await WriteConfigurationFile(DefaultChannelConfigurationFileName, defaultChannels, $"Removing default channel for '{_options.Repository} / {normalizedBranch}'");

            if (!_options.NoPr && (_options.Quiet || UxHelpers.PromptForYesNo($"Create PR with changes in {_options.ConfigurationRepository}?")))
            {
                await CreatePullRequest(
                    _options.ConfigurationRepository,
                    _options.ConfigurationBranch,
                    _options.ConfigurationBaseBranch,
                    $"Remove default channel for '{_options.Repository} / {normalizedBranch}'",
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
            _logger.LogError(e, "Error: Failed remove the default channel association.");
            return Constants.ErrorCode;
        }
    }
}
