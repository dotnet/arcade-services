// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class Build
    {
        public Build(int id, DateTimeOffset dateProduced, int staleness, bool released, bool stable, string commit, List<Channel> channels, List<Asset> assets, List<BuildRef> dependencies, List<BuildIncoherence> incoherencies)
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
        public List<Channel> Channels { get; }

        [JsonProperty("assets")]
        public List<Asset> Assets { get; }

        [JsonProperty("dependencies")]
        public List<BuildRef> Dependencies { get; }

        [JsonProperty("incoherencies")]
        public List<BuildIncoherence> Incoherencies { get; }

        [JsonProperty("staleness")]
        public int Staleness { get; }

        [JsonProperty("released")]
        public bool Released { get; }

        [JsonProperty("stable")]
        public bool Stable { get; }
    }
}
