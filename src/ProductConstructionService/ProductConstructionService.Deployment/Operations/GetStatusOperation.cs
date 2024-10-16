// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Client;

namespace ProductConstructionService.Deployment.Operations;
internal class GetStatusOperation : IOperation
{
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<GetStatusOperation> _logger;

    public GetStatusOperation(IProductConstructionServiceApi client, ILogger<GetStatusOperation> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<bool> RunAsync()
    {
        var statuses = await _client.Status.GetPcsWorkItemProcessorStatusAsync();
        foreach (var stat in statuses)
        {
            _logger.LogInformation("Replica {replica} has status {status}", stat.Key, stat.Value);
        }
        return true;
    }
}
