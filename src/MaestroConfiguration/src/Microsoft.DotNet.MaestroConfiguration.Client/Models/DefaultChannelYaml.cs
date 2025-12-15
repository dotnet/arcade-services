// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.MaestroConfiguration.Client.Models;

public class DefaultChannelYaml : IYamlModel
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
}
