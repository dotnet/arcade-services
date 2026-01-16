// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Octokit;

namespace ProductConstructionService.ReproTool.Operations;
internal abstract class Operation(
    ILogger<Operation> logger,
    GitHubClient ghClient,
    IProductConstructionServiceApi localPcsApi)
{
    protected const string MaestroAuthTestOrgName = "maestro-auth-test";
    protected const string VmrForkRepoName = "dotnet";
    protected const string VmrForkUri = $"https://github.com/{MaestroAuthTestOrgName}/{VmrForkRepoName}";
    protected const string ProductRepoFormat = $"https://github.com/{MaestroAuthTestOrgName}/";
    protected const long InstallationId = 289474;
    protected const string SourceMappingsPath = $"{VmrInfo.SourceDirName}/{VmrInfo.SourceMappingsFileName}";
    protected const string SourceManifestPath = $"{VmrInfo.SourceDirName}/{VmrInfo.SourceManifestFileName}";
    protected const string DarcPRBranchPrefix = "darc";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        WriteIndented = true,
    };

    internal abstract Task RunAsync();

    protected async Task DeleteDarcPRBranchAsync(string repo, string targetBranch)
    {
        var branch = (await ghClient.Repository.Branch.GetAll(MaestroAuthTestOrgName, repo))
            .FirstOrDefault(branch => branch.Name.StartsWith($"{DarcPRBranchPrefix}-{targetBranch}"));

        if (branch == null)
        {
            logger.LogWarning("Couldn't find darc PR branch targeting branch {targetBranch}", targetBranch);
        }
        else
        {
            await DeleteGitHubBranchAsync(repo, branch.Name);
        }
    }

    private async Task DeleteGitHubBranchAsync(string repo, string branch) => await ghClient.Git.Reference.Delete(MaestroAuthTestOrgName, repo, $"heads/{branch}");

    protected async Task<Build> CreateBuildAsync(string repositoryUrl, string branch, string commit, List<AssetData> assets)
    {
        logger.LogInformation("Creating a test build");

        Build build = await localPcsApi.Builds.CreateAsync(new BuildData(
            commit: commit,
            azureDevOpsAccount: "test",
            azureDevOpsProject: "test",
            azureDevOpsBuildNumber: $"{DateTime.UtcNow:yyyyMMdd}.{new Random().Next(1, 75)}",
            azureDevOpsRepository: repositoryUrl,
            azureDevOpsBranch: branch,
            released: false,
            stable: false)
        {
            GitHubRepository = repositoryUrl,
            GitHubBranch = branch,
            Assets = assets
        });

        return build;
    }

    protected async Task IngestConfigurationAsync(string @namespace, ClientYamlConfiguration yamlConfiguration)
        => await localPcsApi.Ingestion.IngestNamespaceAsync(@namespace, saveChanges: true, yamlConfiguration);

    protected async Task<(ClientChannelYaml channel, ClientSubscriptionYaml subscription)> CreateChannelAndSubscriptionAsync(
        string @namespace,
        string channelName,
        string sourceRepository,
        string targetRepository,
        string targetBranch,
        bool sourceEnabled,
        string? sourceDirectory = null,
        string? targetDirectory = null,
        IEnumerable<string>? excludedAssets = null)
    {
        ClientChannelYaml channel = new(channelName, "test");
        ClientSubscriptionYaml subscription = new(
            channel: channelName,
            sourceRepository: sourceRepository,
            targetRepository: targetRepository,
            targetBranch: targetBranch,
            sourceEnabled: sourceEnabled,
            batchable: false,
            enabled: true,
            id: Guid.NewGuid(),
            updateFrequency: ClientUpdateFrequency.None)
        {
            SourceDirectory = sourceDirectory,
            TargetDirectory = targetDirectory,
            ExcludedAssets = excludedAssets != null ? [.. excludedAssets] : []
        };
        await IngestConfigurationAsync(@namespace, new ClientYamlConfiguration
        {
            Channels = [channel],
            Subscriptions = [subscription]
        });

        return (channel, subscription);
    }

    protected async Task DeleteNamespace(string @namespace)
    {
        logger.LogInformation("Deleting namespace {namespace}", @namespace);
        await localPcsApi.Ingestion.DeleteNamespaceAsync(@namespace, saveChanges: true);
    }

    protected async Task TriggerSubscriptionAsync(Guid subscriptionId, int buildId = 0, bool force = false)
    {
        logger.LogInformation("Triggering subscription {subscriptionId}", subscriptionId);
        await localPcsApi.Subscriptions.TriggerSubscriptionAsync(buildId, force: force, subscriptionId);
    }

    protected async Task<AsyncDisposableValue<string>> PrepareVmrForkAsync(string branch, bool skipCleanup)
    {
        logger.LogInformation("Preparing VMR fork");
        // Sync the VMR fork branch
        await SyncForkAsync("dotnet", "dotnet", branch);

        return await CreateTmpBranchAsync(VmrForkRepoName, branch, skipCleanup);
    }

    protected async Task UpdateVmrSourceFiles(string branch, string productRepoUri, string productRepoForkUri)
    {
        // Fetch source mappings and source manifest files and replace the mapping for the repo we're testing on
        logger.LogInformation("Updating source mappings and source manifest files in VMR fork to replace original product repo mapping with fork mapping");

        await UpdateRemoteVmrForkFileAsync(branch, SourceMappingsPath, content =>
        {
            var settings = JsonSerializer.Deserialize<SourceMappingFile>(content, JsonSerializerOptions)
                ?? throw new Exception($"Failed to deserialize {VmrInfo.SourceMappingsFileName}");

            var affectedMapping = settings.Mappings.First(m => m.DefaultRemote?.Equals(productRepoUri, StringComparison.OrdinalIgnoreCase) ?? false);
            affectedMapping.DefaultRemote = productRepoForkUri;

            return JsonSerializer.Serialize(settings, JsonSerializerOptions);
        });

        await UpdateRemoteVmrForkFileAsync(branch, SourceManifestPath, content =>
        {
            var sourceManifest = SourceManifest.FromJson(content)
                ?? throw new Exception($"Failed to deserialize {VmrInfo.SourceManifestFileName}");

            var affectedRecord = sourceManifest.Repositories.First(m => m.RemoteUri.Equals(productRepoUri, StringComparison.OrdinalIgnoreCase));
            ((ManifestRecord)affectedRecord).RemoteUri = productRepoForkUri;

            return JsonSerializer.Serialize(sourceManifest, JsonSerializerOptions);
        });
    }

    private async Task UpdateRemoteVmrForkFileAsync(
        string branch,
        string filePath,
        Func<string, string> contentUpdater)
    {
        logger.LogInformation("Updating file {file} on branch {branch} in the VMR fork", filePath, branch);

        // Fetch remote file and replace the product repo URI with the repo we're testing on        
        IReadOnlyList<RepositoryContent> repositoryContents = await ghClient.Repository.Content.GetAllContentsByRef(
            MaestroAuthTestOrgName,
            VmrForkRepoName,
            filePath,
            branch);

        if (repositoryContents.Count == 0)
        {
            throw new Exception($"Failed to find file {filePath} in {MaestroAuthTestOrgName}/{VmrForkRepoName} on branch {branch}");
        }

        UpdateFileRequest update = new(
            $"Updated {filePath}",
            contentUpdater(repositoryContents[0].Content),
            repositoryContents[0].Sha,
            branch);

        await ghClient.Repository.Content.UpdateFile(
            MaestroAuthTestOrgName,
            VmrForkRepoName,
            filePath,
            update);
    }

    protected async Task<AsyncDisposableValue<string>> PrepareProductRepoForkAsync(
        string productRepoUri,
        string productRepoForkUri,
        string productRepoBranch,
        bool skipCleanup)
    {
        logger.LogInformation("Preparing product repo {repo} fork", productRepoUri);
        (var name, var org) = GitRepoUrlUtils.GetRepoNameAndOwner(productRepoUri);
        // Check if the product repo fork already exists
        var allRepos = await ghClient.Repository.GetAllForOrg(MaestroAuthTestOrgName);

        // If we already have a fork in maestro-auth-test, sync the branch we need with the source
        if (allRepos.Any(repo => repo.HtmlUrl.Equals(productRepoForkUri, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogInformation("Product repo fork {fork} already exists, syncing branch {branch} with source", productRepoForkUri, productRepoBranch);
            await SyncForkAsync(org, name, productRepoBranch);
        }
        // If we don't, create a fork
        else
        {
            logger.LogInformation("Forking product repo {source} to fork {fork}", productRepoUri, productRepoForkUri);
            await ghClient.Repository.Forks.Create(org, name, new NewRepositoryFork { Organization = MaestroAuthTestOrgName });

            // The Octokit client doesn't wait for the fork to actually be created, so we should wait a bit to make sure it's there
            await Task.Delay(TimeSpan.FromSeconds(15));
        }

        return await CreateTmpBranchAsync(name, productRepoBranch, skipCleanup);
    }

    protected async Task SyncForkAsync(string originOrg, string repoName, string branch)
    {
        logger.LogInformation("Syncing fork {fork} branch {branch} with upstream repo {upstream}", $"{MaestroAuthTestOrgName}/{repoName}", branch, $"{originOrg}/{repoName}");
        var reference = $"heads/{branch}";
        var upstream = await ghClient.Git.Reference.Get(originOrg, repoName, reference);

        try
        {
            // Try to update the reference in the fork
            await ghClient.Git.Reference.Update(MaestroAuthTestOrgName, repoName, reference, new ReferenceUpdate(upstream.Object.Sha, true));
            logger.LogInformation("Updated existing branch {branch} in fork to commit {sha}", branch, upstream.Object.Sha);
        }
        catch (ApiValidationException ex) when (ex.Message.Contains("Reference does not exist"))
        {
            // If the branch doesn't exist in the fork, create it
            logger.LogInformation("Branch {branch} doesn't exist in fork yet, creating it with commit {sha}", branch, upstream.Object.Sha);
            var newBranch = new NewReference($"refs/heads/{branch}", upstream.Object.Sha);
            await ghClient.Git.Reference.Create(MaestroAuthTestOrgName, repoName, newBranch);
        }
    }

    protected async Task<AsyncDisposableValue<string>> CreateTmpBranchAsync(string repoName, string originalBranch, bool skipCleanup)
    {
        var newBranchName = $"repro/{Guid.NewGuid()}";
        logger.LogInformation("Creating temporary branch {branch} in {repo}", newBranchName, $"{MaestroAuthTestOrgName}/{repoName}");

        var baseBranch = await ghClient.Git.Reference.Get(MaestroAuthTestOrgName, repoName, $"heads/{originalBranch}");
        var newBranch = new NewReference($"refs/heads/{newBranchName}", baseBranch.Object.Sha);
        await ghClient.Git.Reference.Create(MaestroAuthTestOrgName, repoName, newBranch);

        return AsyncDisposableValue.Create(newBranchName, async () =>
        {
            if (skipCleanup)
            {
                return;
            }

            logger.LogInformation("Cleaning up temporary branch {branchName}", newBranchName);
            try
            {
                await DeleteGitHubBranchAsync(repoName, newBranchName);
            }
            catch
            {
                // If this throws an exception the most likely cause is that the branch was already deleted
            }
        });
    }

    protected static List<AssetData> CreateAssetDataFromBuild(Build build)
    {
        return build.Assets
            .Select(asset => new AssetData(false)
            {
                Name = asset.Name,
                Version = asset.Version,
                Locations = asset.Locations?.Select(location => new AssetLocationData(location.Type) { Location = location.Location }).ToList()
            })
            .ToList();
    }

    protected async Task<string> GetLatestCommitInBranch(string owner, string repo, string branch)
    {
        var reference = await ghClient.Git.Reference.Get(owner, repo, $"heads/{branch}");
        return reference.Object.Sha;
    }

    protected async Task<GitHubCommit> GetCommit(string sourceRepoOwner, string sourceRepoName, string commit)
        => await ghClient.Repository.Commit.Get(sourceRepoOwner, sourceRepoName, commit);
}
