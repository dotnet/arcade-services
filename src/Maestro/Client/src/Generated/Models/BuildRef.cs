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
        public int BuildId { get; set; }

        [JsonProperty("isProduct")]
        public bool IsProduct { get; set; }

        [JsonProperty("timeToInclusionInMinutes")]
        public double TimeToInclusionInMinutes { get; set; }
    }
}
