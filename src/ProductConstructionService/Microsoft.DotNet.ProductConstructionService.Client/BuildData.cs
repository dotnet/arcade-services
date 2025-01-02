// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class BuildData
    {
        // Deep-copy constructor not provided by generated code
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
                var assetList = new List<AssetData>();

                foreach (AssetData asset in buildData.Assets)
                {
                    var locationsList = new List<AssetLocationData>();
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
                        Locations = locationsList
                    });
                }

                Assets = assetList;
            }

            //Dependencies deep copy
            if (buildData.Dependencies != null)
            {
                var dependenciesList = new List<BuildRef>();

                foreach (BuildRef dep in buildData.Dependencies)
                {
                    dependenciesList.Add(new BuildRef(dep.BuildId, dep.IsProduct, dep.TimeToInclusionInMinutes));
                }

                Dependencies = dependenciesList;
            }


            //Incoherencies deep copy
            if (buildData.Incoherencies != null)
            {
                var incoherenciesList = new List<BuildIncoherence>();

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

                Incoherencies = incoherenciesList;
            }
        }
    }
}
