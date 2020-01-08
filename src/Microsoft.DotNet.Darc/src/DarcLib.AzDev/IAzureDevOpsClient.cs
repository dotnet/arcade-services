// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    public interface IAzureDevOpsClient
    {
        /// <summary>
        /// Edit the release Art
        /// </summary>
        /// <param name="accountName"></param>
        /// <param name="projectName"></param>
        /// <param name="releaseDefinition"></param>
        /// <param name="build"></param>
        /// <returns></returns>
        Task<AzureDevOpsReleaseDefinition> AdjustReleasePipelineArtifactSourceAsync(string accountName, string projectName, AzureDevOpsReleaseDefinition releaseDefinition, AzureDevOpsBuild build);

        /// <summary>
        /// Deletes an Azure Artifacts feed and all of its packages
        /// </summary>
        /// <param name="accountName">Azure DevOps account name</param>
        /// <param name="project">Project that the feed was created in</param>
        /// <param name="feedIdentifier">Name or id of the feed</param>
        /// <returns></returns>
        Task DeleteFeedAsync(string accountName, string project, string feedIdentifier);

        /// <summary>
        /// Deletes a NuGet package version from a feed.
        /// </summary>
        /// <param name="accountName">Azure DevOps account name</param>
        /// <param name="project">Project that the feed was created in</param>
        /// <param name="feedIdentifier">Name or id of the feed</param>
        /// <param name="packageName">Name of the package</param>
        /// <param name="version">Version to delete</param>
        /// <returns></returns>
        Task DeleteNuGetPackageVersionFromFeedAsync(string accountName, string project, string feedIdentifier, string packageName, string version);

        /// <summary>
        ///     Fetches an specific AzDO build based on its ID.
        /// </summary>
        /// <param name="accountName">Azure DevOps account name</param>
        /// <param name="projectName">Project name</param>
        /// <param name="buildId">Id of the build to be retrieved</param>
        /// <returns>AzureDevOpsBuild</returns>
        Task<AzureDevOpsBuild> GetBuildAsync(string accountName, string projectName, long buildId);

        /// <summary>
        /// Gets a specified Artifact feed with their pacckages in an Azure DevOps account.
        /// </summary>
        /// <param name="accountName">Azure DevOps account name.</param>
        /// <param name="feedIdentifier">ID or name of the feed.</param>
        /// <returns>List of Azure DevOps feeds in the account.</returns>
        Task<AzureDevOpsFeed> GetFeedAndPackagesAsync(string accountName, string project, string feedIdentifier);

        /// <summary>
        /// Gets a specified Artifact feed in an Azure DevOps account.
        /// </summary>
        /// <param name="accountName">Azure DevOps account name</param>
        /// <param name="feedIdentifier">ID or name of the feed</param>
        /// <returns>List of Azure DevOps feeds in the account</returns>
        Task<AzureDevOpsFeed> GetFeedAsync(string accountName, string project, string feedIdentifier);

        /// <summary>
        /// Gets all Artifact feeds along with their packages in an Azure DevOps account.
        /// </summary>
        /// <param name="accountName">Azure DevOps account name.</param>
        /// <returns>List of Azure DevOps feeds in the account.</returns>
        Task<List<AzureDevOpsFeed>> GetFeedsAndPackagesAsync(string accountName);

        /// <summary>
        /// Gets a specified Artifact feed in an Azure DevOps account.
        /// </summary>
        /// <param name="accountName">Azure DevOps account name</param>
        /// <param name="feedIdentifier">ID or name of the feed</param>
        /// <returns>List of Azure DevOps feeds in the account</returns>
        Task<List<AzureDevOpsFeed>> GetFeedsAsync(string accountName);

        /// <summary>
        /// Gets all packages in a given Azure DevOps feed
        /// </summary>
        /// <param name="accountName">Azure DevOps account name</param>
        /// <param name="project">Project that the feed was created in</param>
        /// <param name="feedIdentifier">Name or id of the feed</param>
        /// <returns>List of packages in the feed</returns>
        Task<List<AzureDevOpsPackage>> GetPackagesForFeedAsync(string accountName, string project, string feedIdentifier);

        /// <summary>
        /// Returns the project ID for a combination of Azure DevOps account and project name
        /// </summary>
        /// <param name="accountName">Azure DevOps account</param>
        /// <param name="projectName">Azure DevOps project to get the ID for</param>
        /// <returns>Project Id</returns>
        Task<string> GetProjectIdAsync(string accountName, string projectName);

        /// <summary>
        /// Return the description of the release with ID informed.
        /// </summary>
        /// <param name="accountName">Azure DevOps account name</param>
        /// <param name="projectName">Project name</param>
        /// <param name="releaseId">ID of the release that should be looked up for</param>
        /// <returns></returns>
        Task<AzureDevOpsRelease> GetReleaseAsync(string accountName, string projectName, int releaseId);

        /// <summary>
        ///     Fetches an specific AzDO release definition based on its ID.
        /// </summary>
        /// <param name="accountName">Azure DevOps account name</param>
        /// <param name="projectName">Project name</param>
        /// <param name="releaseDefinitionId">Id of the release definition to be retrieved</param>
        /// <returns>AzureDevOpsReleaseDefinition</returns>
        Task<AzureDevOpsReleaseDefinition> GetReleaseDefinitionAsync(string accountName, string projectName, long releaseDefinitionId);

        /// <summary>
        ///     Trigger a new release using the release definition informed. No change is performed
        ///     on the release definition - it is used as is.
        /// </summary>
        /// <param name="accountName">Azure DevOps account name</param>
        /// <param name="projectName">Project name</param>
        /// <param name="releaseDefinition">Release definition to be updated</param>
        Task<int> StartNewReleaseAsync(string accountName, string projectName, AzureDevOpsReleaseDefinition releaseDefinition, int barBuildId);
    }
}
