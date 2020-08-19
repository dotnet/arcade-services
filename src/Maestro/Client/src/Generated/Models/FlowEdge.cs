using System;
using System.Collections.Immutable;
using System.Dynamic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class FlowEdge
    {
        public FlowEdge(bool onLongestBuildPath, string toId, string fromId)
        {
            OnLongestBuildPath = onLongestBuildPath;
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

        // TODO: This is explicitly ignored in Maestro API, should we return it?
        [JsonProperty("backEdge")]
        public bool BackEdge { get; set; }

        [JsonProperty("isToolingOnly")]
        public bool IsToolingOnly { get; set; }

        [JsonProperty("partOfCycle")]
        public bool PartOfCycle { get; set; }
    }
}
