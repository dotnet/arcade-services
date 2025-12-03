// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class MergePolicyYaml
{
    [YamlMember(Alias = "Name")]
    public required string Name { get; init; }

    [YamlMember(Alias = "Properties")]
    public Dictionary<string, object> Properties { get; init; } = [];
}
