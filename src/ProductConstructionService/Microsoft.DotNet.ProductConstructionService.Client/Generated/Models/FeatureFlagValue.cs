using System;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class FeatureFlagValue
    {
        public FeatureFlagValue(Guid subscriptionId)
        {
            SubscriptionId = subscriptionId;
        }

        [JsonProperty("subscriptionId")]
        public Guid SubscriptionId { get; set; }

        [JsonProperty("flagName")]
        public string FlagName { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("createdAt")]
        public DateTimeOffset? CreatedAt { get; set; }
    }
}
