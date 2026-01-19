// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class SourceManifestEntry
    {
        public SourceManifestEntry(string path, string remoteUri, string commitSha)
        {
            Path = path;
            RemoteUri = remoteUri;
            CommitSha = commitSha;
        }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("remoteUri")]
        public string RemoteUri { get; set; }

        [JsonProperty("commitSha")]
        public string CommitSha { get; set; }

        [JsonProperty("barId")]
        public int? BarId { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(Path))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(RemoteUri))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(CommitSha))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
