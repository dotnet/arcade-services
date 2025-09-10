// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public enum FeatureFlagType
    {
        [EnumMember(Value = "boolean")]
        Boolean,
        [EnumMember(Value = "string")]
        String,
        [EnumMember(Value = "integer")]
        Integer,
        [EnumMember(Value = "double")]
        Double,
    }
}
