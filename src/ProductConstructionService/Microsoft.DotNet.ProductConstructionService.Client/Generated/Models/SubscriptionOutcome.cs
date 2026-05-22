using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class SubscriptionOutcome
    {
        public SubscriptionOutcome(Guid subscriptionId, int buildId, DateTimeOffset date, Models.SubscriptionOutcomeType type, string operationId, string message)
        {
            SubscriptionId = subscriptionId;
            BuildId = buildId;
            Date = date;
            Type = type;
            OperationId = operationId;
            Message = message;
        }

        [JsonProperty("operationId")]
        public string OperationId { get; }

        [JsonProperty("subscriptionId")]
        public Guid SubscriptionId { get; }

        [JsonProperty("buildId")]
        public int BuildId { get; }

        [JsonProperty("date")]
        public DateTimeOffset Date { get; }

        [JsonProperty("message")]
        public string Message { get; }

        [JsonProperty("type")]
        public Models.SubscriptionOutcomeType Type { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Type == default(Models.SubscriptionOutcomeType))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
