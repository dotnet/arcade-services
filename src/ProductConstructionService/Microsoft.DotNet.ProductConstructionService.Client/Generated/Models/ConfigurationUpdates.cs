// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class ConfigurationUpdates
    {
        public ConfigurationUpdates()
        {
        }

        [JsonProperty("subscriptions")]
        public Models.EntityChangesSubscriptionYaml Subscriptions { get; set; }

        [JsonProperty("channels")]
        public Models.EntityChangesChannelYaml Channels { get; set; }

        [JsonProperty("defaultChannels")]
        public Models.EntityChangesDefaultChannelYaml DefaultChannels { get; set; }

        [JsonProperty("repositoryBranches")]
        public Models.EntityChangesBranchMergePoliciesYaml RepositoryBranches { get; set; }
    }
}
