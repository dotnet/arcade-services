using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class IncoherentNode
    {
        public IncoherentNode()
        {
        }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("commit")]
        public string Commit { get; set; }
    }
}
