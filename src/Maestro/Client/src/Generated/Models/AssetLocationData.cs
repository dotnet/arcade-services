// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class AssetLocationData
    {
        public AssetLocationData(Models.LocationType type)
        {
            Type = type;
        }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("type")]
        public Models.LocationType Type { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Type == default(Models.LocationType))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
