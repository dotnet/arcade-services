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

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

internal class DeleteChannelOperation : ConfigurationManagementOperation
{
    private readonly DeleteChannelCommandLineOptions _options;
    private readonly ILogger<DeleteChannelOperation> _logger;

    public DeleteChannelOperation(
            DeleteChannelCommandLineOptions options,
            IGitRepoFactory gitRepoFactory,
            IRemoteFactory remoteFactory,
            ILogger<DeleteChannelOperation> logger,
            ILocalGitRepoFactory localGitRepoFactory)
        : base(options, gitRepoFactory, remoteFactory, logger, localGitRepoFactory)
    {
        _options = options;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            List<ChannelYamlData> channels = await GetConfiguration<ChannelYamlData>(ChannelConfigurationFileName, _options.ConfigurationBaseBranch);
            ChannelYamlData? channel = channels.FirstOrDefault(c => c.Name == _options.Name);
            if (channel == null)
            {
                _logger.LogError("Could not find channel with name '{channelName}'", _options.Name);
                return Constants.ErrorCode;
            }

            bool openPr = string.IsNullOrEmpty(_options.ConfigurationBranch);

            await CreateConfigurationBranchIfNeeded();

            channels.Remove(channel);

            _logger.LogInformation("Removing channel '{channelName}' from {fileName}", _options.Name, ChannelConfigurationFileName);
            await WriteConfigurationFile(ChannelConfigurationFileName, channels, $"Removing channel '{_options.Name}'");

            if (!_options.NoPr && (_options.Quiet || UxHelpers.PromptForYesNo($"Create PR with changes in {_options.ConfigurationRepository}?")))
            {
                await CreatePullRequest(
                    _options.ConfigurationRepository,
                    _options.ConfigurationBranch,
                    _options.ConfigurationBaseBranch,
                    $"Remove channel '{_options.Name}'",
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
            _logger.LogError(e, "Error: Failed to delete channel.");
            return Constants.ErrorCode;
        }
    }
}
