// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
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
        public Channel Channel { get; set; }

        [JsonProperty("minutes")]
        public int Minutes { get; set; }
    }
}
