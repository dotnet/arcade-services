// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.Yaml;


#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Helpers;

public class IngestedChannel : IExternallySyncedEntity<string>
{
    internal IngestedChannel(ChannelYaml values) => Values = values;

    public string UniqueId => Values.Name;

    internal ChannelYaml Values { init; get; }
}
