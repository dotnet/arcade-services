using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class IncoherentDependency
    {
        public IncoherentDependency()
        {
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("commit")]
        public string Commit { get; set; }
    }
}
