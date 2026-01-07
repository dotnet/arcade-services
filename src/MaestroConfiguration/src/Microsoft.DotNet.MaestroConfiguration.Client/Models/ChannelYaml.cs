// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.MaestroConfiguration.Client.Models;

public record ChannelYaml : IYamlModel
{
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
}
