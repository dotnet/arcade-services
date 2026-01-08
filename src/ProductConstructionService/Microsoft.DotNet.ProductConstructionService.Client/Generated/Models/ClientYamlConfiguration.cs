// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class ClientYamlConfiguration
    {
        public ClientYamlConfiguration()
        {
        }

        [JsonProperty("subscriptions")]
        public IImmutableList<Models.ClientSubscriptionYaml> Subscriptions { get; set; }

        [JsonProperty("channels")]
        public IImmutableList<Models.ClientChannelYaml> Channels { get; set; }

        [JsonProperty("defaultChannels")]
        public IImmutableList<Models.ClientDefaultChannelYaml> DefaultChannels { get; set; }

        [JsonProperty("branchMergePolicies")]
        public IImmutableList<Models.ClientBranchMergePoliciesYaml> BranchMergePolicies { get; set; }
    }
}
