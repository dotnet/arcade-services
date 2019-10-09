using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
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

        [JsonProperty("policy")]
        public SubscriptionPolicy Policy { get; set; }

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
