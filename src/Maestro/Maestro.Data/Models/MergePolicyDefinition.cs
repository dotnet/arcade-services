// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;

namespace Maestro.Data.Models;

public class MergePolicyDefinition
{
    public string Name { get; set; }

    [CanBeNull]
    public Dictionary<string, JToken> Properties { get; set; }
}
