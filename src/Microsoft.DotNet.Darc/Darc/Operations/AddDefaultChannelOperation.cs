// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using IConfigurationRepositoryManager = Microsoft.DotNet.MaestroConfiguration.Client.IConfigurationRepositoryManager;

namespace Microsoft.DotNet.Darc.Operations;

internal class AddDefaultChannelOperation : Operation
{
    private readonly AddDefaultChannelCommandLineOptions _options;
    private readonly ILogger<AddDefaultChannelOperation> _logger;
    private readonly IBarApiClient _barClient;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly IConfigurationRepositoryManager _configurationRepositoryManager;

    public AddDefaultChannelOperation(
        AddDefaultChannelCommandLineOptions options,
        ILogger<AddDefaultChannelOperation> logger,
        IBarApiClient barClient,
        IRemoteFactory remoteFactory,
        IGitRepoFactory gitRepoFactory,
        IConfigurationRepositoryManager configurationRepositoryManager)
    {
        _options = options;
        _logger = logger;
        _barClient = barClient;
        _remoteFactory = remoteFactory;
        _gitRepoFactory = gitRepoFactory;
        _configurationRepositoryManager = configurationRepositoryManager;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IRemote repoRemote = await _remoteFactory.CreateRemoteAsync(_options.Repository);

            // Users can ignore the flag and pass in -regex: but to prevent typos we'll avoid that.
            _options.Branch = _options.UseBranchAsRegex ? $"-regex:{_options.Branch}" : GitHelpers.NormalizeBranchName(_options.Branch);

            if (!(await UxHelpers.VerifyAndConfirmBranchExistsAsync(repoRemote, _options.Repository, _options.Branch, !_options.NoConfirmation)))
            {
                Console.WriteLine("Aborting default channel creation.");
                return Constants.ErrorCode;
            }

            if (_options.ShouldUseConfigurationRepository)
            {
                DefaultChannelYaml defaultChannelYaml = new()
                {
                    Repository = _options.Repository,
                    Branch = _options.Branch,
                    Channel = _options.Channel,
                    Enabled = _options.Enabled
                };

                await ValidateNoEquivalentDefaultChannel(defaultChannelYaml);

                await _configurationRepositoryManager.AddDefaultChannelAsync(
                    _options.ToConfigurationRepositoryOperationParameters(),
                    defaultChannelYaml);
            }
            else
            {
                await _barClient.AddDefaultChannelAsync(_options.Repository, _options.Branch, _options.Channel);
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
            _logger.LogError(e, "Error: Failed to add a new default channel association.");
            return Constants.ErrorCode;
        }
    }

    /// <summary>
    /// Validates that no equivalent default channel already exists in BAR or YAML files.
    /// </summary>
    private async Task ValidateNoEquivalentDefaultChannel(DefaultChannelYaml defaultChannel)
    {
        // Check BAR for existing default channels with same repo and branch
        var existingDefaultChannels = await _barClient.GetDefaultChannelsAsync(
            repository: defaultChannel.Repository,
            branch: defaultChannel.Branch,
            channel: null);

        var equivalentInBar = existingDefaultChannels.FirstOrDefault(dc =>
            string.Equals(dc.Repository, defaultChannel.Repository, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(dc.Branch, defaultChannel.Branch, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(dc.Channel.Name, defaultChannel.Channel, StringComparison.OrdinalIgnoreCase));

        if (equivalentInBar != null)
        {
            _logger.LogError("A default channel with the same repository, branch, and channel already exists (ID: {id})",
                equivalentInBar.Id);
            throw new MaestroConfiguration.Client.DuplicateConfigurationObjectException(null);
        }
    }
}
