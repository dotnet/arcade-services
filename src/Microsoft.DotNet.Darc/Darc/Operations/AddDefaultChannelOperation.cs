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

namespace Microsoft.DotNet.Darc.Operations;

internal class AddDefaultChannelOperation : ConfigurationManagementOperation
{
    private readonly AddDefaultChannelCommandLineOptions _options;
    private readonly ILogger<AddDefaultChannelOperation> _logger;
    private readonly IRemoteFactory _remoteFactory;

    public AddDefaultChannelOperation(
            AddDefaultChannelCommandLineOptions options,
            IGitRepoFactory gitRepoFactory,
            ILocalGitRepoFactory localGitRepoFactory,
            IRemoteFactory remoteFactory,
            ILogger<AddDefaultChannelOperation> logger)
        : base(options, gitRepoFactory, remoteFactory, logger, localGitRepoFactory)
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

            // Users can ignore the flag and pass in -regex: but to prevent typos we'll avoid that.
            _options.Branch = _options.UseBranchAsRegex
                ? $"-regex:{_options.Branch}"
                : GitHelpers.NormalizeBranchName(_options.Branch);

            if (!await UxHelpers.VerifyAndConfirmBranchExistsAsync(repoRemote, _options.Repository, _options.Branch, !_options.Quiet))
            {
                Console.WriteLine("Aborting default channel creation.");
                return Constants.ErrorCode;
            }

            List<DefaultChannelYamlData> defaultChannels = await GetConfiguration<DefaultChannelYamlData>(DefaultChannelConfigurationFileName, _options.ConfigurationBaseBranch);

            if (defaultChannels.Any(c => c.Repository == _options.Repository
                                         && c.Branch == _options.Branch
                                         && c.Channel == _options.Channel))
            {
                _logger.LogError("This default channel already exists");
                return Constants.ErrorCode;
            }

            bool openPr = string.IsNullOrEmpty(_options.ConfigurationBranch);

            await CreateConfigurationBranchIfNeeded();

            defaultChannels.Add(new DefaultChannelYamlData()
            {
                Repository = _options.Repository,
                Branch = _options.Branch,
                Channel = _options.Channel,
            });

            defaultChannels = [..defaultChannels.OrderBy(c => c.Repository).ThenBy(c => c.Branch).ThenBy(c => c.Channel)];

            _logger.LogInformation("Adding default channel for {repo} / {branch} to {fileName}", _options.Repository, _options.Branch, ChannelConfigurationFileName);
            await WriteConfigurationFile(DefaultChannelConfigurationFileName, defaultChannels, $"Adding default channel for '{_options.Repository} / {_options.Branch}'");

            if (!_options.NoPr && (_options.Quiet || UxHelpers.PromptForYesNo($"Create PR with changes in {_options.ConfigurationRepository}?")))
            {
                await CreatePullRequest(
                    _options.ConfigurationRepository,
                    _options.ConfigurationBranch,
                    _options.ConfigurationBaseBranch,
                    $"Add default channel for '{_options.Repository} / {_options.Branch}'",
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
            _logger.LogError(e, "Error: Failed to add a new default channel association.");
            return Constants.ErrorCode;
        }
    }
}
