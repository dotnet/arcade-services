// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc.Yaml;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class AddChannelOperation : ChannelManagementOperation
{
    private readonly AddChannelCommandLineOptions _options;
    private readonly ILogger<AddChannelOperation> _logger;

    public AddChannelOperation(
            AddChannelCommandLineOptions options,
            IGitRepoFactory gitRepoFactory,
            IRemoteFactory remoteFactory,
            ILogger<AddChannelOperation> logger,
            ILocalGitRepoFactory localGitRepoFactory)
        : base(options, gitRepoFactory, remoteFactory, logger, localGitRepoFactory)
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

            // Check if channel already exists in any category file
            var existingChannel = await FindChannelInCategoryFiles(_options.Name, _options.ConfigurationBaseBranch);
            if (existingChannel != null)
            {
                _logger.LogError("An existing channel with name '{channelName}' already exists in category '{category}'", _options.Name, existingChannel.Category);
                return Constants.ErrorCode;
            }

            bool openPr = string.IsNullOrEmpty(_options.ConfigurationBranch);

            await CreateConfigurationBranchIfNeeded();

            // Create the new channel
            var newChannel = new ChannelYamlData()
            {
                Name = _options.Name,
                Classification = _options.Classification
            };

            // Add channel to the appropriate category file
            await AddChannelToCategoryFile(newChannel, $"Adding channel '{_options.Name}'");

            if (!_options.NoPr && (_options.Quiet || UxHelpers.PromptForYesNo($"Create PR with changes in {_options.ConfigurationRepository}?")))
            {
                await CreatePullRequest(
                    _options.ConfigurationRepository,
                    _options.ConfigurationBranch,
                    _options.ConfigurationBaseBranch,
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
