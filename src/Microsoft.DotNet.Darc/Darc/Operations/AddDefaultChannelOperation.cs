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

            DefaultChannelYaml defaultChannelYaml = new()
            {
                Repository = _options.Repository,
                Branch = _options.Branch,
                Channel = _options.Channel,
                Enabled = true
            };

            await ValidateNoEquivalentDefaultChannel(defaultChannelYaml);

            await _configurationRepositoryManager.AddDefaultChannelAsync(
                        _options.ToConfigurationRepositoryOperationParameters(),
                        defaultChannelYaml);

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (MaestroConfiguration.Client.DuplicateConfigurationObjectException e)
        {
            _logger.LogError("Default channel with repository '{repo}', branch '{branch}', and channel '{channel}' already exists in '{filePath}' in repo {configRepo} on branch {configBranch}.",
                _options.Repository,
                _options.Branch,
                _options.Channel,
                e.FilePath,
                e.Repository,
                e.Branch);
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
        var existingDefaultChannel = (await _barClient.GetDefaultChannelsAsync(
                repository: defaultChannel.Repository,
                branch: defaultChannel.Branch,
                channel: defaultChannel.Channel))
            .FirstOrDefault();

        if (existingDefaultChannel != null)
        {
            _logger.LogError("A default channel with the same repository, branch, and channel already exists (ID: {id})",
                existingDefaultChannel.Id);
            throw new ArgumentException($"A default channel with the repository {existingDefaultChannel.Repository}, branch {existingDefaultChannel.Branch} and channel {existingDefaultChannel.Channel} already exists");
        }
    }
}
