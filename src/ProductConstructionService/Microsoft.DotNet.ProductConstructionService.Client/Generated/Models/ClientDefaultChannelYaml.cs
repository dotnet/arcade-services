// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class ClientDefaultChannelYaml
    {
        public ClientDefaultChannelYaml(string repository, string branch, string channel, bool enabled)
        {
            Repository = repository;
            Branch = branch;
            Channel = channel;
            Enabled = enabled;
        }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

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
                if (string.IsNullOrEmpty(Branch))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(Channel))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
