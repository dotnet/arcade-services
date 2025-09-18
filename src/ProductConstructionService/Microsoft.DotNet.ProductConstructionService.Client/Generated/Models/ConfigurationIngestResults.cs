// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class ConfigurationIngestResults
    {
        public ConfigurationIngestResults()
        {
        }

        [JsonProperty("subscriptions")]
        public Models.ConfigurationIngestResult Subscriptions { get; set; }

        [JsonProperty("channels")]
        public Models.ConfigurationIngestResult Channels { get; set; }

        [JsonProperty("defaultChannels")]
        public Models.ConfigurationIngestResult DefaultChannels { get; set; }
    }
}
