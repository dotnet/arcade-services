using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class Commit
    {
        public Commit()
        {
        }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("sha")]
        public string Sha { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
