// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Model;

internal class IngestedSubscription : IExternallySyncedEntity<Guid>
{
    public IngestedSubscription(SubscriptionYaml values) => Values = values;

    public Guid UniqueId => Values.Id;

    public SubscriptionYaml Values { init; get; }

    public override string ToString()
    {
        return $"Subscription (Id: {Values.Id}, Channel: '{Values.Channel}', Source: '{Values.SourceRepository}', Target: '{Values.TargetRepository}', Branch: '{Values.TargetBranch}')";
    }
}
