using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class Channel
    {
        public Channel(int id, string name, string classification, IImmutableList<ReleasePipeline> releasePipelines)
        {
            Id = id;
            Name = name;
            Classification = classification;
            ReleasePipelines = releasePipelines;
        }

        [JsonProperty("id")]
        public int Id { get; }

        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("classification")]
        public string Classification { get; }

        [JsonProperty("releasePipelines")]
        public IImmutableList<ReleasePipeline> ReleasePipelines { get; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(Name))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(Classification))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
