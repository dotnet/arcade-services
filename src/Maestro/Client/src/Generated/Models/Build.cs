using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class Build
    {
        public Build(int id, DateTimeOffset dateProduced, int staleness, bool released, bool publishUsingPipelines, string commit, IImmutableList<Channel> channels, IImmutableList<Asset> assets, IImmutableList<BuildRef> dependencies)
        {
            Id = id;
            DateProduced = dateProduced;
            Staleness = staleness;
            Released = released;
            PublishUsingPipelines = publishUsingPipelines;
            Commit = commit;
            Channels = channels;
            Assets = assets;
            Dependencies = dependencies;
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

        [JsonProperty("publishUsingPipelines")]
        public bool PublishUsingPipelines { get; set; }

        [JsonProperty("dateProduced")]
        public DateTimeOffset DateProduced { get; }

        [JsonProperty("channels")]
        public IImmutableList<Channel> Channels { get; }

        [JsonProperty("assets")]
        public IImmutableList<Asset> Assets { get; }

        [JsonProperty("dependencies")]
        public IImmutableList<BuildRef> Dependencies { get; }

        [JsonProperty("staleness")]
        public int Staleness { get; }

        [JsonProperty("released")]
        public bool Released { get; }
    }
}
