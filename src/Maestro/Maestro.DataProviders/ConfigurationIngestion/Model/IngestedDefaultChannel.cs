// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Model;

internal class IngestedDefaultChannel :
    IExternallySyncedEntity<(string Repository, string Branch, string Channel)>
{
    public IngestedDefaultChannel(DefaultChannelYaml values) => _values = values;

    public override (string, string, string) UniqueId => (_values.Repository, _values.Branch, _values.Channel);

    public DefaultChannelYaml _values { init; get; }

    public override string SerializedData => _yamlSerializer.Serialize(_values);

    public override string ToString()
    {
        return $"DefaultChannel (Repository: '{_values.Repository}', Branch: '{_values.Branch}', Channel: '{_values.Channel}')";
    }
}
