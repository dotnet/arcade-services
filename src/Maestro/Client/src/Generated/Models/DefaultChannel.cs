using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class DefaultChannel
    {
        public DefaultChannel(int id, string repository, bool enabled)
        {
            Id = id;
            Repository = repository;
            Enabled = enabled;
        }

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("channel")]
        public Channel Channel { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(Repository))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
