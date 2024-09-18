// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class SubscriptionData
    {
        public SubscriptionData(string channelName, string sourceRepository, string targetRepository, string targetBranch, SubscriptionPolicy policy)
        {
            ChannelName = channelName;
            SourceRepository = sourceRepository;
            TargetRepository = targetRepository;
            TargetBranch = targetBranch;
            Policy = policy;
        }

        [JsonProperty("channelName")]
        public string ChannelName { get; set; }

        [JsonProperty("sourceRepository")]
        public string SourceRepository { get; set; }

        [JsonProperty("targetRepository")]
        public string TargetRepository { get; set; }

        [JsonProperty("targetBranch")]
        public string TargetBranch { get; set; }

        [JsonProperty("enabled")]
        public bool? Enabled { get; set; }

        [JsonProperty("sourceEnabled")]
        public bool? SourceEnabled { get; set; }

        [JsonProperty("sourceDirectory")]
        public string SourceDirectory { get; set; }

        [JsonProperty("targetDirectory")]
        public string TargetDirectory { get; set; }

        [JsonProperty("policy")]
        public SubscriptionPolicy Policy { get; set; }

        [JsonProperty("pullRequestFailureNotificationTags")]
        public string PullRequestFailureNotificationTags { get; set; }

        [JsonProperty("excludedAssets")]
        public List<string> ExcludedAssets { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(ChannelName))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(SourceRepository))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(TargetRepository))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(TargetBranch))
                {
                    return false;
                }
                if (Policy == default(SubscriptionPolicy))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
