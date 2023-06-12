// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class BuildTime
    {
        public BuildTime()
        {
        }

        [JsonProperty("defaultChannelId")]
        public int? DefaultChannelId { get; set; }

        [JsonProperty("officialBuildTime")]
        public double? OfficialBuildTime { get; set; }

        [JsonProperty("prBuildTime")]
        public double? PrBuildTime { get; set; }

        [JsonProperty("goalTimeInMinutes")]
        public int? GoalTimeInMinutes { get; set; }
    }
}
