// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Newtonsoft.Json.Linq;

namespace ProductConstructionService.FeedCleaner;

// TODO (https://github.com/dotnet/arcade-services/issues/3808) delete this class and use the normal AzureDevOpsClient
internal class FakeAzureDevOpsClient : IAzureDevOpsClient
{
    public Task<AzureDevOpsReleaseDefinition> AdjustReleasePipelineArtifactSourceAsync(string accountName, string projectName, AzureDevOpsReleaseDefinition releaseDefinition, AzureDevOpsBuild build)
        => Task.FromResult(releaseDefinition);
    public Task DeleteFeedAsync(string accountName, string project, string feedIdentifier) => Task.CompletedTask;
    public Task DeleteNuGetPackageVersionFromFeedAsync(string accountName, string project, string feedIdentifier, string packageName, string version)
        => Task.CompletedTask;
    public Task<List<AzureDevOpsBuildArtifact>> GetBuildArtifactsAsync(string accountName, string projectName, int buildId, int maxRetries = 15)
        => Task.FromResult<List<AzureDevOpsBuildArtifact>>([]);
    public Task<AzureDevOpsBuild> GetBuildAsync(string accountName, string projectName, long buildId)
        => Task.FromResult(new AzureDevOpsBuild());
    public Task<JObject> GetBuildsAsync(string account, string project, int definitionId, string branch, int count, string status)
        => Task.FromResult(new JObject());
    public Task<AzureDevOpsFeed> GetFeedAndPackagesAsync(string accountName, string project, string feedIdentifier)
        => Task.FromResult(new AzureDevOpsFeed("fake", "fake", "fake"));
    public Task<AzureDevOpsFeed> GetFeedAsync(string accountName, string project, string feedIdentifier)
        => Task.FromResult(new AzureDevOpsFeed("fake", "fake", "fake"));
    public Task<List<AzureDevOpsFeed>> GetFeedsAndPackagesAsync(string accountName)
        => Task.FromResult<List<AzureDevOpsFeed>>([]);
    public Task<List<AzureDevOpsFeed>> GetFeedsAsync(string accountName)
        => Task.FromResult<List<AzureDevOpsFeed>>([]);
    public Task<List<AzureDevOpsPackage>> GetPackagesForFeedAsync(string accountName, string project, string feedIdentifier)
        => Task.FromResult<List<AzureDevOpsPackage>>([]);
    public Task<string> GetProjectIdAsync(string accountName, string projectName) => Task.FromResult(string.Empty);
    public Task<AzureDevOpsRelease> GetReleaseAsync(string accountName, string projectName, int releaseId)
        => Task.FromResult(new AzureDevOpsRelease());
    public Task<AzureDevOpsReleaseDefinition> GetReleaseDefinitionAsync(string accountName, string projectName, long releaseDefinitionId)
        => Task.FromResult(new AzureDevOpsReleaseDefinition());
    public Task<int> StartNewBuildAsync(string accountName, string projectName, int buildDefinitionId, string sourceBranch, string sourceVersion, Dictionary<string, string> queueTimeVariables, Dictionary<string, string> templateParameters, Dictionary<string, string> pipelineResources)
        => Task.FromResult(0);
    public Task<int> StartNewReleaseAsync(string accountName, string projectName, AzureDevOpsReleaseDefinition releaseDefinition, int barBuildId)
        => Task.FromResult(0);
}
