// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Newtonsoft.Json.Linq;

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class MergePolicy
{
    public MergePolicy()
    {
    }

    public MergePolicy(MergePolicyDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Name = other.Name;
        Properties = new(other.Properties ?? []);
    }

    public string Name { get; set; }

    public Dictionary<string, JToken> Properties { get; set; }

    public MergePolicyDefinition ToDb()
    {
        return new MergePolicyDefinition
        {
            Name = Name,
            Properties = Properties?.ToDictionary(p => p.Key, p => p.Value)
        };
    }
}
