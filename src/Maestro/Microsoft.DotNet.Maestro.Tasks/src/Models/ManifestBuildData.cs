// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks
{
    public class ManifestBuildData
    {
        public string InitialAssetsLocation { get; set; }

        public int? AzureDevOpsBuildId { get; set; }

        public int? AzureDevOpsBuildDefinitionId { get; set; }

        public string AzureDevOpsAccount { get; set; }

        public string AzureDevOpsProject { get; set; }

        public string AzureDevOpsBuildNumber { get; set; }

        public string AzureDevOpsRepository { get; set; }

        public string AzureDevOpsBranch { get; set; }

        public int PublishingVersion { get; set; }

        public string  IsReleaseOnlyPackageVersion { get; set; }

        public ManifestBuildData(Manifest manifest)
        {
            InitialAssetsLocation = manifest.InitialAssetsLocation;
            AzureDevOpsBuildId = manifest.AzureDevOpsBuildId;
            AzureDevOpsBuildDefinitionId = manifest.AzureDevOpsBuildDefinitionId;
            AzureDevOpsAccount = manifest.AzureDevOpsAccount;
            AzureDevOpsProject = manifest.AzureDevOpsProject;
            AzureDevOpsBuildNumber = manifest.AzureDevOpsBuildNumber;
            AzureDevOpsRepository = manifest.AzureDevOpsRepository;
            AzureDevOpsBranch = manifest.AzureDevOpsBranch;
            PublishingVersion = manifest.PublishingVersion;
            IsReleaseOnlyPackageVersion = manifest.IsReleaseOnlyPackageVersion;
        }

        public bool Equals(ManifestBuildData manifestBuildData)
        {
            if (InitialAssetsLocation != manifestBuildData.InitialAssetsLocation ||
                AzureDevOpsBuildId != manifestBuildData.AzureDevOpsBuildId ||
                AzureDevOpsBuildDefinitionId != manifestBuildData.AzureDevOpsBuildDefinitionId ||
                AzureDevOpsAccount != manifestBuildData.AzureDevOpsAccount ||
                AzureDevOpsProject != manifestBuildData.AzureDevOpsProject ||
                AzureDevOpsBuildNumber != manifestBuildData.AzureDevOpsBuildNumber ||
                AzureDevOpsRepository != manifestBuildData.AzureDevOpsRepository ||
                AzureDevOpsBranch != manifestBuildData.AzureDevOpsBranch ||
                PublishingVersion != manifestBuildData.PublishingVersion)
            {
                return false;
            }

            return true;
        }

        public Dictionary<string, string> ToDictionary()
        {
            return new Dictionary<string, string>()
            {
                { nameof(InitialAssetsLocation), InitialAssetsLocation },
                { nameof(AzureDevOpsBuildId), AzureDevOpsBuildId.ToString() },
                { nameof(AzureDevOpsBuildDefinitionId), AzureDevOpsBuildDefinitionId.ToString() },
                { nameof(AzureDevOpsAccount), AzureDevOpsAccount },
                { nameof(AzureDevOpsProject), AzureDevOpsProject },
                { nameof(AzureDevOpsBuildNumber), AzureDevOpsBuildNumber },
                { nameof(AzureDevOpsRepository), AzureDevOpsRepository },
                { nameof(AzureDevOpsBranch), AzureDevOpsBranch }
            };
        }
    }
}
