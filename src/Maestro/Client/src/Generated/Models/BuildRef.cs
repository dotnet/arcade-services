using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class BuildRef
    {
        public BuildRef(int buildId, bool isProduct, double timeToInclusionInMinutes)
        {
            BuildId = buildId;
            IsProduct = isProduct;
            TimeToInclusionInMinutes = timeToInclusionInMinutes;
        }

        [JsonProperty("buildId")]
        public int BuildId { get; }

        [JsonProperty("isProduct")]
        public bool IsProduct { get; }

        [JsonProperty("timeToInclusionInMinutes")]
        public double TimeToInclusionInMinutes { get; set; }
    }
}
