// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class DefaultChannelYaml : IComparable<DefaultChannelYaml>
{
    [YamlMember(Alias = "Repository", ApplyNamingConventions = false)]
    public required string Repository { get; init; }

    [YamlMember(Alias = "Branch", ApplyNamingConventions = false)]
    public required string Branch { get; init; }

    [YamlMember(Alias = "Channel", ApplyNamingConventions = false)]
    public required string Channel { get; init; }

    [YamlMember(Alias = "Enabled", ApplyNamingConventions = false)]
    public required bool Enabled { get; init; }

    /// <summary>
    /// Compares default channels for sorting purposes.
    /// Order: Channel, Branch
    /// </summary>
    public int CompareTo(DefaultChannelYaml? other)
    {
        if (other is null) return 1;

        int result = string.Compare(Channel, other.Channel, StringComparison.OrdinalIgnoreCase);
        if (result != 0) return result;

        return string.Compare(Branch, other.Branch, StringComparison.OrdinalIgnoreCase);
    }

    public static IComparer<DefaultChannelYaml> Comparer { get; } = Comparer<DefaultChannelYaml>.Create((x, y) => x?.CompareTo(y) ?? (y is null ? 0 : -1));
}
