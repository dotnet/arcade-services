using System;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public enum UpdateFrequency
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
    }
}
