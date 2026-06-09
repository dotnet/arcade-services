// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class SubscriptionTriggerOutcome
    {
        public SubscriptionTriggerOutcome(Guid subscriptionId, int buildId, DateTimeOffset date, Models.OutcomeType type, string operationId, string message)
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
        public OutcomeType Type { get; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Type == default)
                {
                    return false;
                }
                return true;
            }
        }
    }
}
