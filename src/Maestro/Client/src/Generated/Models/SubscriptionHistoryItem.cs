using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class SubscriptionHistoryItem
    {
        public SubscriptionHistoryItem(DateTimeOffset timestamp, bool success, Guid subscriptionId, string errorMessage, string action, string retryUrl)
        {
            Timestamp = timestamp;
            Success = success;
            SubscriptionId = subscriptionId;
            ErrorMessage = errorMessage;
            Action = action;
            RetryUrl = retryUrl;
        }

        [JsonProperty("timestamp")]
        public DateTimeOffset Timestamp { get; }

        [JsonProperty("errorMessage")]
        public string ErrorMessage { get; }

        [JsonProperty("success")]
        public bool Success { get; }

        [JsonProperty("subscriptionId")]
        public Guid SubscriptionId { get; }

        [JsonProperty("action")]
        public string Action { get; }

        [JsonProperty("retryUrl")]
        public string RetryUrl { get; }
    }
}
