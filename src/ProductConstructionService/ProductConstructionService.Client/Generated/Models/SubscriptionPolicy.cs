// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class SubscriptionPolicy
    {
        public SubscriptionPolicy(bool batchable, UpdateFrequency updateFrequency)
        {
            Batchable = batchable;
            UpdateFrequency = updateFrequency;
        }

        [JsonProperty("batchable")]
        public bool Batchable { get; set; }

        [JsonProperty("updateFrequency")]
        public UpdateFrequency UpdateFrequency { get; set; }

        [JsonProperty("mergePolicies")]
        public List<MergePolicy> MergePolicies { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (UpdateFrequency == default)
                {
                    return false;
                }
                return true;
            }
        }
    }
}
