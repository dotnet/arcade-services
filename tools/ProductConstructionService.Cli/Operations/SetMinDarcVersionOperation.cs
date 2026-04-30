// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Cli.Options;
using Tools.Cli.Core;

namespace ProductConstructionService.Cli.Operations;

internal class SetMinDarcVersionOperation : IOperation
{
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<SetMinDarcVersionOperation> _logger;
    private readonly SetMinDarcVersionOptions _options;

    public SetMinDarcVersionOperation(
        IProductConstructionServiceApi client,
        ILogger<SetMinDarcVersionOperation> logger,
        SetMinDarcVersionOptions options)
    {
        _client = client;
        _logger = logger;
        _options = options;
    }

    public async Task<int> RunAsync()
    {
        await _client.MinDarcVersion.SetMinDarcVersionAsync(_options.Version);
        _logger.LogInformation("Minimum darc client version set to {version}", _options.Version);
        return 0;
    }
}
