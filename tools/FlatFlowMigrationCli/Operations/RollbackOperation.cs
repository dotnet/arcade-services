// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FlatFlowMigrationCli.Options;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;
using Tools.Common;

namespace FlatFlowMigrationCli.Operations;

internal class RollbackOperation : Operation
{
    private readonly IProductConstructionServiceApi _pcsClient;
    private readonly ISubscriptionMigrator _subscriptionMigrator;
    private readonly ILogger<RollbackOperation> _logger;
    private readonly RollbackOptions _options;

    public RollbackOperation(
        ILogger<RollbackOperation> logger,
        IProductConstructionServiceApi pcsClient,
        RollbackOptions options,
        ISubscriptionMigrator subscriptionMigrator)
    {
        _logger = logger;
        _pcsClient = pcsClient;
        _options = options;
        _subscriptionMigrator = subscriptionMigrator;
    }

    public override async Task<int> RunAsync()
    {
        ConfirmOperation("This is not a dry run, changes to subscriptions will be made. Continue");

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
                var subscription = await _pcsClient.Subscriptions.GetSubscriptionAsync(Guid.Parse(change.Value.Id!));
                await _subscriptionMigrator.EnableSubscriptionAsync(subscription);
            }
        }

        var codeflowSubscriptions = await _pcsClient.Subscriptions.ListSubscriptionsAsync(sourceEnabled: true);
        foreach (var subscription in codeflowSubscriptions)
        {
            if (subscription.SourceRepository == Constants.VmrUri || subscription.TargetRepository == Constants.VmrUri)
            {
                await _subscriptionMigrator.DisableSubscriptionAsync(subscription);
            }
        }

        _logger.LogInformation("Rollback operation completed successfully.");
        return 0;
    }
}
