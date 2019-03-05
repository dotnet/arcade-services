using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class ReleasePipeline
    {
        public ReleasePipeline(int id, int pipelineIdentifier)
        {
            Id = id;
            PipelineIdentifier = pipelineIdentifier;
        }

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("pipelineIdentifier")]
        public int PipelineIdentifier { get; set; }

        [JsonProperty("organization")]
        public string Organization { get; set; }

        [JsonProperty("project")]
        public string Project { get; set; }
    }
}
