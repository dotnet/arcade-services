// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octokit;
using GitHubClient = Octokit.GitHubClient;

namespace ProductConstructionService.ReproTool;

internal class FlatFlowTestOperation(
    VmrDependencyResolver vmrDependencyResolver,
    ILogger<FlatFlowTestOperation> logger,
    GitHubClient ghClient,
    BuildAssetRegistryContext context,
    DarcProcessManager darcProcessManager,
    IBarApiClient prodBarClient,
    [FromKeyedServices("local")] IProductConstructionServiceApi localPcsApi)
{
    private const string MaestroAuthTestOrgName = "maestro-auth-test";
    private const string VmrForkRepoName = "dotnet";
    private const string VmrForkUri = $"https://github.com/{MaestroAuthTestOrgName}/{VmrForkRepoName}";
    private const string ProductRepoFormat = $"https://github.com/{MaestroAuthTestOrgName}/";
    private const long InstallationId = 289474;
    private const string SourceMappingsPath = $"{VmrInfo.SourceDirName}/{VmrInfo.SourceMappingsFileName}";
    private const string SourceManifestPath = $"{VmrInfo.SourceDirName}/{VmrInfo.SourceManifestFileName}";
    private const string DarcPRBranchPrefix = "darc";

    internal async Task TestFlatFlow()
    {
        await darcProcessManager.InitializeAsync();

        var vmrDependencies = await vmrDependencyResolver.GetVmrDependenciesAsync(
            "https://github.com/dotnet/dotnet",
            "https://github.com/dotnet/sdk",
            "main");

        vmrDependencies = vmrDependencies.Where(d => d.Mapping.Name == "runtime").ToList();

        logger.LogInformation("Preparing VMR fork");
        // Sync the VMR fork branch
        await SyncForkAsync("dotnet", "dotnet", "main");
        // Check if the user has the forked VMR in local DB
        await AddRepositoryToBarIfMissingAsync(VmrForkUri);

        //var vmrTestBranch = await CreateTmpBranchAsync(VmrForkRepoName, "main", true);
        var vmrTestBranch = "repro/21283b94-b656-432d-95ce-cb603b39b353";

        var channelName = $"repro-{Guid.NewGuid()}";
        await using var channel = await darcProcessManager.CreateTestChannelAsync(channelName, true);

        foreach (var vmrDependency in vmrDependencies)
        {          
            var productRepoForkUri = $"{ProductRepoFormat}{vmrDependency.Mapping.Name}";
            if (vmrDependency.Mapping.Name == "nuget-client")
            {
                productRepoForkUri = $"{ProductRepoFormat}nuget.client";
            }
            var latestBuild = await prodBarClient.GetLatestBuildAsync(vmrDependency.Mapping.DefaultRemote, vmrDependency.Channel.Channel.Id);

            var productRepoTmpBranch = await PrepareProductRepoForkAsync(vmrDependency.Mapping.DefaultRemote, productRepoForkUri, latestBuild.GetBranch(), false);

            var localBuild = await CreateBuildAsync(
                productRepoForkUri,
                productRepoTmpBranch.Value,
                latestBuild.Commit,
                []);

            await PrepareVmrForkAsync(
                vmrTestBranch,
                vmrDependency.Mapping.DefaultRemote, productRepoForkUri, true);

            await using var testSubscription = await darcProcessManager.CreateSubscriptionAsync(
                channel: channelName,
                sourceRepo: productRepoForkUri,
                targetRepo: VmrForkUri,
                targetBranch: vmrTestBranch,
                sourceDirectory: null,
                targetDirectory: vmrDependency.Mapping.Name,
                skipCleanup: true);

            var testChannel = (await localPcsApi.Channels.ListChannelsAsync()).Where(channel => channel.Name == channelName).First();
            await localPcsApi.Channels.AddBuildToChannelAsync(localBuild.Id, testChannel!.Id);

            await TriggerSubscriptionAsync(testSubscription.Value);
        }
    }

    private async Task SyncForkAsync(string originOrg, string repoName, string branch)
    {
        logger.LogInformation("Syncing fork {fork} branch {branch} with upstream repo {upstream}", $"{MaestroAuthTestOrgName}/{repoName}", branch, $"{originOrg}/{repoName}");
        var reference = $"heads/{branch}";
        var upstream = await ghClient.Git.Reference.Get(originOrg, repoName, reference);
        await ghClient.Git.Reference.Update(MaestroAuthTestOrgName, repoName, reference, new ReferenceUpdate(upstream.Object.Sha, true));
    }

    private async Task PrepareVmrForkAsync(
        string branch,
        string productRepoUri,
        string productRepoForkUri,
        bool skipCleanup)
    {
        // Fetch source mappings and source manifest files and replace the mapping for the repo we're testing on
        logger.LogInformation("Updating source mappings and source manifest files in VMR fork to replace original product repo mapping with fork mapping");
        await UpdateRemoteVmrForkFileAsync(branch, productRepoUri, productRepoForkUri, SourceMappingsPath);
        await UpdateRemoteVmrForkFileAsync(branch, productRepoUri, productRepoForkUri, SourceManifestPath);
    }

    private async Task AddRepositoryToBarIfMissingAsync(string repositoryName)
    {
        if ((await context.Repositories.FirstOrDefaultAsync(repo => repo.RepositoryName == repositoryName)) == null)
        {
            logger.LogInformation("Repo {repo} missing in local BAR. Adding an entry for it", repositoryName);
            context.Repositories.Add(new Maestro.Data.Models.Repository
            {
                RepositoryName = repositoryName,
                InstallationId = InstallationId
            });
            await context.SaveChangesAsync();
        }
    }

    private async Task UpdateRemoteVmrForkFileAsync(string branch, string productRepoUri, string productRepoForkUri, string filePath)
    {
        logger.LogInformation("Updating file {file} on branch {branch} in the VMR fork", filePath, branch);
        // Fetch remote file and replace the product repo URI with the repo we're testing on        
        var sourceMappingsFile = (await ghClient.Repository.Content.GetAllContentsByRef(
                MaestroAuthTestOrgName,
                VmrForkRepoName,
                filePath,
                branch))
            .FirstOrDefault()
            ?? throw new Exception($"Failed to find file {SourceMappingsPath} in {MaestroAuthTestOrgName}" +
                                   $"/{VmrForkRepoName} on branch {SourceMappingsPath}");

        // Replace the product repo uri with the forked one
        var updatedSourceMappings = sourceMappingsFile.Content.Replace(productRepoUri, productRepoForkUri);
        UpdateFileRequest update = new(
            $"Update {productRepoUri} source mapping",
            updatedSourceMappings,
            sourceMappingsFile.Sha,
            branch);

        await ghClient.Repository.Content.UpdateFile(
            MaestroAuthTestOrgName,
            VmrForkRepoName,
            filePath,
            update);
    }

    private async Task<AsyncDisposableValue<string>> CreateTmpBranchAsync(string repoName, string originalBranch, bool skipCleanup)
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
            await Task.Delay(1);
            return;
        });
    }

    private async Task<AsyncDisposableValue<string>> PrepareProductRepoForkAsync(
        string productRepoUri,
        string productRepoForkUri,
        string productRepoBranch,
        bool skipCleanup)
    {
        logger.LogInformation("Preparing product repo {repo} fork", productRepoUri);
        (var name, var org) = GitRepoUrlParser.GetRepoNameAndOwner(productRepoUri);
        // Check if the product repo fork already exists
        var allRepos = await ghClient.Repository.GetAllForOrg(MaestroAuthTestOrgName);

        // If we already have a fork in maestro-auth-test, sync the branch we need with the source
        if (allRepos.FirstOrDefault(repo => repo.HtmlUrl == productRepoForkUri) != null)
        {
            logger.LogInformation("Product repo fork {fork} already exists, syncing branch {branch} with source", productRepoForkUri, productRepoBranch);
            await SyncForkAsync(org, name, productRepoBranch);
        }
        // If we don't, create a fork
        else
        {
            logger.LogInformation("Forking product repo {source} to fork {fork}", productRepoUri, productRepoForkUri);
            await ghClient.Repository.Forks.Create(org, name, new NewRepositoryFork { Organization = MaestroAuthTestOrgName });

            await Task.Delay(TimeSpan.FromSeconds(15));
        }
        await AddRepositoryToBarIfMissingAsync(productRepoForkUri);

        return await CreateTmpBranchAsync(name, productRepoBranch, skipCleanup);
    }

    private async Task<Build> CreateBuildAsync(string repositoryUrl, string branch, string commit, List<AssetData> assets)
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

    private async Task TriggerSubscriptionAsync(string subscriptionId)
    {
        logger.LogInformation("Triggering subscription {subscriptionId}", subscriptionId);
        await localPcsApi.Subscriptions.TriggerSubscriptionAsync(default, Guid.Parse(subscriptionId));
    }
}
