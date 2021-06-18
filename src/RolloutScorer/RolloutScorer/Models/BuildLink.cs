using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace RolloutScorer.Models
{
    public class BuildLink
    {
        [JsonProperty("href")]
        public string Href { get; set; }
    }
}
