using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    public class SharedMethods
    {
        public static void CompareManifestBuildData(ManifestBuildData actual, ManifestBuildData expected)
        {
            actual.AzureDevOpsAccount.Should().Be(expected.AzureDevOpsAccount);
            actual.AzureDevOpsBranch.Should().Be(expected.AzureDevOpsBranch);
            actual.AzureDevOpsBuildDefinitionId.Should().Be(expected.AzureDevOpsBuildDefinitionId);
            actual.AzureDevOpsBuildId.Should().Be(expected.AzureDevOpsBuildId);
            actual.AzureDevOpsBuildNumber.Should().Be(expected.AzureDevOpsBuildNumber);
            actual.AzureDevOpsProject.Should().Be(expected.AzureDevOpsProject);
            actual.AzureDevOpsRepository.Should().Be(expected.AzureDevOpsRepository);
            actual.InitialAssetsLocation.Should().Be(expected.InitialAssetsLocation);
            actual.PublishingVersion.Should().Be(expected.PublishingVersion);
        }

        public static void CompareSigningInformation(SigningInformation actualSigningInfo, SigningInformation expectedSigningInfo)
        {
            actualSigningInfo.CertificatesSignInfo.Should().BeEquivalentTo(expectedSigningInfo.CertificatesSignInfo);
            actualSigningInfo.FileExtensionSignInfos.Should().BeEquivalentTo(expectedSigningInfo.FileExtensionSignInfos);
            actualSigningInfo.FileSignInfos.Should().BeEquivalentTo(expectedSigningInfo.FileSignInfos);
            actualSigningInfo.ItemsToSign.Should().BeEquivalentTo(expectedSigningInfo.ItemsToSign);
        }

        public static void CompareBuildDataInformation(BuildData actualBuildData, BuildData expectedBuildData)
        {
            actualBuildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
            actualBuildData.AzureDevOpsAccount.Should().Be(expectedBuildData.AzureDevOpsAccount);
            actualBuildData.AzureDevOpsBranch.Should().Be(expectedBuildData.AzureDevOpsBranch);
            actualBuildData.AzureDevOpsBuildDefinitionId.Should().Be(expectedBuildData.AzureDevOpsBuildDefinitionId);
            actualBuildData.AzureDevOpsBuildId.Should().Be(expectedBuildData.AzureDevOpsBuildId);
            actualBuildData.AzureDevOpsBuildNumber.Should().Be(expectedBuildData.AzureDevOpsBuildNumber);
            actualBuildData.AzureDevOpsProject.Should().Be(expectedBuildData.AzureDevOpsProject);
            actualBuildData.AzureDevOpsRepository.Should().Be(expectedBuildData.AzureDevOpsRepository);
            actualBuildData.Commit.Should().Be(expectedBuildData.Commit);
            actualBuildData.Dependencies.Should().BeEquivalentTo(expectedBuildData.Dependencies);
            actualBuildData.GitHubBranch.Should().Be(expectedBuildData.GitHubBranch);
            actualBuildData.GitHubRepository.Should().Be(expectedBuildData.GitHubRepository);
            actualBuildData.Incoherencies.Should().BeEquivalentTo(expectedBuildData.Incoherencies);
            actualBuildData.IsValid.Should().Be(actualBuildData.IsValid);
            actualBuildData.Released.Should().Be(expectedBuildData.Released);
            actualBuildData.Stable.Should().Be(expectedBuildData.Stable);
        }

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
                Blobs = manifest.Blobs,
                Branch = manifest.Branch,
                BuildId = manifest.BuildId,
                Commit = manifest.Commit,
                InitialAssetsLocation = manifest.InitialAssetsLocation,
                IsReleaseOnlyPackageVersion = manifest.IsReleaseOnlyPackageVersion,
                IsStable = manifest.IsStable,
                Location = manifest.Location,
                Name = manifest.Name,

                // Note that these are shallow copies for the moment, since deep copy isn't needed for the current tests
                Packages = manifest.Packages,
                PublishingVersion = manifest.PublishingVersion,
                SigningInformation = manifest.SigningInformation
            };
        }
    }
}
