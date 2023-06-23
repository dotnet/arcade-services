using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class BuildUpdate
    {
        public BuildUpdate()
        {
        }

        [JsonProperty("released")]
        public bool? Released { get; set; }
    }
}
