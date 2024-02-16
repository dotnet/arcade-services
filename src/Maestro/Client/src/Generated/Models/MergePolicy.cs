using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class MergePolicy
    {
        public MergePolicy()
        {
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("properties")]
        public IImmutableDictionary<string, Newtonsoft.Json.Linq.JToken> Properties { get; set; }
    }
}
