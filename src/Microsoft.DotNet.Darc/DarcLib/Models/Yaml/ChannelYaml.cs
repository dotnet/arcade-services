// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class ChannelYaml : IComparable<ChannelYaml>
{
    [YamlMember(Alias = "Name", ApplyNamingConventions = false)]
    public required string Name { get; init; }

    [YamlMember(Alias = "Classification", ApplyNamingConventions = false,
        DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)]
    public required string Classification { get; init; }

    /// <summary>
    /// Compares channels for sorting purposes.
    /// Order: Name
    /// </summary>
    public int CompareTo(ChannelYaml? other)
    {
        if (other is null) return 1;
        return string.Compare(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public static IComparer<ChannelYaml> Comparer { get; } = Comparer<ChannelYaml>.Create((x, y) => x?.CompareTo(y) ?? (y is null ? 0 : -1));
}
