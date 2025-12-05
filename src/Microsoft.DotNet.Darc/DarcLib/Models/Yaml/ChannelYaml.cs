// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class ChannelYaml : IComparable<ChannelYaml>, IExternallySyncedEntity<string>
{
    public string UniqueId => Name;

    [YamlMember(Alias = "Name", ApplyNamingConventions = false)]
    public required string Name { get; init; }

    [YamlMember(Alias = "Classification", ApplyNamingConventions = false,
        DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)]
    public required string Classification { get; init; }

    public static ChannelYaml FromClientModel(Channel channel) => new()
    {
        Name = channel.Name,
        Classification = channel.Classification,
    };

    /// <summary>
    /// Compares channels for sorting purposes.
    /// Order: Name
    /// </summary>
    public int CompareTo(ChannelYaml? other)
    {
        if (other is null) return 1;
        return string.Compare(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }
}
