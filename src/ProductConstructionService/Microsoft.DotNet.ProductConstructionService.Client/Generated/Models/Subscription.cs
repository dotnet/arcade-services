// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class Subscription
    {
        public Subscription(Guid id, bool mergePrs, bool enabled, bool sourceEnabled, bool autoApprove, string sourceRepository, string targetRepository, string targetBranch, List<string> ignoredChecks, string sourceDirectory, string targetDirectory, string pullRequestFailureNotificationTags, List<string> excludedAssets)
        {
            Id = id;
            MergePrs = mergePrs;
            Enabled = enabled;
            SourceEnabled = sourceEnabled;
            AutoApprove = autoApprove;
            SourceRepository = sourceRepository;
            TargetRepository = targetRepository;
            TargetBranch = targetBranch;
            IgnoredChecks = ignoredChecks;
            SourceDirectory = sourceDirectory;
            TargetDirectory = targetDirectory;
            PullRequestFailureNotificationTags = pullRequestFailureNotificationTags;
            ExcludedAssets = excludedAssets;
        }

        [JsonProperty("id")]
        public Guid Id { get; }

        [JsonProperty("channel")]
        public Models.Channel Channel { get; set; }

        [JsonProperty("sourceRepository")]
        public string SourceRepository { get; }

        [JsonProperty("targetRepository")]
        public string TargetRepository { get; }

        [JsonProperty("targetBranch")]
        public string TargetBranch { get; }

        [JsonProperty("mergePrs")]
        public bool MergePrs { get; }

        [JsonProperty("ignoredChecks")]
        public List<string> IgnoredChecks { get; }

        [JsonProperty("policy")]
        public SubscriptionPolicy Policy { get; set; }

        [JsonProperty("lastAppliedBuild")]
        public Build LastAppliedBuild { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; }

        [JsonProperty("sourceEnabled")]
        public bool SourceEnabled { get; }

        [JsonProperty("autoApprove")]
        public bool AutoApprove { get; }

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
