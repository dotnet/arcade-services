// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class TrackedPullRequest
    {
        public TrackedPullRequest(bool sourceEnabled, DateTimeOffset lastUpdate, DateTimeOffset lastCheck)
        {
            SourceEnabled = sourceEnabled;
            LastUpdate = lastUpdate;
            LastCheck = lastCheck;
        }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("channel")]
        public Models.Channel Channel { get; set; }

        [JsonProperty("targetBranch")]
        public string TargetBranch { get; set; }

        [JsonProperty("sourceEnabled")]
        public bool SourceEnabled { get; set; }

        [JsonProperty("lastUpdate")]
        public DateTimeOffset LastUpdate { get; set; }

        [JsonProperty("lastCheck")]
        public DateTimeOffset LastCheck { get; set; }

        [JsonProperty("nextCheck")]
        public DateTimeOffset? NextCheck { get; set; }

        [JsonProperty("updates")]
        public List<PullRequestUpdate> Updates { get; set; }
    }
}
