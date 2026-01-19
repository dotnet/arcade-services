// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class ClientYamlConfiguration
    {
        public ClientYamlConfiguration()
        {
        }

        [JsonProperty("subscriptions")]
        public List<ClientSubscriptionYaml> Subscriptions { get; set; }

        [JsonProperty("channels")]
        public List<ClientChannelYaml> Channels { get; set; }

        [JsonProperty("defaultChannels")]
        public List<ClientDefaultChannelYaml> DefaultChannels { get; set; }

        [JsonProperty("branchMergePolicies")]
        public List<ClientBranchMergePoliciesYaml> BranchMergePolicies { get; set; }
    }
}
