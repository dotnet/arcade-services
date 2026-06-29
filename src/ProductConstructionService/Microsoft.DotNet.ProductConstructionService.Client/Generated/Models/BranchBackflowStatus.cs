// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class BranchBackflowStatus
    {
        public BranchBackflowStatus(string branch, int defaultChannelId, List<SubscriptionBackflowStatus> subscriptionStatuses)
        {
            Branch = branch;
            DefaultChannelId = defaultChannelId;
            SubscriptionStatuses = subscriptionStatuses;
        }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("defaultChannelId")]
        public int DefaultChannelId { get; set; }

        [JsonProperty("subscriptionStatuses")]
        public List<SubscriptionBackflowStatus> SubscriptionStatuses { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(Branch))
                {
                    return false;
                }
                if (SubscriptionStatuses == default(List<SubscriptionBackflowStatus>))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
