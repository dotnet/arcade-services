// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class DefaultChannelYaml
{
    [YamlMember(Alias = "Repository", ApplyNamingConventions = false)]
    public required string Repository { get; init; }

    [YamlMember(Alias = "Branch", ApplyNamingConventions = false)]
    public required string Branch { get; init; }

    [YamlMember(Alias = "Channel", ApplyNamingConventions = false)]
    public required string Channel { get; init; }

    [YamlMember(Alias = "Enabled", ApplyNamingConventions = false)]
    public required bool Enabled { get; init; }
}
