using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class SubscriptionPolicy
    {
        public SubscriptionPolicy(bool batchable, SubscriptionPolicyUpdateFrequency updateFrequency)
        {
            Batchable = batchable;
            UpdateFrequency = updateFrequency;
        }

        [JsonProperty("batchable")]
        public bool Batchable { get; set; }

        [JsonProperty("updateFrequency")]
        public SubscriptionPolicyUpdateFrequency UpdateFrequency { get; set; }

        [JsonProperty("mergePolicies")]
        public IImmutableList<MergePolicy> MergePolicies { get; set; }

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
