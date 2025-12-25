// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class DeleteDefaultChannelOperation : UpdateDefaultChannelBaseOperation
{
    private readonly DeleteDefaultChannelCommandLineOptions _options;
    private readonly IConfigurationRepositoryManager _configurationRepositoryManager;
    private readonly ILogger<DeleteDefaultChannelOperation> _logger;

    public DeleteDefaultChannelOperation(
        DeleteDefaultChannelCommandLineOptions options,
        IBarApiClient barClient,
        IConfigurationRepositoryManager configurationRepositoryManager,
        ILogger<DeleteDefaultChannelOperation> logger)
        : base(options, barClient)
    {
        _options = options;
        _configurationRepositoryManager = configurationRepositoryManager;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            DefaultChannel resolvedChannel = await ResolveSingleChannel();
            if (resolvedChannel == null)
            {
                return Constants.ErrorCode;
            }

            if (_options.ShouldUseConfigurationRepository)
            {
                var defaultChannelYaml = DefaultChannelYaml.FromClientModel(resolvedChannel);

                await _configurationRepositoryManager.DeleteDefaultChannelAsync(
                    _options.ToConfigurationRepositoryOperationParameters(),
                    defaultChannelYaml);
            }
            else
            {
                await _barClient.DeleteDefaultChannelAsync(resolvedChannel.Id);
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
