// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class Subscription
    {
        public Subscription(Guid id, bool enabled, string sourceRepository, string targetRepository, string targetBranch, string pullRequestFailureNotificationTags)
        {
            Id = id;
            Enabled = enabled;
            SourceRepository = sourceRepository;
            TargetRepository = targetRepository;
            TargetBranch = targetBranch;
            PullRequestFailureNotificationTags = pullRequestFailureNotificationTags;
        }

        [JsonProperty("id")]
        public Guid Id { get; }

        [JsonProperty("channel")]
        public Channel Channel { get; set; }

        [JsonProperty("sourceRepository")]
        public string SourceRepository { get; }

        [JsonProperty("targetRepository")]
        public string TargetRepository { get; }

        [JsonProperty("targetBranch")]
        public string TargetBranch { get; }

        [JsonProperty("policy")]
        public SubscriptionPolicy Policy { get; set; }

        [JsonProperty("lastAppliedBuild")]
        public Build LastAppliedBuild { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; }

        [JsonProperty("pullRequestFailureNotificationTags")]
        public string PullRequestFailureNotificationTags { get; }
    }
}
