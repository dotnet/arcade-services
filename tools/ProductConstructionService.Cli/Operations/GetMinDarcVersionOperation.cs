// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Tools.Cli.Core;

namespace ProductConstructionService.Cli.Operations;

internal class GetMinDarcVersionOperation : IOperation
{
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<GetMinDarcVersionOperation> _logger;

    public GetMinDarcVersionOperation(IProductConstructionServiceApi client, ILogger<GetMinDarcVersionOperation> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<int> RunAsync()
    {
        try
        {
            var version = await _client.MinDarcVersion.GetMinDarcVersionAsync();
            if (string.IsNullOrWhiteSpace(version))
            {
                _logger.LogInformation("Minimum darc client version is not set.");
            }
            else
            {
                _logger.LogInformation("Minimum darc client version: {version}", version);
            }
            return 0;
        }
        catch (RestApiException<ApiError> ex) when ((int)ex.Response.Status == 204)
        {
            _logger.LogInformation("Minimum darc client version is not set.");
            return 0;
        }
    }
}
