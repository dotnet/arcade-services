// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Linq;
using Maestro.Data.Models;
using Newtonsoft.Json.Linq;

namespace Maestro.Api.Model.v2018_07_16;

public class MergePolicy
{
    public MergePolicy()
    {
    }

    public MergePolicy(MergePolicyDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Name = other.Name;
        Properties = other.Properties?.ToImmutableDictionary();
    }

    public string Name { get; set; }

    public IImmutableDictionary<string, JToken> Properties { get; set; }

    public MergePolicyDefinition ToDb()
    {
        return new MergePolicyDefinition
        {
            Name = Name,
            Properties = Properties?.ToDictionary(p => p.Key, p => p.Value)
        };
    }
}
