using System;
using System.Collections.Immutable;
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

        [JsonProperty("onLongestBuildPath")]
        public bool OnLongestBuildPath { get; set; }
    }
}
