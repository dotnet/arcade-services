// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Maestro.Data;

public enum RunningService
{
    Maestro,
    PCS
}

public class SubscriptionIdGenerator(RunningService runningService)
{
    private readonly RunningService _runningService = runningService;

    private const string PcsSubscriptionIdPrefix = "00000000";

    public Guid GenerateSubscriptionId()
    {
        if (_runningService == RunningService.PCS)
        {
            return Guid.Parse($"{PcsSubscriptionIdPrefix}{Guid.NewGuid().ToString().Substring(PcsSubscriptionIdPrefix.Length)}");
        }

        var guid = Guid.NewGuid();

        if (guid.ToString().StartsWith(PcsSubscriptionIdPrefix))
        {
            return GenerateSubscriptionId();
        }

        return guid;
    }

    public bool ShouldTriggerSubscription(Guid subscriptionId)
    {
        bool startsWithPcsSubscriptionIdPrefix = subscriptionId.ToString().StartsWith(PcsSubscriptionIdPrefix);
        bool runningServiceIsPCS = _runningService == RunningService.PCS;
        return runningServiceIsPCS == startsWithPcsSubscriptionIdPrefix;
    }
}
