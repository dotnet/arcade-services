// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class PullRequestCheckProcessor : DependencyFlowUpdateProcessor<PullRequestCheck>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory;

    public PullRequestCheckProcessor(
            IPullRequestUpdaterFactory updaterFactory,
            IRedisMutex redisMutex,
            IReminderManagerFactory reminderFactory,
            TelemetryClient telemetryClient,
            ILogger<PullRequestCheckProcessor> logger)
        : base(redisMutex, telemetryClient, logger)
    {
        _updaterFactory = updaterFactory;
    }

    protected override async Task<bool> ProcessUpdateAsync(
        PullRequestCheck workItem,
        CancellationToken cancellationToken)
    {
        var updater = _updaterFactory.CreatePullRequestUpdater(PullRequestUpdaterId.Parse(workItem.UpdaterId));
        return await updater.CheckPullRequestAsync(workItem);
    }

    protected override Dictionary<string, object> GetLoggingScopeData(PullRequestCheck workItem)
    {
        var data = base.GetLoggingScopeData(workItem);
        data["PrUrl"] = workItem.Url;
        return data;
    }
}
