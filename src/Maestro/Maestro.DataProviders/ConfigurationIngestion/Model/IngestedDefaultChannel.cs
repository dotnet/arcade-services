// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Model;

internal class IngestedDefaultChannel :
    IExternallySyncedEntity<(string Repository, string Branch, string Channel)>
{
    public IngestedDefaultChannel(DefaultChannelYaml values) => Values = values;

    public (string, string, string) UniqueId => (Values.Repository, Values.Branch, Values.Channel);

    public DefaultChannelYaml Values { init; get; }

    public override string ToString()
    {
        return $"DefaultChannel (Repository: '{Values.Repository}', Branch: '{Values.Branch}', Channel: '{Values.Channel}')";
    }
}
