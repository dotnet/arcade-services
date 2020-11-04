using System.Collections.Generic;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
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

        public BuildData(BuildData buildData)
        {
            Commit = buildData.Commit;
            AzureDevOpsBuildId = buildData.AzureDevOpsBuildId;
            AzureDevOpsBuildDefinitionId = buildData.AzureDevOpsBuildDefinitionId;
            AzureDevOpsAccount = buildData.AzureDevOpsAccount;
            AzureDevOpsProject = buildData.AzureDevOpsProject;
            AzureDevOpsBuildNumber = buildData.AzureDevOpsBuildNumber;
            AzureDevOpsRepository = buildData.AzureDevOpsRepository;
            AzureDevOpsBranch = buildData.AzureDevOpsBranch;
            GitHubRepository = buildData.GitHubRepository;
            GitHubBranch = buildData.GitHubBranch;
            Released = buildData.Released;
            Stable = buildData.Stable;

            // Assets deep copy
            if (buildData.Assets != null)
            {
                List<AssetData> assetList = new List<AssetData>();

                foreach (AssetData asset in buildData.Assets)
                {
                    List<AssetLocationData> locationsList = new List<AssetLocationData>();
                    foreach (AssetLocationData location in asset.Locations)
                    {
                        locationsList.Add(new AssetLocationData(location.Type)
                        {
                            Location = location.Location,
                        });
                    }

                    assetList.Add(new AssetData(asset.NonShipping)
                    {
                        Name = asset.Name,
                        Version = asset.Version,
                        Locations = locationsList.ToImmutableList<AssetLocationData>()
                    });
                }

                Assets = assetList.ToImmutableList<AssetData>();
            }

            //Dependencies deep copy
            if (buildData.Dependencies != null)
            {
                List<BuildRef> dependenciesList = new List<BuildRef>();

                foreach (BuildRef dep in buildData.Dependencies)
                {
                    dependenciesList.Add(new BuildRef(dep.BuildId, dep.IsProduct, dep.TimeToInclusionInMinutes));
                }

                Dependencies = dependenciesList.ToImmutableList<BuildRef>();
            }


            //Incoherencies deep copy
            if (buildData.Incoherencies != null)
            {
                List<BuildIncoherence> incoherenciesList = new List<BuildIncoherence>();

                foreach (BuildIncoherence incoherence in buildData.Incoherencies)
                {
                    incoherenciesList.Add(new BuildIncoherence()
                    {
                        Commit = incoherence.Commit,
                        Name = incoherence.Name,
                        Repository = incoherence.Repository,
                        Version = incoherence.Version
                    });
                }

                Incoherencies = incoherenciesList.ToImmutableList<BuildIncoherence>();
            }
        }

        [JsonProperty("commit")]
        public string Commit { get; set; }

        [JsonProperty("assets")]
        public IImmutableList<Models.AssetData> Assets { get; set; }

        [JsonProperty("dependencies")]
        public IImmutableList<Models.BuildRef> Dependencies { get; set; }

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
        public IImmutableList<Models.BuildIncoherence> Incoherencies { get; set; }

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
