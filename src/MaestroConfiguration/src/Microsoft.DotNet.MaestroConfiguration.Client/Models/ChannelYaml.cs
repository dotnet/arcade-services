// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.MaestroConfiguration.Client.Models;

public class ChannelYaml : IYamlModel
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

    public string GetUniqueId() => Name;

    public string GetDefaultFilePath() => $"channels/{Name}.yaml";

    public int Compare(object? x, object? y)
    {
        if (x is not ChannelYaml left)
        {
            return 1;
        }
        if (y is not ChannelYaml right)
        {
            return -1;
        }
        return string.Compare(left.Name, right.Name, StringComparison.Ordinal);
    }
}
