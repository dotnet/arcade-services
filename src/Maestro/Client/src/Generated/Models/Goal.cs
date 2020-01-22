using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class Goal
    {
        public Goal(int definitionId, int minutes)
        {
            DefinitionId = definitionId;
            Minutes = minutes;
        }

        [JsonProperty("definitionId")]
        public int DefinitionId { get; set; }

        [JsonProperty("channel")]
        public Models.Channel Channel { get; set; }

        [JsonProperty("minutes")]
        public int Minutes { get; set; }
    }
}
