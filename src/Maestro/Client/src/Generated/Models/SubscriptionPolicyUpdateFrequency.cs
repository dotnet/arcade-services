using System;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public enum SubscriptionPolicyUpdateFrequency
    {
        [EnumMember(Value = "none")]
        None,
        [EnumMember(Value = "everyDay")]
        EveryDay,
        [EnumMember(Value = "everyBuild")]
        EveryBuild,
    }
}
