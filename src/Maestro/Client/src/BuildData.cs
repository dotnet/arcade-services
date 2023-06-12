// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.DotNet.Maestro.Client.Models
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
    }
}
