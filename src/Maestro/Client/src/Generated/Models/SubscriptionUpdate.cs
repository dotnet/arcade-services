// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class SubscriptionUpdate
    {
        public SubscriptionUpdate()
        {
        }

        [JsonProperty("channelName")]
        public string ChannelName { get; set; }

        [JsonProperty("sourceRepository")]
        public string SourceRepository { get; set; }

        [JsonProperty("policy")]
        public Models.SubscriptionPolicy Policy { get; set; }

        [JsonProperty("enabled")]
        public bool? Enabled { get; set; }

        [JsonProperty("sourceEnabled")]
        public bool? SourceEnabled { get; set; }

        [JsonProperty("pullRequestFailureNotificationTags")]
        public string PullRequestFailureNotificationTags { get; set; }

        [JsonProperty("sourceDirectory")]
        public string SourceDirectory { get; set; }

        [JsonProperty("targetDirectory")]
        public string TargetDirectory { get; set; }

        [JsonProperty("excludedAssets")]
        public IImmutableList<string> ExcludedAssets { get; set; }
    }
}
