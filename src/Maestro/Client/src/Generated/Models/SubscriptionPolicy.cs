// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class SubscriptionPolicy
    {
        public SubscriptionPolicy(bool batchable, Models.UpdateFrequency updateFrequency)
        {
            Batchable = batchable;
            UpdateFrequency = updateFrequency;
        }

        [JsonProperty("batchable")]
        public bool Batchable { get; set; }

        [JsonProperty("updateFrequency")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Models.UpdateFrequency UpdateFrequency { get; set; }

        [JsonProperty("mergePolicies")]
        public IImmutableList<Models.MergePolicy> MergePolicies { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (UpdateFrequency == default(Models.UpdateFrequency))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
