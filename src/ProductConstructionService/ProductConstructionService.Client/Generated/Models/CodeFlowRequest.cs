// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class CodeFlowRequest
    {
        public CodeFlowRequest(Guid subscriptionId, int buildId)
        {
            SubscriptionId = subscriptionId;
            BuildId = buildId;
        }

        [JsonProperty("subscriptionId")]
        public Guid SubscriptionId { get; set; }

        [JsonProperty("buildId")]
        public int BuildId { get; set; }

        [JsonProperty("prBranch")]
        public string PrBranch { get; set; }

        [JsonProperty("prUrl")]
        public string PrUrl { get; set; }
    }
}
