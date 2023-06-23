using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class GoalRequestJson
    {
        public GoalRequestJson(int minutes)
        {
            Minutes = minutes;
        }

        [JsonProperty("minutes")]
        public int Minutes { get; set; }
    }
}
