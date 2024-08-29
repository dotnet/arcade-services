// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;

namespace ProductConstructionService.Client.Models
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
