using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class FlowEdge
    {
        public FlowEdge(Guid subscriptionId, bool onLongestBuildPath, bool isToolingOnly, bool backEdge, string toId, string fromId)
        {
            SubscriptionId = subscriptionId;
            OnLongestBuildPath = onLongestBuildPath;
            IsToolingOnly = isToolingOnly;
            BackEdge = backEdge;
            ToId = toId;
            FromId = fromId;
        }

        [JsonProperty("toId")]
        public string ToId { get; }

        [JsonProperty("fromId")]
        public string FromId { get; }

        [JsonProperty("subscriptionId")]
        public Guid SubscriptionId { get; set; }

        [JsonProperty("onLongestBuildPath")]
        public bool OnLongestBuildPath { get; set; }

        [JsonProperty("isToolingOnly")]
        public bool IsToolingOnly { get; set; }

        [JsonProperty("partOfCycle")]
        public bool? PartOfCycle { get; set; }

        [JsonProperty("backEdge")]
        public bool BackEdge { get; set; }
    }
}
