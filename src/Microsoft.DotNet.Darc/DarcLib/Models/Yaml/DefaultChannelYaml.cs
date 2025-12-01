// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using YamlDotNet.Serialization;

namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class DefaultChannelYaml
{
    [YamlMember(Alias = "Repository", ApplyNamingConventions = false)]
    public string Repository { get; set; }

    [YamlMember(Alias = "Branch", ApplyNamingConventions = false)]
    public string Branch { get; set; }

    [YamlMember(Alias = "ChannelId", ApplyNamingConventions = false)]
    public int ChannelId { get; set; }

    [YamlMember(Alias = "Enabled", ApplyNamingConventions = false)]
    public bool Enabled { get; set; }
}
