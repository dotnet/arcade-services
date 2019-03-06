using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class BuildRef
    {
        public BuildRef(int buildId, bool isProduct)
        {
            BuildId = buildId;
            IsProduct = isProduct;
        }

        [JsonProperty("buildId")]
        public int BuildId { get; }

        [JsonProperty("isProduct")]
        public bool IsProduct { get; }
    }
}
