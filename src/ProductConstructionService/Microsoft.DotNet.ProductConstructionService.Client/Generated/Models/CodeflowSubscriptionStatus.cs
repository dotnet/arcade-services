// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class CodeflowSubscriptionStatus
    {
        public CodeflowSubscriptionStatus()
        {
        }

        [JsonProperty("subscription")]
        public Subscription Subscription { get; set; }

        [JsonProperty("activePullRequest")]
        public TrackedPullRequest ActivePullRequest { get; set; }

        [JsonProperty("newerBuildsAvailable")]
        public int? NewerBuildsAvailable { get; set; }

        [JsonProperty("newestBuildDate")]
        public DateTimeOffset? NewestBuildDate { get; set; }
    }
}
