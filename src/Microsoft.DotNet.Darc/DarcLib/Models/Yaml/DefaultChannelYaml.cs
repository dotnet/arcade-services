// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class DefaultChannelYaml :
    IComparable<DefaultChannelYaml>
{

    [YamlMember(Alias = "Repository", ApplyNamingConventions = false)]
    public required string Repository { get; init; }

    [YamlMember(Alias = "Branch", ApplyNamingConventions = false)]
    public required string Branch { get; init; }

    [YamlMember(Alias = "Channel", ApplyNamingConventions = false)]
    public required string Channel { get; init; }

    [YamlMember(Alias = "Enabled", ApplyNamingConventions = false)]
    public required bool Enabled { get; init; }

    public static DefaultChannelYaml FromClientModel(DefaultChannel defaultChannel) => new()
    {
        Repository = defaultChannel.Repository,
        Branch = defaultChannel.Branch,
        Channel = defaultChannel.Channel.Name,
        Enabled = defaultChannel.Enabled,
    };

    /// <summary>
    /// Compares default channels for sorting purposes.
    /// Order: Branch (main, master, release/*, internal/release/*, then alphabetically), Channel
    /// </summary>
    public int CompareTo(DefaultChannelYaml? other)
    {
        if (other is null) return 1;

        int result = BranchOrderComparer.Compare(Branch, other.Branch);
        if (result != 0) return result;

        return string.Compare(Channel, other.Channel, StringComparison.OrdinalIgnoreCase);
    }
}
