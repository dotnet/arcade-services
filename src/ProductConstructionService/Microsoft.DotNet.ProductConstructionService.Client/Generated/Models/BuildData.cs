// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class BuildData
    {
        public BuildData(string commit, string azureDevOpsAccount, string azureDevOpsProject, string azureDevOpsBuildNumber, string azureDevOpsRepository, string azureDevOpsBranch, bool released, bool stable)
        {
            Commit = commit;
            AzureDevOpsAccount = azureDevOpsAccount;
            AzureDevOpsProject = azureDevOpsProject;
            AzureDevOpsBuildNumber = azureDevOpsBuildNumber;
            AzureDevOpsRepository = azureDevOpsRepository;
            AzureDevOpsBranch = azureDevOpsBranch;
            Released = released;
            Stable = stable;
        }

        [JsonProperty("commit")]
        public string Commit { get; set; }

        [JsonProperty("assets")]
        public List<AssetData> Assets { get; set; }

        [JsonProperty("dependencies")]
        public List<BuildRef> Dependencies { get; set; }

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

        [JsonProperty("released")]
        public bool Released { get; set; }

        [JsonProperty("stable")]
        public bool Stable { get; set; }

        [JsonProperty("incoherencies")]
        public List<BuildIncoherence> Incoherencies { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(Commit))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(AzureDevOpsAccount))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(AzureDevOpsProject))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(AzureDevOpsBuildNumber))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(AzureDevOpsRepository))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(AzureDevOpsBranch))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
