// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class DeleteDefaultChannelOperation : UpdateDefaultChannelBaseOperation
{
    private readonly ILogger<DeleteDefaultChannelOperation> _logger;

    public DeleteDefaultChannelOperation(
        DeleteDefaultChannelCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<DeleteDefaultChannelOperation> logger)
        : base(options, barClient)
    {
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

            await _barClient.DeleteDefaultChannelAsync(resolvedChannel.Id);

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
