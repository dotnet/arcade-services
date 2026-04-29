// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class SetGoalOperation : Operation
{
    private readonly SetGoalCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<SetGoalOperation> _logger;

    public SetGoalOperation(
        SetGoalCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<SetGoalOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    ///  Sets Goal in minutes for a definition in a Channel.
    /// </summary>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            Goal goalInfo = await _barClient.SetGoalAsync(_options.Channel, _options.DefinitionId, _options.Minutes);
            Console.Write(goalInfo.Minutes);
            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (RestApiException e) when (e.Response.Status == (int) HttpStatusCode.NotFound)
        {
            _logger.LogError(e, $"Cannot find Channel '{_options.Channel}'.");
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Unable to create goal for Channel : '{_options.Channel}' and DefinitionId : '{_options.DefinitionId}'.");
            return Constants.ErrorCode;
        }
    }
}
