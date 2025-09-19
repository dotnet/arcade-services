// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ProductConstructionService.Cli.Operations;

internal class ClearConfigurationOperation : IOperation
{
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<ClearConfigurationOperation> _logger;
    private readonly string _repoUri;
    private readonly string _branch;

    public ClearConfigurationOperation(
        IProductConstructionServiceApi client, 
        ILogger<ClearConfigurationOperation> logger,
        string repoUri,
        string branch)
    {
        _client = client;
        _logger = logger;
        _repoUri = repoUri;
        _branch = branch;
    }

    public async Task<int> RunAsync()
    {
        try
        {
            _logger.LogInformation("Clearing PCS configuration from repository '{repoUri}' branch '{branch}'", _repoUri, _branch);
            
            var result = await _client.Configuration.ClearConfigurationAsync(_branch, _repoUri);
            Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while clearing configuration");
            return 1;
        }
    }
}
