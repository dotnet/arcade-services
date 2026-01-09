// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Model;

internal class IngestedChannel : IExternallySyncedEntity<string>
{
    public IngestedChannel(ChannelYaml values) => _values = values;

    public override string UniqueId => _values.Name;

    public ChannelYaml _values { init; get; }

    public override string SerializedData => _yamlSerializer.Serialize(_values);

    public override string ToString()
    {
        return $"Channel (Name: '{_values.Name}')";
    }
}
