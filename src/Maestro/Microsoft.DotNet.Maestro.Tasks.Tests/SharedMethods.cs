// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    public class SharedMethods
    {
        public static Manifest GetCopyOfManifest(Manifest manifest)
        {
            return new Manifest()
            {
                AzureDevOpsAccount = manifest.AzureDevOpsAccount,
                AzureDevOpsBranch = manifest.AzureDevOpsBranch,
                AzureDevOpsBuildDefinitionId = manifest.AzureDevOpsBuildDefinitionId,
                AzureDevOpsBuildDefinitionIdString = manifest.AzureDevOpsBuildDefinitionIdString,
                AzureDevOpsBuildId = manifest.AzureDevOpsBuildId,
                AzureDevOpsBuildIdString = manifest.AzureDevOpsBuildIdString,
                AzureDevOpsBuildNumber = manifest.AzureDevOpsBuildNumber,
                AzureDevOpsProject = manifest.AzureDevOpsProject,
                AzureDevOpsRepository = manifest.AzureDevOpsRepository,
                Branch = manifest.Branch,
                BuildId = manifest.BuildId,
                Commit = manifest.Commit,
                InitialAssetsLocation = manifest.InitialAssetsLocation,
                IsReleaseOnlyPackageVersion = manifest.IsReleaseOnlyPackageVersion,
                IsStable = manifest.IsStable,
                Location = manifest.Location,
                Name = manifest.Name,

                // Note that these are shallow copies for the moment, since deep copy isn't needed for the current tests
                Blobs = manifest.Blobs,
                Packages = manifest.Packages,
                PublishingVersion = manifest.PublishingVersion,
                SigningInformation = manifest.SigningInformation
            };
        }
    }
}
