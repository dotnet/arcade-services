using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class Build
    {
        public Build(int id, DateTimeOffset dateProduced, int staleness, bool released, bool stable, string commit, IImmutableList<Models.Channel> channels, IImmutableList<Models.Asset> assets, IImmutableList<Models.BuildRef> dependencies, IImmutableList<Models.BuildIncoherence> incoherencies)
        {
            Id = id;
            DateProduced = dateProduced;
            Staleness = staleness;
            Released = released;
            Stable = stable;
            Commit = commit;
            Channels = channels;
            Assets = assets;
            Dependencies = dependencies;
            Incoherencies = incoherencies;
        }

        [JsonProperty("id")]
        public int Id { get; }

        [JsonProperty("commit")]
        public string Commit { get; }

        [JsonProperty("azureDevOpsBuildId")]
        public int? AzureDevOpsBuildId { get; set; }

        [JsonProperty("azureDevOpsBuildDefinitionId")]
        public int? AzureDevOpsBuildDefinitionId { get; set; }

        [JsonProperty("azureDevOpsAccount")]
        public string AzureDevOpsAccount { get; set; }

        [JsonProperty("azureDevOpsProject")]
        public string AzureDevOpsProject { get; set; }

        [JsonProperty("azureDevOpsBuildNumber")]
        public string AzureDevOpsBuildNumber { get; set; }

        [JsonProperty("azureDevOpsRepository")]
        public string AzureDevOpsRepository { get; set; }

        [JsonProperty("azureDevOpsBranch")]
        public string AzureDevOpsBranch { get; set; }

        [JsonProperty("gitHubRepository")]
        public string GitHubRepository { get; set; }

        [JsonProperty("gitHubBranch")]
        public string GitHubBranch { get; set; }

        [JsonProperty("dateProduced")]
        public DateTimeOffset DateProduced { get; }

        [JsonProperty("channels")]
        public IImmutableList<Models.Channel> Channels { get; }

        [JsonProperty("assets")]
        public IImmutableList<Models.Asset> Assets { get; }

        [JsonProperty("dependencies")]
        public IImmutableList<Models.BuildRef> Dependencies { get; }

        [JsonProperty("incoherencies")]
        public IImmutableList<Models.BuildIncoherence> Incoherencies { get; }

        [JsonProperty("staleness")]
        public int Staleness { get; }

        [JsonProperty("released")]
        public bool Released { get; }

        [JsonProperty("stable")]
        public bool Stable { get; }
    }
}
