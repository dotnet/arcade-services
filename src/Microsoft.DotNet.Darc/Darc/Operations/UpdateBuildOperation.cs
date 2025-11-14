// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class UpdateBuildOperation : Operation
{
    private readonly UpdateBuildCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<UpdateBuildOperation> _logger;

    public UpdateBuildOperation(
        UpdateBuildCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<UpdateBuildOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        if (!(_options.Released ^ _options.NotReleased))
        {
            Console.WriteLine("Please specify either --released or --not-released.");
            return Constants.ErrorCode;
        }

        try
        {
            var updatedBuild = await _barClient.UpdateBuildAsync(_options.Id, new BuildUpdate { Released = _options.Released });

            Console.WriteLine($"Updated build {_options.Id} with new information.");
            Console.WriteLine(UxHelpers.GetTextBuildDescription(updatedBuild));
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Error: Failed to update build with id '{_options.Id}'");
            return Constants.ErrorCode;
        }

        return Constants.SuccessCode;
    }
}
