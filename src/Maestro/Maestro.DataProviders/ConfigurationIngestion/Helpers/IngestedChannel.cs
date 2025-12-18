// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Helpers;

internal class IngestedChannel : IExternallySyncedEntity<string>
{
    public IngestedChannel(ChannelYaml values) => Values = values;

    public string UniqueId => Values.Name;

    public ChannelYaml Values { init; get; }
}
