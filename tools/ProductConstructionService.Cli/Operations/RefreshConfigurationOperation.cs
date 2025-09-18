// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ProductConstructionService.Cli.Operations;

internal class RefreshConfigurationOperation : IOperation
{
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<RefreshConfigurationOperation> _logger;
    private readonly string _repoUri;
    private readonly string _branch;

    public RefreshConfigurationOperation(
        IProductConstructionServiceApi client, 
        ILogger<RefreshConfigurationOperation> logger,
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
            _logger.LogInformation("Refreshing PCS configuration from repository '{repoUri}' branch '{branch}'", _repoUri, _branch);
            
            var result = await _client.Configuration.RefreshConfigurationAsync(_branch, _repoUri);
            Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while refreshing configuration");
            return 1;
        }
    }
}
