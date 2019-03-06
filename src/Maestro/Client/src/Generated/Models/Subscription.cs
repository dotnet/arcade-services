using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class Subscription
    {
        public Subscription(Guid id, bool enabled, string sourceRepository, string targetRepository, string targetBranch)
        {
            Id = id;
            Enabled = enabled;
            SourceRepository = sourceRepository;
            TargetRepository = targetRepository;
            TargetBranch = targetBranch;
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
    }
}
