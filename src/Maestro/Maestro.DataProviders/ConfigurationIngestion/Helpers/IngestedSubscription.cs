// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.DarcLib.Models.Yaml;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Helpers;

public class IngestedSubscription : IExternallySyncedEntity<Guid>
{
    internal IngestedSubscription(SubscriptionYaml values) => Values = values;

    public Guid UniqueId => Values.Id;

    internal SubscriptionYaml Values { init; get; }
}
