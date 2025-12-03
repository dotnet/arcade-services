// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class ChannelYaml
{
    [YamlMember(Alias = "Name", ApplyNamingConventions = false)]
    public required string Name { get; init; }

    [YamlMember(Alias = "Classification", ApplyNamingConventions = false,
        DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)]
    public required string Classification { get; init; }
}
