using System;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public enum AssetLocationDataType
    {
        [EnumMember(Value = "none")]
        None,
        [EnumMember(Value = "nugetFeed")]
        NugetFeed,
        [EnumMember(Value = "container")]
        Container,
    }
}
