// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using Tools.Cli.Core;

namespace ProductConstructionService.Cli.Operations;

internal class ClearMinDarcVersionOperation : IOperation
{
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<ClearMinDarcVersionOperation> _logger;

    public ClearMinDarcVersionOperation(IProductConstructionServiceApi client, ILogger<ClearMinDarcVersionOperation> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<int> RunAsync()
    {
        await _client.MinDarcVersion.ClearMinDarcVersionAsync();
        _logger.LogInformation("Minimum darc client version cleared.");
        return 0;
    }
}
