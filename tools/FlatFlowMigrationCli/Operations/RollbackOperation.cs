// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FlatFlowMigrationCli.Options;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;

namespace FlatFlowMigrationCli.Operations;

internal class RollbackOperation : IOperation
{
    private readonly IProductConstructionServiceApi _pcsClient;
    private readonly ILogger<RollbackOperation> _logger;
    private readonly RollbackOptions _options;

    public RollbackOperation(
        ILogger<RollbackOperation> logger,
        IProductConstructionServiceApi pcsClient,
        RollbackOptions options)
    {
        _logger = logger;
        _pcsClient = pcsClient;
        _options = options;
    }

    public async Task<int> RunAsync()
    {
        _logger.LogInformation("Starting rollback operation...");

        if (string.IsNullOrEmpty(_options.LogFilePath))
        {
            _logger.LogError("Log file path is not provided.");
            return 1;
        }

        if (!File.Exists(_options.LogFilePath))
        {
            _logger.LogError("Log file not found at {path}", _options.LogFilePath);
            return 1;
        }

        var migrationLog = await MigrationLogger.ReadLogAsync(_options.LogFilePath);

        foreach (var change in migrationLog)
        {
            if (change.Value.Action == Action.Disable)
            {

                //_logger.LogInformation("Enabling previously disabled subscription {subscriptionId}...", disabledSubscription.Id);
                //await _pcsClient.Subscriptions.EnableSubscriptionAsync(disabledSubscription.Id);
            }
        }

        _logger.LogInformation("Rollback operation completed successfully.");
        return 0;
    }
}
