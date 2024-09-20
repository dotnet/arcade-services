// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class Subscription
    {
        public Subscription(Guid id, bool enabled, bool sourceEnabled, string sourceRepository, string targetRepository, string targetBranch, string sourceDirectory, string targetDirectory, string pullRequestFailureNotificationTags, List<string> excludedAssets)
        {
            Id = id;
            Enabled = enabled;
            SourceEnabled = sourceEnabled;
            SourceRepository = sourceRepository;
            TargetRepository = targetRepository;
            TargetBranch = targetBranch;
            SourceDirectory = sourceDirectory;
            TargetDirectory = targetDirectory;
            PullRequestFailureNotificationTags = pullRequestFailureNotificationTags;
            ExcludedAssets = excludedAssets;
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

        [JsonProperty("sourceEnabled")]
        public bool SourceEnabled { get; }

        [JsonProperty("sourceDirectory")]
        public string SourceDirectory { get; }

        [JsonProperty("targetDirectory")]
        public string TargetDirectory { get; }

        [JsonProperty("pullRequestFailureNotificationTags")]
        public string PullRequestFailureNotificationTags { get; }

        [JsonProperty("excludedAssets")]
        public List<string> ExcludedAssets { get; }
    }
}
