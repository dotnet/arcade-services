// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Client;

namespace ProductConstructionService.Cli.Operations;
internal class StopPcsOperation : IOperation
{
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<GetPcsStatusOperation> _logger;

    public StopPcsOperation(ILogger<GetPcsStatusOperation> logger, IProductConstructionServiceApi client)
    {
        _logger = logger;
        _client = client;
    }

    public async Task<int> RunAsync()
    {
        var statuses = await _client.Status.StopPcsWorkItemProcessorsAsync();
        foreach (var stat in statuses)
        {
            _logger.LogInformation("Replica {replica} has status {status}", stat.Key, stat.Value);
        }
        return 0;
    }
}
