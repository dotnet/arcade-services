// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public enum ClientUpdateFrequency
    {
        [EnumMember(Value = "none")]
        None,
        [EnumMember(Value = "everyDay")]
        EveryDay,
        [EnumMember(Value = "everyBuild")]
        EveryBuild,
        [EnumMember(Value = "twiceDaily")]
        TwiceDaily,
        [EnumMember(Value = "everyWeek")]
        EveryWeek,
        [EnumMember(Value = "everyTwoWeeks")]
        EveryTwoWeeks,
        [EnumMember(Value = "everyMonth")]
        EveryMonth,
    }
}
