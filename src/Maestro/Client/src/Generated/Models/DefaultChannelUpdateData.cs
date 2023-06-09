// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class DefaultChannelUpdateData
    {
        public DefaultChannelUpdateData()
        {
        }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("channelId")]
        public int? ChannelId { get; set; }

        [JsonProperty("enabled")]
        public bool? Enabled { get; set; }
    }
}
