// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
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
