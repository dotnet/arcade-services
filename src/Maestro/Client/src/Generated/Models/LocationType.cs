// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public enum LocationType
    {
        [EnumMember(Value = "none")]
        None,
        [EnumMember(Value = "nugetFeed")]
        NugetFeed,
        [EnumMember(Value = "container")]
        Container,
    }
}
