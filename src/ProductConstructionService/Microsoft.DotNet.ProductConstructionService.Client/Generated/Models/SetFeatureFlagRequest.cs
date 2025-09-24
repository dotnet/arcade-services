// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class SetFeatureFlagRequest
    {
        public SetFeatureFlagRequest(Guid subscriptionId)
        {
            SubscriptionId = subscriptionId;
        }

        [JsonProperty("subscriptionId")]
        public Guid SubscriptionId { get; set; }

        [JsonProperty("flagName")]
        public string FlagName { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("expiryDays")]
        public int? ExpiryDays { get; set; }
    }
}
