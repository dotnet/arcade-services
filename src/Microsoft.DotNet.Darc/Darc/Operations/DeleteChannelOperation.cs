// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

internal class DeleteChannelOperation : ChannelManagementOperation
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
            // Find the channel across all category files
            ChannelWithCategory? channelWithCategory = await FindChannelInCategoryFiles(_options.Name, _options.ConfigurationBaseBranch);
            if (channelWithCategory == null)
            {
                _logger.LogError("Could not find channel with name '{channelName}' in any category file", _options.Name);
                return Constants.ErrorCode;
            }

            bool openPr = string.IsNullOrEmpty(_options.ConfigurationBranch);

            await CreateConfigurationBranchIfNeeded();

            // Remove channel from its category file
            await RemoveChannelFromCategoryFile(channelWithCategory, $"Removing channel '{_options.Name}'");

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
