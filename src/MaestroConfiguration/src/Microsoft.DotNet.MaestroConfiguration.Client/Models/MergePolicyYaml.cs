// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.MaestroConfiguration.Client.Models;

public class MergePolicyYaml
{
    [YamlMember(Alias = "Name")]
    public required string Name { get; init; }

    [YamlMember(Alias = "Properties")]
    public Dictionary<string, object> Properties { get; init; } = [];

    public static MergePolicyYaml FromClientModel(MergePolicy policy) => new()
    {
        Name = policy.Name,
        Properties = policy.Properties != null
            ? policy.Properties.ToDictionary(
                p => p.Key,
                p => p.Value.Type switch
                {
                    JTokenType.Array => (object)p.Value.ToObject<List<object>>()!,
                    _ => throw new NotImplementedException($"Unexpected property value type {p.Value.Type}")
                })
            : []
    };

    public static List<MergePolicyYaml> FromClientModels(IEnumerable<MergePolicy>? mergePolicies)
        => mergePolicies?.Select(FromClientModel).ToList() ?? [];
}
