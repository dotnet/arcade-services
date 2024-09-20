using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class SyncedCommit
    {
        public SyncedCommit()
        {
        }

        [JsonProperty("repoPath")]
        public string RepoPath { get; set; }

        [JsonProperty("commitUrl")]
        public string CommitUrl { get; set; }

        [JsonProperty("dateCommitted")]
        public string DateCommitted { get; set; }
    }
}
