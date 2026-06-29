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
        public EntityChangesSubscriptionYaml Subscriptions { get; set; }

        [JsonProperty("channels")]
        public EntityChangesChannelYaml Channels { get; set; }

        [JsonProperty("defaultChannels")]
        public EntityChangesDefaultChannelYaml DefaultChannels { get; set; }

        [JsonProperty("repositoryBranches")]
        public EntityChangesBranchMergePoliciesYaml RepositoryBranches { get; set; }
    }
}
