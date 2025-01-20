// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class PullRequestUpdate
    {
        public PullRequestUpdate(Guid subscriptionId, int buildId)
        {
            SubscriptionId = subscriptionId;
            BuildId = buildId;
        }

        [JsonProperty("sourceRepository")]
        public string SourceRepository { get; set; }

        [JsonProperty("subscriptionId")]
        public Guid SubscriptionId { get; set; }

        [JsonProperty("buildId")]
        public int BuildId { get; set; }
    }
}
