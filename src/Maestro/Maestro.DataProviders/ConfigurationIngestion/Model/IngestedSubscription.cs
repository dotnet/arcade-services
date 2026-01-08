// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Model;

internal class IngestedSubscription : IExternallySyncedEntity<Guid>
{
    public IngestedSubscription(SubscriptionYaml values) => _values = values;

    public override Guid UniqueId => _values.Id;

    public SubscriptionYaml _values { init; get; }

    public override IYamlModel Values => _values;

    public override string ToString()
    {
        return $"Subscription (Id: {_values.Id}, Channel: '{_values.Channel}', Source: '{_values.SourceRepository}', Target: '{_values.TargetRepository}', Branch: '{_values.TargetBranch}')";
    }
}
