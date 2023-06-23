using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class DefaultChannelCreateData
    {
        public DefaultChannelCreateData(string repository, string branch, int channelId)
        {
            Repository = repository;
            Branch = branch;
            ChannelId = channelId;
        }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("channelId")]
        public int ChannelId { get; set; }

        [JsonProperty("enabled")]
        public bool? Enabled { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(Repository))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(Branch))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
